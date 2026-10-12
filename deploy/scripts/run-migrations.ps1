[CmdletBinding()]
param(
    [string] $RegistryName = $env:REGISTRY_NAME,
    [string] $ImageTag = $env:IMAGE_TAG,
    [string] $ResourceGroup = $env:RESOURCE_GROUP_NAME,
    [string] $JobName = 'caj-myvedicstar-migrate-prod'
)
. "$PSScriptRoot/_common.ps1"
Assert-Required $RegistryName 'RegistryName'
Assert-Required $ResourceGroup 'ResourceGroup'
Assert-ImageTag $ImageTag
& az containerapp job update --name $JobName --resource-group $ResourceGroup --image "$RegistryName.azurecr.io/myvedicstar-migrator:$ImageTag" --output none
if ($LASTEXITCODE) { throw 'Migration job update failed.' }
$execution = & az containerapp job start --name $JobName --resource-group $ResourceGroup --query name --output tsv
if ($LASTEXITCODE -or -not $execution) { throw 'Migration job start failed.' }
for ($attempt = 0; $attempt -lt 120; $attempt++) {
    $status = & az containerapp job execution show --name $JobName --resource-group $ResourceGroup --job-execution-name $execution --query properties.status --output tsv
    if ($LASTEXITCODE) { throw 'Migration status lookup failed.' }
    if ($status -eq 'Succeeded') { Write-Host "Migration $execution succeeded."; return }
    if ($status -in 'Failed', 'Stopped') { throw "Migration $execution $status. API deployment stopped." }
    Start-Sleep -Seconds 5
}
throw "Migration $execution did not succeed within 10 minutes."
