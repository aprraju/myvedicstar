<#
.SYNOPSIS
    Releases an image to an existing (Terraform-managed) Azure Container App and waits until it is healthy.

.DESCRIPTION
    Changes only what the application owns:
      - container image
      - application environment variables from deploy/environments/<env>.json, merged with the ones
        Terraform already set (Terraform's values are kept unless the same name appears in the file);
        names listed under "removedSettings" are deleted
      - Key Vault secret references (read by the platform with the app's managed identity)
      - startup, liveness and readiness probes and the termination grace period
    Infrastructure (the app itself, identity, registry, Key Vault, network, scale, CPU/memory) is not touched.

    Every parameter can come from an environment variable, which is how GitHub Actions passes values
    without interpolating them into script text. In GitHub Actions the step outputs "app-url" and
    "revision" are set and a job summary is written.

.EXAMPLE
    ./deploy-app.ps1 -ResourceGroup rg-myvedicstar-prod-app-eus2 -ContainerAppName ca-myvedicstar-api-prod `
        -RegistryName crmyvedicstarprodxw4t -ImageTag 1a2b3c4d5e6f
#>
[CmdletBinding()]
param(
    [string] $Environment = $(if ($env:DEPLOY_ENVIRONMENT) { $env:DEPLOY_ENVIRONMENT } else { 'prod' }),
    [string] $ResourceGroup = $env:RESOURCE_GROUP_NAME,
    [string] $ContainerAppName = $env:CONTAINER_APP_NAME,
    [string] $RegistryName = $env:REGISTRY_NAME,
    # Defaults to the current git commit.
    [string] $ImageTag = $env:IMAGE_TAG
)

. "$PSScriptRoot/_common.ps1"

Assert-Required $ResourceGroup 'Resource group (-ResourceGroup or RESOURCE_GROUP_NAME)'
Assert-Required $ContainerAppName 'Container App name (-ContainerAppName or CONTAINER_APP_NAME)'
Assert-Required $RegistryName 'Registry name (-RegistryName or REGISTRY_NAME)'
if ([string]::IsNullOrWhiteSpace($ImageTag)) {
    $ImageTag = Get-DefaultImageTag
}
Assert-ImageTag $ImageTag

$config = Get-DeploymentConfig $Environment
Initialize-AzCli
$subscriptionId = Get-AzSubscriptionId

function Get-HealthEndpointPaths {
    # Single source of truth: the paths the application itself serves (HealthChecks section of appsettings.json).
    $appSettingsPath = Join-Path $RepositoryRoot 'src/MyVedicDay.Api/appsettings.json'
    $healthChecks = (Get-Content -Raw -LiteralPath $appSettingsPath | ConvertFrom-Json).HealthChecks
    Assert-Required $healthChecks.LivenessPath "HealthChecks:LivenessPath in $appSettingsPath"
    Assert-Required $healthChecks.ReadinessPath "HealthChecks:ReadinessPath in $appSettingsPath"
    return $healthChecks
}

function New-ContainerProbe([string] $Type, [string] $Path, [int] $Port, $Timing) {
    # Timing values (initialDelaySeconds, periodSeconds, timeoutSeconds, failureThreshold) come from the environment file.
    $probe = [ordered]@{ type = $Type; httpGet = @{ path = $Path; port = $Port } }
    foreach ($setting in $Timing.PSObject.Properties) {
        $probe[$setting.Name] = [int] $setting.Value
    }
    return [pscustomobject] $probe
}

# ---------------------------------------------------------------------------------------------------------------------
# Pre-flight checks: fail before anything changes
# ---------------------------------------------------------------------------------------------------------------------

$healthPaths = Get-HealthEndpointPaths

$appId = "/subscriptions/$subscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.App/containerApps/$ContainerAppName"
$app = Invoke-ArmGet $appId $ContainerAppsApiVersion
if ($null -eq $app) {
    throw "Container App '$ContainerAppName' was not found in '$ResourceGroup' (or this identity cannot read it). It is created by Terraform."
}

if ($app.properties.configuration.activeRevisionsMode -ne 'Single') {
    throw 'Shared deployment requires single revision mode so readiness verifies the released image. Configure revision mode in the infrastructure before deploying.'
}

if (-not (Test-ImageExists $RegistryName $ImageTag)) {
    throw "Image '$($ImageRepository):$ImageTag' was not found in $RegistryName (or this identity cannot read it). Run build-image.ps1 first."
}
$loginServer = "$(Invoke-Az acr show --name $RegistryName --query loginServer --output tsv)".Trim()
$registries = @($app.properties.configuration.registries | Where-Object { $_ -and $_.server -eq $loginServer })
if ($registries.Count -eq 0) {
    throw "Container App '$ContainerAppName' has no pull access configured for $loginServer. Add the registry (with its managed identity) in Terraform."
}

$identityId = "/subscriptions/$subscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.ManagedIdentity/userAssignedIdentities/$($config.keyVaultIdentityName)"
$assignedIdentities = @()
if ($app.identity -and $app.identity.userAssignedIdentities) {
    $assignedIdentities = @($app.identity.userAssignedIdentities.PSObject.Properties.Name)
}
if (-not ($assignedIdentities | Where-Object { $_ -ieq $identityId })) {
    throw "Managed identity '$($config.keyVaultIdentityName)' is not assigned to '$ContainerAppName'."
}

$secretMappings = @()
if ($config.secrets) {
    $secretMappings = @($config.secrets.PSObject.Properties | ForEach-Object { [pscustomobject]@{ EnvironmentVariable = $_.Name; SecretName = $_.Value } })
}

# Validate persistence configuration before creating a revision or updating secret references.
$persistenceSetting = $config.settings.PSObject.Properties['FeatureFlags__UserPersistence']
if ($persistenceSetting -and [string]$persistenceSetting.Value -ieq 'true') {
    $managedIdentitySetting = $config.settings.PSObject.Properties['Database__UseManagedIdentity']
    if ($managedIdentitySetting -and [string]$managedIdentitySetting.Value -ieq 'true') {
        foreach ($requiredDatabaseSetting in @('Database__Host', 'Database__Name', 'Database__Username')) {
            $ownedSetting = $config.settings.PSObject.Properties[$requiredDatabaseSetting]
            $existingSetting = @($app.properties.template.containers | Where-Object name -eq $config.containerName |
                ForEach-Object { $_.env } | Where-Object name -eq $requiredDatabaseSetting)
            $hasValue = if ($ownedSetting) { -not [string]::IsNullOrWhiteSpace([string]$ownedSetting.Value) }
                else { @($existingSetting | Where-Object { $_.PSObject.Properties['value'] -and -not [string]::IsNullOrWhiteSpace([string]$_.value) }).Count -gt 0 }
            if (-not $hasValue) { throw "Managed-identity PostgreSQL requires $requiredDatabaseSetting before deployment." }
        }
    }
    else {
    $connectionSetting = $config.settings.PSObject.Properties['Database__ConnectionString']
    $hasConnection = ($connectionSetting -and -not [string]::IsNullOrWhiteSpace([string]$connectionSetting.Value)) -or
        @($secretMappings | Where-Object EnvironmentVariable -eq 'Database__ConnectionString').Count -gt 0
    if (-not $connectionSetting -and -not $hasConnection) {
        $hasConnection = @($app.properties.template.containers | Where-Object name -eq $config.containerName |
            ForEach-Object { $_.env } | Where-Object {
                $_.name -eq 'Database__ConnectionString' -and
                (($_.PSObject.Properties['secretRef'] -and -not [string]::IsNullOrWhiteSpace([string]$_.secretRef)) -or
                 ($_.PSObject.Properties['value'] -and -not [string]::IsNullOrWhiteSpace([string]$_.value)))
            }).Count -gt 0
    }
    if (-not $hasConnection) {
        throw 'User persistence is enabled but Database__ConnectionString is missing. Configure a PostgreSQL connection-string secret, or enable Database__UseManagedIdentity with Host, Name and Username.'
    }
    }
}
$keyVaultUri = ''
if ($secretMappings.Count -gt 0) {
    $keyVault = Invoke-ArmGet (Get-KeyVaultResourceId $subscriptionId $config) $KeyVaultApiVersion
    if ($null -eq $keyVault) {
        throw "Key Vault '$($config.keyVault.name)' was not found in '$($config.keyVault.resourceGroup)' (or this identity cannot read it)."
    }
    $keyVaultUri = $keyVault.properties.vaultUri.TrimEnd('/')

    # Secret metadata only, through Azure Resource Manager: values are never read.
    $missing = @($secretMappings | Where-Object {
            $null -eq (Invoke-ArmGet "$(Get-KeyVaultResourceId $subscriptionId $config)/secrets/$($_.SecretName)" $KeyVaultApiVersion)
        } | ForEach-Object { $_.SecretName })
    if ($missing.Count -gt 0) {
        throw "Key Vault $($config.keyVault.name) is missing: $($missing -join ', '). A person with Key Vault Secrets Officer runs ./set-secrets.ps1 -Environment $Environment."
    }
}

# ---------------------------------------------------------------------------------------------------------------------
# Key Vault secret references (the platform resolves them with the app's managed identity)
# ---------------------------------------------------------------------------------------------------------------------

if ($secretMappings.Count -gt 0) {
    $secretArguments = @($secretMappings | ForEach-Object {
            "$($_.SecretName)=keyvaultref:$keyVaultUri/secrets/$($_.SecretName),identityref:$identityId"
        })
    Write-Host "Setting Key Vault references: $(($secretMappings | ForEach-Object { $_.SecretName }) -join ', ')"
    Invoke-Az containerapp secret set --name $ContainerAppName --resource-group $ResourceGroup --secrets @secretArguments --output none
}

# ---------------------------------------------------------------------------------------------------------------------
# New revision: image, environment variables and probes
# ---------------------------------------------------------------------------------------------------------------------

$app = Invoke-ArmGet $appId $ContainerAppsApiVersion
$template = $app.properties.template
$container = @($template.containers | Where-Object { $_.name -eq $config.containerName })
if ($container.Count -ne 1) {
    throw "Container '$($config.containerName)' not found in '$ContainerAppName'."
}
$container = $container[0]

$owned = @{}
foreach ($setting in $config.settings.PSObject.Properties) {
    $owned[$setting.Name] = [pscustomobject]@{ name = $setting.Name; value = [string] $setting.Value }
}
foreach ($mapping in $secretMappings) {
    $owned[$mapping.EnvironmentVariable] = [pscustomobject]@{ name = $mapping.EnvironmentVariable; secretRef = $mapping.SecretName }
}
$removed = @(if ($config.PSObject.Properties.Name -contains 'removedSettings') { $config.removedSettings })
$environmentVariables = @(@($container.env) | Where-Object { $_ -and -not $owned.ContainsKey($_.name) -and $removed -notcontains $_.name }) + @($owned.Values | Sort-Object name)

# Startup and liveness use the liveness endpoint (process only); readiness uses the dependency checks.
$probes = @(
    New-ContainerProbe 'Startup' $healthPaths.LivenessPath $config.probes.port $config.probes.startup
    New-ContainerProbe 'Liveness' $healthPaths.LivenessPath $config.probes.port $config.probes.liveness
    New-ContainerProbe 'Readiness' $healthPaths.ReadinessPath $config.probes.port $config.probes.readiness
)

$image = "$loginServer/$($ImageRepository):$ImageTag"
$container | Add-Member -NotePropertyName image -NotePropertyValue $image -Force
$container | Add-Member -NotePropertyName env -NotePropertyValue $environmentVariables -Force
$container | Add-Member -NotePropertyName probes -NotePropertyValue $probes -Force
$template | Add-Member -NotePropertyName terminationGracePeriodSeconds -NotePropertyValue ([int] $config.terminationGracePeriodSeconds) -Force
# PATCH omission retains the old suffix. Explicitly name each deployment uniquely,
# including retries of an image tag or deployments after a manually named revision.
$suffixLength = [Math]::Min(20, 54 - $ContainerAppName.Length - 2)
if ($suffixLength -lt 2) { throw 'Container App name leaves insufficient space for a revision suffix.' }
$revisionSuffix = 'r' + [guid]::NewGuid().ToString('N').Substring(0, $suffixLength - 1)
$template | Add-Member -NotePropertyName revisionSuffix -NotePropertyValue $revisionSuffix -Force

$previousRevision = $app.properties.latestRevisionName
$body = @{ properties = @{ template = $template } } | ConvertTo-Json -Depth 50 -Compress
$bodyFile = [System.IO.Path]::GetTempFileName()
try {
    [System.IO.File]::WriteAllText($bodyFile, $body)
    Write-Host "Deploying $image to $ContainerAppName..."
    Invoke-Az rest --method patch --url "$($appId)?api-version=$ContainerAppsApiVersion" --body "@$bodyFile" --headers 'Content-Type=application/json' --output none
}
finally {
    Remove-Item -LiteralPath $bodyFile -Force -ErrorAction SilentlyContinue
}

# ---------------------------------------------------------------------------------------------------------------------
# Wait for the new revision to become ready. Until then the previous revision keeps serving traffic.
# ---------------------------------------------------------------------------------------------------------------------

$deadline = (Get-Date).AddMinutes([int] $config.rollout.readyTimeoutMinutes)
$revisionName = ''
$state = ''
do {
    Start-Sleep -Seconds ([int] $config.rollout.pollIntervalSeconds)
    $app = Invoke-ArmGet $appId $ContainerAppsApiVersion
    if ($null -eq $app) { continue }

    $provisioning = $app.properties.provisioningState
    if ($provisioning -eq 'Failed') {
        throw "Container App update failed (provisioningState: Failed). The previous revision '$previousRevision' is still active."
    }
    if ($provisioning -ne 'Succeeded' -or $app.properties.latestRevisionName -eq $previousRevision) { continue }

    $revisionName = $app.properties.latestRevisionName
    $revision = Invoke-ArmGet "$appId/revisions/$revisionName" $ContainerAppsApiVersion
    if ($null -eq $revision) { continue }

    $state = "running=$($revision.properties.runningState) health=$($revision.properties.healthState)"
    Write-Host "Revision $($revisionName): $state"
    if ($revision.properties.runningState -in @('Failed', 'ActivationFailed') -or $revision.properties.provisioningState -eq 'Failed') {
        throw "Revision $revisionName failed to start ($state). Check the container logs in Log Analytics. Traffic stays on '$previousRevision'."
    }
    # healthState is "None" when an idle revision has scaled to zero, so only "Unhealthy" blocks success here;
    # the smoke test below proves the new image actually serves.
    if ($app.properties.latestReadyRevisionName -eq $revisionName -and $revision.properties.healthState -ne 'Unhealthy') {
        break
    }
} while ((Get-Date) -lt $deadline)

if ($app.properties.latestReadyRevisionName -ne $revisionName -or [string]::IsNullOrEmpty($revisionName)) {
    throw "Revision '$revisionName' did not become ready within $($config.rollout.readyTimeoutMinutes) minutes ($state). Traffic stays on '$previousRevision'."
}

$appUrl = "https://$($app.properties.configuration.ingress.fqdn)"

$smokeTest = $config.smokeTest
if ($smokeTest.enabled) {
    # Through the public URL: wakes a scaled-to-zero app and checks TLS, ingress and the new container end to end.
    $healthUrl = "$appUrl$($healthPaths.ReadinessPath)"
    $healthy = $false
    for ($attempt = 1; $attempt -le [int] $smokeTest.attempts -and -not $healthy; $attempt++) {
        try {
            $response = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec ([int] $smokeTest.timeoutSeconds)
            $healthy = $response.StatusCode -eq 200
        }
        catch {
            Write-Host "Smoke test attempt $($attempt): $($_.Exception.Message)"
            Start-Sleep -Seconds ([int] $smokeTest.retryDelaySeconds)
        }
    }
    if (-not $healthy) {
        throw "Revision $revisionName is active but $healthUrl did not return 200. Roll back by deploying the previous image tag."
    }
    Write-Host "Smoke test passed: $healthUrl"
}

Write-Host "Deployed $image as revision $revisionName"
Write-Host "URL: $appUrl"

Set-PipelineOutput 'app-url' $appUrl
Set-PipelineOutput 'revision' $revisionName
Add-PipelineSummary @"
### Deployed ``$Environment``

| | |
|---|---|
| Container App | ``$ContainerAppName`` (``$ResourceGroup``) |
| Image | ``$image`` |
| Revision | ``$revisionName`` (previous: ``$previousRevision``) |
| URL | $appUrl |
"@
