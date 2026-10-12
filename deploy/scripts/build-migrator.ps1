[CmdletBinding()]
param([string] $RegistryName = $env:REGISTRY_NAME, [string] $ImageTag = $env:IMAGE_TAG)
. "$PSScriptRoot/_common.ps1"
Assert-Required $RegistryName 'RegistryName'
Assert-ImageTag $ImageTag
$context = Join-Path $RepositoryRoot "TestResults/migrator-$ImageTag"
New-Item -ItemType Directory -Force $context | Out-Null
$tools = Join-Path $RepositoryRoot "TestResults/migrator-tools-$ImageTag"
& dotnet tool install dotnet-ef --tool-path $tools --version 10.0.12
if ($LASTEXITCODE) { throw 'EF tool installation failed.' }
& "$tools/dotnet-ef" migrations script --idempotent --project "$RepositoryRoot/src/MyVedicStar.Authorize.Data" --configuration Release --output "$context/migrations.sql"
if ($LASTEXITCODE) { throw 'Migration SQL generation failed.' }
Copy-Item "$RepositoryRoot/deploy/database/*" $context
Copy-Item "$RepositoryRoot/deploy/docker/Migrator.Dockerfile" "$context/Dockerfile"
foreach ($name in 'migrations.sql', 'migrate.sh', 'bootstrap-roles.sql') {
    $path = Join-Path $context $name
    $content = Get-Content -LiteralPath $path -Raw
    [IO.File]::WriteAllText($path, $content.Replace([string][char]0xfeff, '').Replace("`r`n", "`n"), [Text.UTF8Encoding]::new($false))
}
& az acr build --registry $RegistryName --image "myvedicstar-migrator:$ImageTag" --file "$context/Dockerfile" --platform linux/amd64 $context
if ($LASTEXITCODE) { throw 'Migration image build failed.' }
