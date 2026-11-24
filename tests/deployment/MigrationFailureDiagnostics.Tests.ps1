# Api#214: a failed migration must print the job's own log before it throws.
#
# This is a FAILURE path, so a happy-path test proves nothing. The point of the change is that on
# Failed/Stopped the script retrieves and prints the container app job's log, which is where the EF error
# lives. The stub az below returns Failed and a realistic EF failure body, and the assertions are on what
# the script PRINTED - not merely on the fact that it exited non-zero, which was already true before.
#
# No Azure resource is contacted: az is shadowed on PATH by a script that records its arguments and
# answers from a table.
$ErrorActionPreference = 'Stop'
$repositoryPath = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$scriptPath = Join-Path $repositoryPath 'deploy/scripts/run-migrations.ps1'
if (-not (Test-Path -LiteralPath $scriptPath)) { throw "Missing script: $scriptPath" }

# The EF failure body the job would have written. Asserted on verbatim, because the whole defect is that
# this text used to be discarded.
$efError = @'
info: Microsoft.EntityFrameworkCore.Migrations[20402]
      Applying migration '20261122_AddKundaliComparisonTable'.
fail: Microsoft.EntityFrameworkCore.Database.Command[20102]
      Failed executing DbCommand (2ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      CREATE TABLE "KundaliComparisons" (
          "Id" uuid NOT NULL,
          "ProfileAId" uuid NOT NULL,
          CONSTRAINT "PK_KundaliComparisons" PRIMARY KEY ("Id")
      );
Npgsql.PostgresException (0x80004005): 42P07: relation "KundaliComparisons" already exists
   at Npgsql.Internal.Postgres.NpgsqlConnector.HandleCriticalMessage(Byte code)
'@

$stubDir = Join-Path ([System.IO.Path]::GetTempPath()) ("mig-stub-" + [guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $stubDir | Out-Null
$stubLog = Join-Path $stubDir 'calls.log'

# The stub answers by subcommand: job show/update/start/execution show succeed, and execution show reports
# Failed so the diagnostics branch is the one under test. `logs show` prints the EF body.
$stubPath = Join-Path $stubDir 'az'
$stub = @'
#!/usr/bin/env bash
printf '%s\n' "$*" >> "__CALLS__"
args="$*"
case "$args" in
  *'job logs show'*)
    printf '%s\n' __EF__
    exit 0 ;;
  *'job execution show'*)
    echo 'Failed'
    exit 0 ;;
  *'job start'*)
    echo 'caj-myvedicstar-migrate-prod-abc123'
    exit 0 ;;
  *'job update'*)
    exit 0 ;;
  *'job show'*)
    echo '/subscriptions/sub/resourceGroups/rg/providers/Microsoft.App/jobs/caj-myvedicstar-migrate-prod'
    exit 0 ;;
  *'account show'*)
    echo 'sub-id'
    exit 0 ;;
  *) exit 0 ;;
esac
'@
$stub = $stub.Replace('__CALLS__', $stubLog).Replace('__EF__', $efError)
Set-Content -LiteralPath $stubPath -Value $stub -NoNewline
& chmod +x $stubPath

$previousPath = $env:PATH
$env:PATH = "$stubDir$([System.IO.Path]::PathSeparator)$env:PATH"
$env:REGISTRY_NAME = 'testregistry'
$env:IMAGE_TAG = 'deadbeef'
$env:RESOURCE_GROUP_NAME = 'rg-test'
$env:AZURE_SUBSCRIPTION_ID = 'sub-id'

try {
    $output = & pwsh -NoProfile -File $scriptPath 2>&1 | Out-String
    $threw = $false
    $errorText = ''
    if ($LASTEXITCODE -ne 0) {
        $threw = $true
        $errorText = $output
    }
}
finally {
    $env:PATH = $previousPath
    Remove-Item -LiteralPath $stubDir -Recurse -Force -ErrorAction SilentlyContinue
}

function Assert-Mentions([string] $Haystack, [string] $Needle, [string] $Because) {
    if (-not $Haystack.Contains($Needle)) { throw "$Because`n--- looked for: $Needle ---`n--- output was: ---`n$Haystack" }
}

# 1. The failure verdict is unchanged - the script still fails on Failed.
if (-not $threw) { throw 'Migration script did not fail on a Failed execution; the verdict semantics changed' }

# 2. The log was actually fetched, with the documented argument names.
$calls = Get-Content -LiteralPath $stubLog -Raw
Assert-Mentions $calls 'job logs show' 'The script never asked for the job logs; the whole change is missing'
Assert-Mentions $calls '--execution caj-myvedicstar-migrate-prod-abc123' 'Log fetch must name the failed execution'
Assert-Mentions $calls '--tail 100' 'Log fetch must be tail-limited so the step stays readable'

# 3. The EF error reached the step output - the defect being fixed.
Assert-Mentions $output 'Applying migration' 'The EF migration name must appear in the step log'
Assert-Mentions $output '42P07: relation "KundaliComparisons" already exists' 'The SQLSTATE and failing statement must appear in the step log'
Assert-Mentions $output '--- Migration job log' 'The printed log must be delimited so it is legible next to the wrapper output'

# 4. The throw still names the execution and points at the printed log.
Assert-Mentions $output 'caj-myvedicstar-migrate-prod-abc123' 'The failure message must name the execution'
Assert-Mentions $output 'API deployment stopped' 'The original failure wording must survive, so nothing downstream keying on it breaks'

Write-Host 'Migration failure diagnostics: log fetched, EF error printed, verdict unchanged.'