[CmdletBinding()]
param([string] $RegistryName = $env:REGISTRY_NAME, [string] $ImageTag = $env:IMAGE_TAG, [switch] $GenerateOnly)
. "$PSScriptRoot/_common.ps1"
Assert-Required $RegistryName 'RegistryName'
Assert-ImageTag $ImageTag
$context = Join-Path $RepositoryRoot "TestResults/migrator-$ImageTag"
New-Item -ItemType Directory -Force $context | Out-Null
$tools = Join-Path $RepositoryRoot "TestResults/migrator-tools-$ImageTag"
if (-not (Test-Path (Join-Path $tools 'dotnet-ef')) -and -not (Test-Path (Join-Path $tools 'dotnet-ef.exe'))) {
    & dotnet tool install dotnet-ef --tool-path $tools --version 10.0.12
    if ($LASTEXITCODE) { throw 'EF tool installation failed.' }
}
$scripts = [Collections.Generic.List[string]]::new()
foreach ($domain in 'Authorize', 'Settings') {
    $project = Join-Path $RepositoryRoot "src/MyVedicStar.$domain.Data/MyVedicStar.$domain.Data.csproj"
    & dotnet restore $project
    if ($LASTEXITCODE) { throw "$domain migration project restore failed." }
    & dotnet build $project --configuration Release --no-restore
    if ($LASTEXITCODE) { throw "$domain migration project build failed." }
    $output = Join-Path $context "$domain.sql"
    & "$tools/dotnet-ef" migrations script --idempotent --project $project --configuration Release --no-build --output $output
    if ($LASTEXITCODE) { throw "$domain migration SQL generation failed." }
    $scripts.Add((Get-Content -LiteralPath $output -Raw))
}
# Authorize must run first: Settings foreign keys reference its existing user table.
[IO.File]::WriteAllText((Join-Path $context 'migrations.sql'), ($scripts -join "`n"), [Text.UTF8Encoding]::new($false))
Copy-Item "$RepositoryRoot/deploy/database/*" $context
Copy-Item "$RepositoryRoot/deploy/docker/Migrator.Dockerfile" "$context/Dockerfile"
foreach ($name in 'migrations.sql', 'migrate.sh', 'bootstrap-roles.sql') {
    $path = Join-Path $context $name
    $content = Get-Content -LiteralPath $path -Raw
    [IO.File]::WriteAllText($path, $content.Replace([string][char]0xfeff, '').Replace("`r`n", "`n"), [Text.UTF8Encoding]::new($false))
}
if ($GenerateOnly) { Write-Host "Migration image context verified: $context"; return }
& az acr build --registry $RegistryName --image "myvedicstar-migrator:$ImageTag" --file "$context/Dockerfile" --platform linux/amd64 $context
if ($LASTEXITCODE) { throw 'Migration image build failed.' }
