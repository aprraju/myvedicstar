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
    if ($status -in 'Failed', 'Stopped') { throw "Migration $execution $status. API deployment stopped." }
    Start-Sleep -Seconds 5
}
throw "Migration $execution did not succeed within 10 minutes."
