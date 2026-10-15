<#
.SYNOPSIS
    Stores the application's secrets in the environment's Key Vault. Values are prompted for and never
    written to source control, shell history, logs or command-line arguments.

.DESCRIPTION
    Asks for each Key Vault secret named under "secrets" in deploy/environments/<env>.json
    (including the production Microsoft Graph client secret).

    Deliberately interactive and refused in CI: a person with Key Vault Secrets Officer enters values
    straight into Key Vault, so they never exist as GitHub secrets or in pipeline logs. The vault denies
    public network access by default, so run it from an allowed network (or add your IP temporarily),
    or create the secret in the Azure portal instead.

    After rotating a secret, redeploy (or restart the active revision) so the container loads the new value.

.EXAMPLE
    ./set-secrets.ps1

.EXAMPLE
    ./set-secrets.ps1 -Environment prod -Name <secret-name>
#>
[CmdletBinding()]
param(
    [string] $Environment = 'prod',
    # Set only these Key Vault secrets, for example to rotate one.
    [string[]] $Name
)

. "$PSScriptRoot/_common.ps1"

if (Test-IsPipeline) {
    throw 'set-secrets.ps1 is interactive and does not run in CI. A person with Key Vault Secrets Officer runs it; pipelines only reference the stored secrets.'
}

$config = Get-DeploymentConfig $Environment
Initialize-AzCli

$keyVaultName = $config.keyVault.name
$secretNames = @(if ($Name) { $Name } elseif ($config.secrets) { $config.secrets.PSObject.Properties | ForEach-Object { $_.Value } })
if ($secretNames.Count -eq 0) {
    Write-Host "Environment '$Environment' references no Key Vault secrets."
    return
}

foreach ($secretName in $secretNames) {
    $secure = Read-Host -AsSecureString "Value for '$secretName' in $keyVaultName"
    $plain = [System.Net.NetworkCredential]::new('', $secure).Password
    if ([string]::IsNullOrEmpty($plain)) {
        throw "No value entered for '$secretName'."
    }

    # Passed through a short-lived file so the value never appears in the process list.
    $tempFile = [System.IO.Path]::GetTempFileName()
    try {
        [System.IO.File]::WriteAllText($tempFile, $plain)
        Invoke-Az keyvault secret set --vault-name $keyVaultName --name $secretName --file $tempFile --encoding utf-8 --content-type 'text/plain' --output none
    }
    finally {
        Remove-Item -LiteralPath $tempFile -Force -ErrorAction SilentlyContinue
        $plain = $null
    }

    Write-Host "Stored '$secretName'."
}

Write-Host 'Redeploy (or restart the active revision) so running containers load the new value.'
