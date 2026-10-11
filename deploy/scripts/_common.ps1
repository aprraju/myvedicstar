# Helpers shared by the deployment scripts. Dot-source it: . "$PSScriptRoot/_common.ps1"
# Works in Windows PowerShell 5.1 and PowerShell 7, locally and on GitHub Actions runners.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Native-command exit codes are checked explicitly below; never let PowerShell 7 turn an expected
# non-zero exit (for example "resource not found") into a terminating error.
$PSNativeCommandUseErrorActionPreference = $false

# Keep the Azure CLI non-interactive (the containerapp extension installs itself without a prompt).
$env:AZURE_EXTENSION_USE_DYNAMIC_INSTALL = 'yes_without_prompt'
$env:AZURE_CORE_NO_COLOR = 'true'

$script:ImageRepository = 'myvedicstar-api'
$script:RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$script:ContainerAppsApiVersion = '2024-03-01'
$script:KeyVaultApiVersion = '2023-07-01'

# ---------------------------------------------------------------------------------------------------------------------
# Azure CLI
# ---------------------------------------------------------------------------------------------------------------------

function Invoke-Az {
    # Runs the Azure CLI and fails the script on a non-zero exit code. Never pass secret values as arguments.
    $output = & az @args
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI command failed (exit code $LASTEXITCODE): az $($args[0]) $($args[1])"
    }
    return $output
}

function Test-AzSucceeds {
    # Runs the Azure CLI for its exit code only, discarding all output (for existence checks).
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & az @args *> $null
        return ($LASTEXITCODE -eq 0)
    }
    finally {
        $ErrorActionPreference = $previous
    }
}

function Initialize-AzCli {
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
        throw 'Azure CLI (az) was not found on PATH.'
    }
    if (-not (Test-AzSucceeds account show)) {
        throw 'Not signed in to Azure. Run "az login" locally, or the azure/login action in GitHub Actions, first.'
    }
}

function Get-AzSubscriptionId {
    return "$(Invoke-Az account show --query id --output tsv)".Trim()
}

function Invoke-ArmGet([string] $ResourceId, [string] $ApiVersion) {
    # Azure Resource Manager GET. Returns $null when the resource does not exist or cannot be read.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $json = & az rest --method get --url "$($ResourceId)?api-version=$ApiVersion" --output json 2> $null
        if ($LASTEXITCODE -ne 0) { return $null }
    }
    finally {
        $ErrorActionPreference = $previous
    }
    return ($json | Out-String | ConvertFrom-Json)
}

# ---------------------------------------------------------------------------------------------------------------------
# Pipeline integration (no-ops when not running in GitHub Actions)
# ---------------------------------------------------------------------------------------------------------------------

function Test-IsPipeline {
    return ($env:GITHUB_ACTIONS -eq 'true') -or ($env:TF_BUILD -eq 'True') -or ($env:CI -eq 'true')
}

function Set-PipelineOutput([string] $Name, [string] $Value) {
    if ($env:GITHUB_OUTPUT) {
        Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "$Name=$Value" -Encoding utf8
    }
}

function Add-PipelineSummary([string] $Markdown) {
    if ($env:GITHUB_STEP_SUMMARY) {
        Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $Markdown -Encoding utf8
    }
}

# ---------------------------------------------------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------------------------------------------------

function Assert-Required([string] $Value, [string] $Description) {
    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw "$Description is required."
    }
}

function Assert-EnvironmentName([string] $Environment) {
    if ($Environment -notmatch '^[a-z][a-z0-9-]{1,19}$') {
        throw "Invalid environment name '$Environment'."
    }
}

function Get-DeploymentConfig([string] $Environment) {
    # Non-secret, application-owned settings for one environment: deploy/environments/<env>.json
    Assert-EnvironmentName $Environment
    $path = Join-Path $script:RepositoryRoot "deploy/environments/$Environment.json"
    if (-not (Test-Path -LiteralPath $path)) {
        throw "No deployment configuration for '$Environment' ($path)."
    }
    return (Get-Content -Raw -LiteralPath $path | ConvertFrom-Json)
}

function Get-KeyVaultResourceId([string] $SubscriptionId, $Config) {
    return "/subscriptions/$SubscriptionId/resourceGroups/$($Config.keyVault.resourceGroup)/providers/Microsoft.KeyVault/vaults/$($Config.keyVault.name)"
}

# ---------------------------------------------------------------------------------------------------------------------
# Images
# ---------------------------------------------------------------------------------------------------------------------

function Get-DefaultImageTag {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $sha = & git -C $script:RepositoryRoot rev-parse --short=12 HEAD 2> $null
    }
    finally {
        $ErrorActionPreference = $previous
    }
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sha)) {
        throw 'Could not determine the git commit. Pass -ImageTag explicitly.'
    }
    return "$sha".Trim()
}

function Assert-ImageTag([string] $ImageTag) {
    # Docker tag grammar; also stops anything unexpected reaching the CLI from a pipeline input.
    if ($ImageTag -notmatch '^[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}$') {
        throw "Invalid image tag '$ImageTag'."
    }
}

function Test-ImageExists([string] $RegistryName, [string] $ImageTag) {
    return Test-AzSucceeds acr repository show --name $RegistryName --image "$($script:ImageRepository):$ImageTag"
}
