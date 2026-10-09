<#
.SYNOPSIS
    Builds the API container image in Azure Container Registry (no local Docker needed).
    Build once; release the tag with deploy-app.ps1 (and redeploy any earlier tag to roll back).

.DESCRIPTION
    Tags are immutable by convention: if the tag already exists (for example when a pipeline run is
    re-run), the build is skipped instead of overwriting an image that may already be in production.
    In GitHub Actions the tag is also written to the step output "image-tag".

.OUTPUTS
    The image tag.

.EXAMPLE
    ./build-image.ps1 -RegistryName crmyvedicstarprodxw4t
#>
[CmdletBinding()]
param(
    [string] $RegistryName = $env:REGISTRY_NAME,
    # Defaults to the current git commit, so each build is traceable.
    [string] $ImageTag = $env:IMAGE_TAG
)

. "$PSScriptRoot/_common.ps1"

Assert-Required $RegistryName 'Registry name (-RegistryName or REGISTRY_NAME)'
if ([string]::IsNullOrWhiteSpace($ImageTag)) {
    $ImageTag = Get-DefaultImageTag
}
Assert-ImageTag $ImageTag

Initialize-AzCli

$image = "$($ImageRepository):$ImageTag"
if (Test-ImageExists $RegistryName $ImageTag) {
    Write-Host "Image $image already exists in $RegistryName; not rebuilding."
}
else {
    Write-Host "Building $image in $RegistryName..."
    # Streamed to the host (not captured) so build logs appear live and never mix with the script's output.
    & az acr build `
        --registry $RegistryName `
        --image $image `
        --file (Join-Path $RepositoryRoot 'deploy/docker/Dockerfile') `
        --build-arg "SOURCE_REVISION=$ImageTag" `
        --platform linux/amd64 `
        $RepositoryRoot | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Image build failed (exit code $LASTEXITCODE)."
    }
    Write-Host "Image ready: $image"
}

Set-PipelineOutput 'image-tag' $ImageTag
Add-PipelineSummary "Image: ``$RegistryName.azurecr.io/$image``"
Write-Output $ImageTag
