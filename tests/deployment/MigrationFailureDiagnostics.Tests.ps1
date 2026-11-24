# Api#214: a failed migration must print the job's own log before it throws.
#
# This is a FAILURE path, so a happy-path test proves nothing. The change under test is that on
# Failed/Stopped the runner retrieves and prints the container app job's log, where the EF error lives.
#
# How the printed output is observed, and why it is done this way:
# The runner prints diagnostics with Write-Host. That goes to the HOST, not to any PowerShell pipeline
# stream, so `2>&1`, `6>&1` and `*>$null` all leave it visible in the step log but absent from the
# captured string - two earlier revisions of this test failed for exactly that reason while the log was
# in fact printed. The test therefore runs the runner in a child pwsh with TRANSCRIPTS enabled: the
# transcript file records host writes, so the assertions read what the step would actually show. That is
# strictly stronger than capturing a variable - it tests the observable the reviewer cares about.
#
# The fake az is a PowerShell function, matching MigrationContract.Tests.ps1's convention. A shell script on
# PATH does not work here: an earlier test in the same step leaves a global `az` function defined, and
# PowerShell resolves functions before PATH executables.
$ErrorActionPreference = 'Stop'
$runner = Join-Path $PSScriptRoot '../../deploy/scripts/run-migrations.ps1'
if (-not (Test-Path -LiteralPath $runner)) { throw "Missing script: $runner" }

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("migdiag-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $tempRoot | Out-Null
$transcript = Join-Path $tempRoot 'transcript.txt'
$fakeModule = Join-Path $tempRoot 'FakeAz.psm1'
$childScript = Join-Path $tempRoot 'run-child.ps1'

# The EF failure body the job would have written. Asserted on verbatim: the defect is that this text was
# discarded, so if it does not reach the transcript nothing has been fixed.
$efLines = @(
    'info: Microsoft.EntityFrameworkCore.Migrations[20402]'
    "      Applying migration '20261122_AddKundaliComparisonTable'."
    'fail: Microsoft.EntityFrameworkCore.Database.Command[20102]'
    "      Failed executing DbCommand (2ms) [Parameters=[], CommandType='Text', CommandTimeout='30']"
    '      CREATE TABLE "KundaliComparisons" ('
    '          "Id" uuid NOT NULL,'
    '          CONSTRAINT "PK_KundaliComparisons" PRIMARY KEY ("Id")'
    '      );'
    'Npgsql.PostgresException (0x80004005): 42P07: relation "KundaliComparisons" already exists'
)

# The fake az. Its behaviour is driven by environment variables so the child process can vary it per case.
# Order matters: 'job logs show' and 'job execution show' both contain 'job' and 'show', and a broader
# pattern tested first would shadow them. That bug existed in an earlier revision.
$moduleSource = @'
function global:az {
    $global:LASTEXITCODE = 0
    $text = $args -join ' '
    if ($text -like '*job logs show*') {
        if ($env:DIAG_LOG_FAILS -eq '1') {
            # A native command writes its error to stderr and exits non-zero; the runner captures that with
            # `2>&1` and prints it. NOT Write-Error: that raises a PowerShell error record, which `2>&1`
            # does not convert into the string a native command would emit, so the runner correctly
            # reported 'no output captured' while this stub believed it had produced text. Writing to the
            # real stderr stream is what makes the stub behave like az.
            $global:LASTEXITCODE = 1
            [Console]::Error.WriteLine($env:DIAG_LOG_ERROR)
            return
        }
        foreach ($line in ($env:DIAG_EF_LINES -split [Environment]::NewLine)) { Write-Output $line }
        return
    }
    if ($text -like '*job execution show*') { return $env:DIAG_STATUS }
    if ($text -like '*job start*') { return 'caj-myvedicstar-migrate-prod-abc123' }
    if ($text -like '*job update*') { return }
    if ($text -like '*job show*') { return $env:DIAG_CONTAINERS }
    if ($args[0] -eq 'account') { return 'sub-id' }
    return
}
Export-ModuleMember -Function az
'@
Set-Content -LiteralPath $fakeModule -Value $moduleSource

# The child: import the fake, start a transcript, run the real runner, stop the transcript. The transcript
# is what catches Write-Host. The runner's exit code is written to the transcript so the calling test can
# assert the verdict as well as the printed text.
$childSource = @'
param([string] $ModulePath, [string] $RunnerPath, [string] $TranscriptPath)
Import-Module $ModulePath -Force
Start-Transcript -Path $TranscriptPath -Force | Out-Null
try {
    try {
        & $RunnerPath -RegistryName testregistry -ImageTag test-tag -ResourceGroup rg-test -SubscriptionId sub-id
        Write-Host 'RESULT: completed without throwing'
    }
    catch {
        Write-Host ('RESULT: threw - ' + $_.Exception.Message)
    }
}
finally { Stop-Transcript | Out-Null }
'@
Set-Content -LiteralPath $childScript -Value $childSource

function Invoke-DiagnosticsCase([hashtable] $Environment) {
    foreach ($pair in $Environment.GetEnumerator()) { Set-Item -Path "env:$($pair.Key)" -Value $pair.Value }
    Remove-Item -LiteralPath $transcript -Force -ErrorAction SilentlyContinue
    & pwsh -NoProfile -File $childScript -ModulePath $fakeModule -RunnerPath $runner -TranscriptPath $transcript | Out-Null
    if (-not (Test-Path -LiteralPath $transcript)) { throw 'No transcript produced; the child run did not execute' }
    return Get-Content -LiteralPath $transcript -Raw
}

function Assert-Mentions([string] $Haystack, [string] $Needle, [string] $Because) {
    if (-not $Haystack.Contains($Needle)) {
        throw "$Because`n--- looked for: $Needle ---`n--- transcript was: ---`n$Haystack"
    }
}

function Assert-NotMentions([string] $Haystack, [string] $Needle, [string] $Because) {
    if ($Haystack.Contains($Needle)) { throw "$Because (unexpected: $Needle)" }
}

$efBody = $efLines -join [Environment]::NewLine
try {
    # --- Case 1: Failed execution, log readable. This is the defect being fixed. ---
    $t1 = Invoke-DiagnosticsCase @{
        DIAG_STATUS     = 'Failed'
        DIAG_LOG_FAILS  = '0'
        DIAG_CONTAINERS = 'migrator'
        DIAG_EF_LINES   = $efBody
    }

    # 1a. The verdict is unchanged: still throws. Pinned first because it is the regression this change
    #     could plausibly cause - diagnostics must never turn a failed migration into a pass.
    Assert-Mentions $t1 'RESULT: threw' 'A Failed execution must still fail the deploy'
    Assert-Mentions $t1 'API deployment stopped' 'The original failure wording must survive'
    Assert-Mentions $t1 'caj-myvedicstar-migrate-prod-abc123' 'The failure message must name the execution'

    # 1b. The job log was fetched and printed - the defect.
    Assert-Mentions $t1 '--- Migration job log' 'The log must be printed and delimited'
    Assert-Mentions $t1 'Applying migration' 'The EF migration name must reach the step output'
    Assert-Mentions $t1 '42P07: relation "KundaliComparisons" already exists' 'The SQLSTATE and failing statement must reach the step output'
    Assert-Mentions $t1 '--- end of migration job log' 'The log block must be closed'

    # --- Case 2: Failed execution, log UNREADABLE. Diagnostics must degrade, not change the verdict. ---
    # The container list is returned exactly as production returns it: the extension notice is the first
    # line, which is what made the real diagnostic read 'WARNING: ..., migrator'.
    $logError = 'ERROR: (ContainerAppNotFound) The Resource Microsoft.App/jobs/caj-myvedicstar-migrate-prod was not found'
    $t2 = Invoke-DiagnosticsCase @{
        DIAG_STATUS     = 'Failed'
        DIAG_LOG_FAILS  = '1'
        DIAG_LOG_ERROR  = $logError
        DIAG_CONTAINERS = "WARNING: The behavior of this command has been altered by the following extension: containerapp`nmigrator"
        DIAG_EF_LINES   = $efBody
    }
    Assert-Mentions $t2 'RESULT: threw' 'An unreadable log must not change the verdict'
    Assert-Mentions $t2 'Could not read logs for container' 'An unreadable log must be reported, not silently skipped'
    Assert-Mentions $t2 "'migrator' IS among them" 'The fallback must say the guessed container is in fact declared, so the operator does not chase the wrong cause'
    Assert-Mentions $t2 'API deployment stopped' 'The verdict wording must survive a diagnostics failure too'

    # The branch that failed in production, and that the previous version of this test did not cover:
    # the log command exits non-zero, so the CLI's own error must reach the step output instead of being
    # swallowed with the message 'diagnostics unavailable'.
    Assert-Mentions $t2 'The Azure CLI said' 'A failed fetch must say it is printing the CLI error'
    # Pins the stub-vs-script confusion that broke the previous revision of this test: a stub using
    # Write-Error produces no captured stderr, so the runner prints '(no output was captured)'. If that
    # string appears here, the STUB has stopped behaving like a native command, not the runner stopped
    # printing - say so explicitly rather than let the next assertion's message imply the runner is at fault.
    Assert-NotMentions $t2 '(no output was captured from the CLI)' 'The stub must emit real stderr; this string means the test stopped simulating a native command, not that the runner dropped output'
    Assert-Mentions $t2 'ContainerAppNotFound' 'The az error text must reach the step output, not be discarded'
    Assert-Mentions $t2 'was not found' 'The full CLI error, not a truncation, must be printed'

    # The extension notice must not be joined into the container list - that is what produced the
    # production line 'WARNING: The behavior of this command ... containerapp, migrator'.
    Assert-NotMentions $t2 'has been altered by the following extension: containerapp, migrator' 'The az extension WARNING must be filtered out of the container list'
    Assert-NotMentions $t2 "Containers declared by job 'caj-myvedicstar-migrate-prod': WARNING" 'The container list must start with a container name, never the extension notice'

    # --- Case 3: Succeeded. Unchanged behaviour: no throw, no log fetch. ---
    $t3 = Invoke-DiagnosticsCase @{
        DIAG_STATUS     = 'Succeeded'
        DIAG_LOG_FAILS  = '0'
        DIAG_CONTAINERS = 'migrator'
        DIAG_EF_LINES   = $efBody
    }
    Assert-Mentions $t3 'completed without throwing' 'A Succeeded execution must not fail the deploy'
    Assert-NotMentions $t3 '--- Migration job log' 'A Succeeded execution must not fetch or print job logs'

    Write-Host 'Migration failure diagnostics: log fetched and printed on failure, EF error in step output, fallback names real containers, verdict unchanged in all three cases.'
}
finally {
    Remove-Item Function:global:az -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}