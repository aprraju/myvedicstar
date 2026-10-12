# Execute the real migration runner with a fake Azure CLI; never touches Azure.
$ErrorActionPreference = 'Stop'
$runner = Join-Path $PSScriptRoot '../../deploy/scripts/run-migrations.ps1'
$global:migrationCalls = [Collections.Generic.List[object]]::new()
$global:migrationStatus = 'Succeeded'
function global:az {
    $global:LASTEXITCODE = 0
    $global:migrationCalls.Add(@($args))
    if ($args[0] -eq 'account') { return 'wrong-subscription' }
    if ($args[2] -eq 'show') { return '/subscriptions/target-sub/resourceGroups/target-rg/providers/Microsoft.App/jobs/target-job' }
    if ($args[2] -eq 'start') { return 'execution-1' }
    if ($args[2] -eq 'execution') { return $global:migrationStatus }
}
try {
    $target = '/subscriptions/target-sub/resourceGroups/target-rg/providers/Microsoft.App/jobs/target-job'
    & $runner -RegistryName testregistry -ImageTag test-tag -ResourceGroup wrong-rg -SubscriptionId wrong-sub -JobResourceId $target
    foreach ($call in $global:migrationCalls | Where-Object { $_[0] -eq 'containerapp' }) {
        foreach ($pair in @(@('--subscription', 'target-sub'), @('--resource-group', 'target-rg'), @('--name', 'target-job'))) {
            $index = [Array]::IndexOf($call, $pair[0])
            if ($index -lt 0 -or $call[$index + 1] -ne $pair[1]) { throw "Incorrect migration target: $($call -join ' ')" }
        }
    }
    $global:migrationStatus = 'Failed'
    $rejected = $false
    try { & $runner -RegistryName testregistry -ImageTag test-tag -JobResourceId $target }
    catch { $rejected = $_.Exception.Message -like '*API deployment stopped*' }
    if (-not $rejected) { throw 'Failed migration did not stop deployment.' }
    $before = $global:migrationCalls.Count
    $rejected = $false
    try { & $runner -RegistryName testregistry -ImageTag test-tag -JobResourceId '/invalid' }
    catch { $rejected = $_.Exception.Message -like '*full Microsoft.App/jobs*' }
    if (-not $rejected) { throw 'Invalid migration resource ID accepted.' }
    if (@($global:migrationCalls | Select-Object -Skip $before | Where-Object { $_[0] -eq 'containerapp' }).Count) { throw 'Invalid target caused a job operation.' }
    Write-Host 'Migration contracts passed: explicit resource targeting, failed-execution gate, invalid-target rejection.'
}
finally {
    Remove-Item Function:global:az
    Remove-Variable migrationCalls, migrationStatus -Scope Global
}
