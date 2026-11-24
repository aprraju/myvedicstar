[CmdletBinding()]
param(
    [string] $RegistryName = $env:REGISTRY_NAME,
    [string] $ImageTag = $env:IMAGE_TAG,
    [string] $ResourceGroup = $env:RESOURCE_GROUP_NAME,
    [string] $JobName = 'caj-myvedicstar-migrate-prod',
    [string] $SubscriptionId = $env:AZURE_SUBSCRIPTION_ID,
    [string] $JobResourceId = $env:MIGRATION_JOB_RESOURCE_ID
)
. "$PSScriptRoot/_common.ps1"

function Get-MigrationJobLogTail {
    # Retrieves the job execution's own logs and prints a bounded tail into the step output.
    # Never throws and never changes the caller's verdict: a log we cannot fetch is reported as unavailable
    # rather than replacing the real failure with a diagnostics error.
    param([string] $SubscriptionId, [string] $ResourceGroup, [string] $JobName, [string] $Execution)

    $container = $env:MIGRATION_JOB_CONTAINER
    if (-not $container) { $container = 'migrator' }
    $tailLines = 100

    Write-Host ''
    Write-Host "---- Migration job log: $Execution (last $tailLines lines) ----"
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $logs = & az containerapp job logs show --subscription $SubscriptionId --name $JobName `
            --resource-group $ResourceGroup --execution $Execution --container $container `
            --tail $tailLines --format text 2>&1
        $logExit = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previous }

    if ($logExit -eq 0 -and $logs) {
        $logs | ForEach-Object { Write-Host $_ }
        Write-Host '---- end of migration job log ----'
        Write-Host ''
        return
    }

    # Api#214 follow-on: the az error was captured by 2>&1 but only printed on the success path, so a
    # failed fetch reported 'unavailable' and discarded its own reason - the same defect as the discarded
    # EF error, one layer down. Print what the CLI actually said, before anything else.
    Write-Host "Could not read logs for container '$container' (exit $logExit). The Azure CLI said:"
    if ($logs) {
        $logs | ForEach-Object { Write-Host "    $_" }
    }
    else {
        Write-Host '    (no output was captured from the CLI)'
    }

    # The container name belongs to the job template, not to this script, so a wrong guess is possible.
    # Say what the job actually declares. The extension notice ('WARNING: The behavior of this command has
    # been altered by the following extension: containerapp') is prepended to stdout and was being joined
    # with the names, so the list read 'WARNING: ..., migrator' - accurate names, unreadable line. Filter
    # the notice rather than accept a diagnostic that needs decoding.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $declared = & az containerapp job show --subscription $SubscriptionId --name $JobName `
            --resource-group $ResourceGroup --query 'properties.template.containers[].name' --output tsv 2>&1
        $declaredExit = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previous }
    $containerNames = @(if ($declaredExit -eq 0) {
            @($declared) | ForEach-Object { "$_".Trim() } | Where-Object { $_ -and $_ -notmatch '^WARNING:' }
        })
    if ($containerNames.Count -gt 0) {
        Write-Host "Containers declared by job '$JobName': $($containerNames -join ', ')"
        if ($containerNames -notcontains $container) {
            Write-Host "'$container' is not among them; set MIGRATION_JOB_CONTAINER to one of the names above."
        }
        else {
            Write-Host "'$container' IS among them, so the container name is not the problem - the failure is the CLI call itself (see the message above)."
        }
    }
    Write-Host 'Diagnostics above are unavailable; the migration failure itself is unaffected.'
    Write-Host "---- end of migration job log ($Execution) ----"
    Write-Host ''
}

Assert-Required $RegistryName 'RegistryName'
Initialize-AzCli
if ($JobResourceId) {
    if ($JobResourceId -notmatch '^/subscriptions/([^/]+)/resourceGroups/([^/]+)/providers/Microsoft\.App/jobs/([^/]+)$') {
        throw 'MIGRATION_JOB_RESOURCE_ID must be a full Microsoft.App/jobs resource ID.'
    }
    $SubscriptionId = $Matches[1]
    $ResourceGroup = $Matches[2]
    $JobName = $Matches[3]
}
if (-not $SubscriptionId) { $SubscriptionId = Get-AzSubscriptionId }
Assert-Required $ResourceGroup 'ResourceGroup'
Assert-ImageTag $ImageTag
Write-Host "Migration target: subscription=$SubscriptionId resourceGroup=$ResourceGroup job=$JobName"
& az containerapp job show --subscription $SubscriptionId --name $JobName --resource-group $ResourceGroup --query id --output tsv
if ($LASTEXITCODE) { throw "Cannot read migration job '$JobName' in subscription '$SubscriptionId', resource group '$ResourceGroup'. Check the target and pipeline identity permissions." }
& az containerapp job update --subscription $SubscriptionId --name $JobName --resource-group $ResourceGroup --image "$RegistryName.azurecr.io/myvedicstar-migrator:$ImageTag" --output none
if ($LASTEXITCODE) { throw 'Migration job update failed.' }
$execution = & az containerapp job start --subscription $SubscriptionId --name $JobName --resource-group $ResourceGroup --query name --output tsv
if ($LASTEXITCODE -or -not $execution) { throw 'Migration job start failed.' }
for ($attempt = 0; $attempt -lt 120; $attempt++) {
    $status = & az containerapp job execution show --subscription $SubscriptionId --name $JobName --resource-group $ResourceGroup --job-execution-name $execution --query properties.status --output tsv
    if ($LASTEXITCODE) { throw 'Migration status lookup failed.' }
    if ($status -eq 'Succeeded') { Write-Host "Migration $execution succeeded."; return }
    if ($status -in 'Failed', 'Stopped') {
        # Api#214: the job's own stdout carries the EF error (migration name, failing statement, SQLSTATE),
        # and nothing in this script previously fetched it. Printing it before the throw is the whole point of
        # this branch: the verdict is unchanged, only the explanation stops being discarded.
        Get-MigrationJobLogTail -SubscriptionId $SubscriptionId -ResourceGroup $ResourceGroup `
            -JobName $JobName -Execution $execution
        throw "Migration $execution $status. API deployment stopped. The failed job's log tail is printed above, in this step log."
    }
    Start-Sleep -Seconds 5
}
throw "Migration $execution did not succeed within 10 minutes."
