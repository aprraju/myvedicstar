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

    $publishDir = Join-Path ([System.IO.Path]::GetTempPath()) ("myvedicstar-combined-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $publishDir | Out-Null

    try {
        Write-Host "Publishing both APIs ($ImageTag)..."
        & dotnet publish (Join-Path $RepositoryRoot 'src/MyVedicStar.Host/MyVedicStar.Host.csproj') `
            --configuration Release `
            --output $publishDir `
            -p:UseAppHost=false `
            "-p:SourceRevisionId=$ImageTag" | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed (exit code $LASTEXITCODE)."
        }

        # az acr build reads the Dockerfile from inside the build context.
        $dockerfile = Join-Path $publishDir 'Dockerfile'
        Copy-Item -LiteralPath (Join-Path $RepositoryRoot 'deploy/docker/Dockerfile') -Destination $dockerfile

        Write-Host "Building $image in $RegistryName..."
        # Streamed to the host (not captured) so build logs appear live and never mix with the script's output.
        # --file is absolute: newer Azure CLI versions resolve a relative --file against the current directory,
        # not the build context.
        & az acr build `
            --registry $RegistryName `
            --image $image `
            --file $dockerfile `
            --platform linux/amd64 `
            $publishDir | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "Image build failed (exit code $LASTEXITCODE)."
        }
    }
    finally {
        $resolvedPublishDir = [System.IO.Path]::GetFullPath($publishDir)
        $resolvedTempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
        if (-not $resolvedPublishDir.StartsWith($resolvedTempRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Publish cleanup path must be within the temporary directory.'
        }
        Remove-Item -LiteralPath $resolvedPublishDir -Recurse -Force -ErrorAction SilentlyContinue
    }

    Write-Host "Image ready: $image"
}

Set-PipelineOutput 'image-tag' $ImageTag
Add-PipelineSummary "Image: ``$RegistryName.azurecr.io/$image``"
Write-Output $ImageTag
