# Runs the real deployment script against a fake Azure CLI. No Azure resources are contacted or changed.
$ErrorActionPreference = 'Stop'
$repositoryPath = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$config = Get-Content (Join-Path $repositoryPath 'deploy/environments/prod.json') -Raw | ConvertFrom-Json
# Keep both hosts and release environment aligned with the shared API app registration.
$expectedApiId = 'fdaa908a-4c0b-4d63-a6d1-de637e0edc4e'
if ($config.settings.Authentication__EntraExternalId__ClientId -ne $expectedApiId) { throw 'Production API audience does not match the shared API registration.' }
foreach ($hostSettings in @('src/MyVedicStar.API/appsettings.Development.json', 'src/MyVedicStar.API/appsettings.Production.json', 'src/MyVedicDay.Api/appsettings.Development.json', 'src/MyVedicDay.Api/appsettings.Production.json')) {
    $hostConfig = Get-Content (Join-Path $repositoryPath $hostSettings) -Raw | ConvertFrom-Json
    if ($hostConfig.Authentication.EntraExternalId.ClientId -ne $expectedApiId) { throw "API audience mismatch in $hostSettings" }
}
$identityPath = "/subscriptions/test-sub/resourceGroups/test-rg/providers/Microsoft.ManagedIdentity/userAssignedIdentities/$($config.keyVaultIdentityName)"
$global:fakeApp = [pscustomobject]@{
    identity = @{ userAssignedIdentities = @{ $identityPath = @{} } }
    properties = @{
        provisioningState = 'Succeeded'
        latestRevisionName = 'old-revision'
        latestReadyRevisionName = 'old-revision'
        configuration = @{
            activeRevisionsMode = 'Single'
            ingress = @{ fqdn = 'vedicstar.example.com'; targetPort = 8080; external = $true }
            registries = @(@{ server = 'testregistry.azurecr.io'; identity = $identityPath })
            secrets = @(@{ name = 'existing-secret'; keyVaultUrl = 'https://example.vault.azure.net/secrets/existing' })
        }
        template = @{
            scale = @{ minReplicas = 2; maxReplicas = 5; rules = @(@{ name = 'http'; http = @{ metadata = @{ concurrentRequests = '50' } } }) }
            containers = @(
                @{
                    name = 'api'; image = 'old-image'
                    resources = @{ cpu = 0.5; memory = '1Gi' }
                    env = @(
                        @{ name = 'UNRELATED_SETTING'; value = 'preserve-me' }
                        @{ name = 'Database__ConnectionString'; secretRef = 'existing-secret' }
                        @{ name = 'Database__Host'; value = 'postgres.example.com' }
                        @{ name = 'Database__Name'; value = 'myvedicstar' }
                        @{ name = 'Database__Username'; value = 'api-identity' }
                        @{ name = 'Authentication__EntraExternalId__Audience'; value = 'retired' }
                    )
                }
                @{ name = 'sidecar'; image = 'sidecar-image'; resources = @{ cpu = 0.25; memory = '0.5Gi' } }
            )
            revisionSuffix = 'old'
        }
    }
}
$global:capturedPatch = $null
$global:mutationCount = 0
$global:fakeImagePresent = $true
$global:fakeRunningState = 'Running'
function az {
    $arguments = @($args)
    $global:LASTEXITCODE = 0
    if ($arguments[0] -eq 'account') {
        if ($arguments -contains '--query') { return 'test-sub' }
        return
    }
    if ($arguments[0] -eq 'acr') {
        if ($arguments[1] -eq 'show') { return 'testregistry.azurecr.io' }
        if (-not $global:fakeImagePresent) { $global:LASTEXITCODE = 1 }
        return
    }
    if ($arguments[0] -eq 'rest') {
        $method = $arguments[[array]::IndexOf($arguments, '--method') + 1]
        $url = $arguments[[array]::IndexOf($arguments, '--url') + 1]
        if ($method -eq 'get') {
            if ($url -match '/Microsoft.KeyVault/vaults/') {
                return (@{ properties = @{ vaultUri = 'https://example.vault.azure.net' } } | ConvertTo-Json)
            }
            if ($url -match '/revisions/') {
                return (@{ properties = @{ runningState = $global:fakeRunningState; healthState = 'Healthy'; provisioningState = 'Succeeded' } } | ConvertTo-Json -Depth 10)
            }
            return ($global:fakeApp | ConvertTo-Json -Depth 30)
        }
        if ($method -eq 'patch') {
            $bodyPath = $arguments[[array]::IndexOf($arguments, '--body') + 1].Substring(1)
            $global:capturedPatch = Get-Content -LiteralPath $bodyPath -Raw | ConvertFrom-Json
            $global:fakeApp.properties.latestRevisionName = 'new-revision'
            $global:fakeApp.properties.latestReadyRevisionName = 'new-revision'
            $global:mutationCount++
            return
        }
    }
    if ($arguments[0] -eq 'containerapp' -and $arguments[1] -eq 'secret' -and $arguments[2] -eq 'set') {
        if ($arguments -notcontains 'microsoft-graph-client-secret=keyvaultref:https://example.vault.azure.net/secrets/microsoft-graph-client-secret,identityref:/subscriptions/test-sub/resourceGroups/test-rg/providers/Microsoft.ManagedIdentity/userAssignedIdentities/id-myvedicstar-api-prod') {
            throw 'Graph credential must reference Key Vault through the existing managed identity.'
        }
        return
    }
    throw "Unexpected Azure command in deployment test: $($arguments -join ' ')"
}
function Start-Sleep { param([int] $Seconds) }
function Invoke-WebRequest { param([string] $Uri, [switch] $UseBasicParsing, [int] $TimeoutSec) return @{ StatusCode = 200 } }
function Assert-Deployment([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}
try {
    & (Join-Path $repositoryPath 'deploy/scripts/deploy-app.ps1') -Environment prod -ResourceGroup test-rg -ContainerAppName ca-myvedicstar-api-prod -RegistryName testregistry -ImageTag test-commit
    Assert-Deployment ($global:mutationCount -eq 1) 'Deployment must issue one template patch.'
    Assert-Deployment ($global:capturedPatch.properties.PSObject.Properties.Name.Count -eq 1) 'Networking and configuration must not be patched.'
    $template = $global:capturedPatch.properties.template
    $api = @($template.containers | Where-Object name -eq 'api')[0]
    Assert-Deployment (@($api.env | Where-Object name -eq 'Integrations__MicrosoftGraph__Enabled')[0].value -eq 'true') 'Production Graph must be enabled.'
    Assert-Deployment (@($api.env | Where-Object name -eq 'Integrations__MicrosoftGraph__CredentialType')[0].value -eq 'ClientSecret') 'Wrong Graph credential type.'
    Assert-Deployment (@($api.env | Where-Object name -eq 'Integrations__MicrosoftGraph__TenantId')[0].value -eq $config.settings.Authentication__EntraExternalId__TenantId) 'Graph tenant mismatch.'
    Assert-Deployment (@($api.env | Where-Object name -eq 'Integrations__MicrosoftGraph__ClientId')[0].value -eq $expectedApiId) 'Graph client mismatch.'
    Assert-Deployment (@($api.env | Where-Object name -eq 'Integrations__MicrosoftGraph__ClientSecret')[0].secretRef -eq 'microsoft-graph-client-secret') 'Graph secret reference missing.'
    Assert-Deployment ($api.image -eq 'testregistry.azurecr.io/myvedicstar-api:test-commit') 'Wrong combined image.'
    Assert-Deployment ($api.resources.cpu -eq 0.5 -and $api.resources.memory -eq '1Gi') 'CPU and memory changed.'
    Assert-Deployment ($template.scale.minReplicas -eq 2 -and $template.scale.maxReplicas -eq 5) 'Scaling changed.'
    Assert-Deployment ($template.scale.rules[0].http.metadata.concurrentRequests -eq '50') 'Scale rule changed.'
    Assert-Deployment (@($template.containers | Where-Object name -eq 'sidecar')[0].image -eq 'sidecar-image') 'Unrelated container changed.'
    Assert-Deployment (@($api.env | Where-Object name -eq 'UNRELATED_SETTING')[0].value -eq 'preserve-me') 'Unrelated environment setting changed.'
    Assert-Deployment (@($api.env | Where-Object name -eq 'Database__ConnectionString')[0].secretRef -eq 'existing-secret') 'Existing secret reference changed.'
    Assert-Deployment (@($api.env | Where-Object name -eq 'Authentication__EntraExternalId__Audience').Count -eq 0) 'Retired setting was retained.'
    Assert-Deployment (@($api.probes | Where-Object type -eq 'Readiness')[0].httpGet.path -eq '/health/ready') 'Wrong readiness path.'
    Assert-Deployment (@($api.probes | Where-Object type -eq 'Liveness')[0].httpGet.path -eq '/health/live') 'Wrong liveness path.'
    Assert-Deployment ($template.revisionSuffix -match '^r[0-9a-f]{1,19}$') 'Deployment must explicitly supply a bounded unique revision suffix.'
    Assert-Deployment (("ca-myvedicstar-api-prod--" + $template.revisionSuffix).Length -le 54) 'Combined production revision name exceeds Azure limit.'
    Assert-Deployment ($template.revisionSuffix -ne $global:fakeApp.properties.template.revisionSuffix) 'Old revision suffix retained.'
    $global:fakeImagePresent = $false
    $missingImageRejected = $false
    try {
        & (Join-Path $repositoryPath 'deploy/scripts/deploy-app.ps1') -Environment prod -ResourceGroup test-rg -ContainerAppName vedicstar -RegistryName testregistry -ImageTag missing-image
    } catch { $missingImageRejected = $_.Exception.Message -like '*was not found*' }
    Assert-Deployment $missingImageRejected 'Missing images must be rejected before deployment.'
    Assert-Deployment ($global:mutationCount -eq 1) 'Missing image caused a mutation.'
    $global:fakeImagePresent = $true
    $global:fakeApp.properties.configuration.activeRevisionsMode = 'Multiple'
    $multipleRejected = $false
    try {
        & (Join-Path $repositoryPath 'deploy/scripts/deploy-app.ps1') -Environment prod -ResourceGroup test-rg -ContainerAppName vedicstar -RegistryName testregistry -ImageTag test-commit
    } catch { $multipleRejected = $_.Exception.Message -like '*single revision mode*' }
    Assert-Deployment $multipleRejected 'Multiple revision mode must not falsely verify traffic on an old revision.'
    Assert-Deployment ($global:mutationCount -eq 1) 'Invalid revision mode caused a mutation.'
    $global:fakeApp.properties.configuration.activeRevisionsMode = 'Single'
    $savedEnvironment = $global:fakeApp.properties.template.containers[0].env
    $global:fakeApp.properties.template.containers[0].env = @($savedEnvironment | Where-Object name -ne 'Database__Host')
    $missingDatabaseRejected = $false
    try {
        & (Join-Path $repositoryPath 'deploy/scripts/deploy-app.ps1') -Environment prod -ResourceGroup test-rg -ContainerAppName vedicstar -RegistryName testregistry -ImageTag test-commit
    } catch { $missingDatabaseRejected = $_.Exception.Message -like '*Managed-identity PostgreSQL requires Database__Host*' }
    Assert-Deployment $missingDatabaseRejected 'Enabled persistence without database configuration must be rejected before deployment.'
    Assert-Deployment ($global:mutationCount -eq 1) 'Missing database configuration caused a mutation.'
    $global:fakeApp.properties.template.containers[0].env = $savedEnvironment
    $global:fakeRunningState = 'ActivationFailed'
    $global:fakeApp.properties.latestRevisionName = 'old-revision'
    $activationRejected = $false
    try {
        & (Join-Path $repositoryPath 'deploy/scripts/deploy-app.ps1') -Environment prod -ResourceGroup test-rg -ContainerAppName vedicstar -RegistryName testregistry -ImageTag test-commit
    } catch { $activationRejected = $_.Exception.Message -like '*failed to start*ActivationFailed*' }
    Assert-Deployment $activationRejected 'ActivationFailed must fail immediately, even if stale readiness metadata claims success.'
    $global:fakeRunningState = 'Running'
    $previousSuffix = ''
    foreach ($name in @('ca-myvedicstar-api-prod', ('a' * 32), 'ca-myvedicstar-api-prod')) {
        $global:fakeApp.properties.latestRevisionName = 'old-revision'
        & (Join-Path $repositoryPath 'deploy/scripts/deploy-app.ps1') -Environment prod -ResourceGroup test-rg -ContainerAppName $name -RegistryName testregistry -ImageTag test-commit
        $suffix = $global:capturedPatch.properties.template.revisionSuffix
        Assert-Deployment (("$name--$suffix").Length -le 54) 'Combined revision name exceeds Azure limit.'
        Assert-Deployment ($suffix -match '^[a-z][a-z0-9-]*[a-z0-9]$') 'Revision suffix has invalid characters.'
        Assert-Deployment ($suffix -ne $previousSuffix) 'Repeated deployment reused the revision suffix.'
        $previousSuffix = $suffix
    }
    Write-Host 'Deployment contract checks passed: image, probes, resources, scaling, environment, secrets, sidecar preservation and activation failure detection.'
}
finally {
    Remove-Variable fakeApp, capturedPatch, mutationCount, fakeImagePresent, fakeRunningState -Scope Global -ErrorAction SilentlyContinue
}
