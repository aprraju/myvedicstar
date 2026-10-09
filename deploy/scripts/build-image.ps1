<#
.SYNOPSIS
    Publishes the API and builds its container image in Azure Container Registry (no local Docker needed).
    Build once; release the tag with deploy-app.ps1 (and redeploy any earlier tag to roll back).

.DESCRIPTION
    1. dotnet publish runs on this machine. MyVedicStar.* packages come from GitHub Packages, so GITHUB_TOKEN must
       be set (GitHub Actions: secrets.GITHUB_TOKEN with packages: read; locally: a PAT with read:packages).
    2. az acr build builds deploy/docker/Dockerfile with the publish output as its context, so neither source code
       nor the package token reaches the image.

    Tags are immutable by convention: if the tag already exists (for example when a pipeline run is re-run), the
    build is skipped instead of overwriting an image that may already be in production. In GitHub Actions the tag
    is also written to the step output "image-tag".

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
    Assert-Required $env:GITHUB_TOKEN 'GITHUB_TOKEN (restores MyVedicStar.* packages from GitHub Packages; see nuget.config)'

    $publishDir = Join-Path ([System.IO.Path]::GetTempPath()) "myvedicstar-authorize-api-$ImageTag"
    if (Test-Path -LiteralPath $publishDir) {
        Remove-Item -LiteralPath $publishDir -Recurse -Force
    }

    try {
        Write-Host "Publishing the API ($ImageTag)..."
        & dotnet publish (Join-Path $RepositoryRoot 'src/MyVedicStar.Authorize.Api/MyVedicStar.Authorize.Api.csproj') `
            --configuration Release `
            --output $publishDir `
            -p:UseAppHost=false `
            "-p:SourceRevisionId=$ImageTag" | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed (exit code $LASTEXITCODE)."
        }

        # az acr build reads the Dockerfile from inside the build context.
        Copy-Item -LiteralPath (Join-Path $RepositoryRoot 'deploy/docker/Dockerfile') -Destination $publishDir

        Write-Host "Building $image in $RegistryName..."
        # Streamed to the host (not captured) so build logs appear live and never mix with the script's output.
        & az acr build `
            --registry $RegistryName `
            --image $image `
            --file Dockerfile `
            --platform linux/amd64 `
            $publishDir | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "Image build failed (exit code $LASTEXITCODE)."
        }
    }
    finally {
        Remove-Item -LiteralPath $publishDir -Recurse -Force -ErrorAction SilentlyContinue
    }

    Write-Host "Image ready: $image"
}

Set-PipelineOutput 'image-tag' $ImageTag
Add-PipelineSummary "Image: ``$RegistryName.azurecr.io/$image``"
Write-Output $ImageTag
