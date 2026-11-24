# Api#214: a failed migration must print the job's own log before it throws.
#
# This is a FAILURE path, so a happy-path test proves nothing. The point of the change is that on
# Failed/Stopped the script retrieves and prints the container app job's log, where the EF error lives.
# Assertions are on what the script PRINTED - not on the fact that it exited non-zero, which was already
# true before the change.
#
# The fake follows the convention of MigrationContract.Tests.ps1: a PowerShell function named `az` shadowing
# the real CLI, recording every call. An earlier attempt put a shell-script stub on PATH instead and it never
# ran, because another test in this step had already defined a global `az` function that Windows and pwsh
# resolve before PATH. Defining the function here is both simpler and immune to that.
$ErrorActionPreference = 'Stop'
$runner = Join-Path $PSScriptRoot '../../deploy/scripts/run-migrations.ps1'
if (-not (Test-Path -LiteralPath $runner)) { throw "Missing script: $runner" }

# The EF failure body the job would have written. Asserted on verbatim: the whole defect is that this text
# used to be discarded, so if it does not reach the step output nothing has been fixed.
$efLines = @(
    'info: Microsoft.EntityFrameworkCore.Migrations[20402]'
    "      Applying migration '20261122_AddKundaliComparisonTable'."
    'fail: Microsoft.EntityFrameworkCore.Database.Command[20102]'
    '      Failed executing DbCommand (2ms) [Parameters=[], CommandType=''Text'', CommandTimeout=''30'']'
    '      CREATE TABLE "KundaliComparisons" ('
    '          "Id" uuid NOT NULL,'
    '          CONSTRAINT "PK_KundaliComparisons" PRIMARY KEY ("Id")'
    '      );'
    'Npgsql.PostgresException (0x80004005): 42P07: relation "KundaliComparisons" already exists'
)

$global:diagCalls = [Collections.Generic.List[object]]::new()
$global:diagStatus = 'Failed'
$global:diagLogFails = $false
$global:diagDeclaredContainers = 'migrator'
function global:az {
    $global:LASTEXITCODE = 0
    $global:diagCalls.Add(@($args))
    $text = $args -join ' '
    if ($args[0] -eq 'account') { return 'sub-id' }
    # Order matters: every one of these strings contains 'job', and 'job logs show' / 'job execution show'
    # both contain 'show'. The most specific patterns are tested first so a broader one cannot shadow them.
    if ($text -like '*job logs show*') {
        if ($global:diagLogFails) { $global:LASTEXITCODE = 1; return 'ERROR: container not found' }
        # Emitted line by line, the way a real CLI prints, so the script's own tail-printing is what puts
        # them in the step output. Returning the array as one object would let a single Write-Host of the
        # collection pass the assertions without the per-line path working.
        foreach ($line in $efLines) { Write-Output $line }
        return
    }
    if ($text -like '*job execution show*') { return $global:diagStatus }
    if ($text -like '*job start*') { return 'caj-myvedicstar-migrate-prod-abc123' }
    if ($text -like '*job update*') { return }
    if ($text -like '*job show*') { return $global:diagDeclaredContainers }
    return
}

function Assert-Mentions([string] $Haystack, [string] $Needle, [string] $Because) {
    if (-not $Haystack.Contains($Needle)) {
        throw "$Because`n--- looked for: $Needle ---`n--- output was: ---`n$Haystack"
    }
}

try {
    # Run the real runner with arguments, capturing everything it writes.
    $global:diagCalls.Clear()
    $threw = $false
    $output = ''
    # The script writes diagnostics with Write-Host, which is NOT part of PowerShell's output stream -
    # only the object pipeline is. 2>&1 therefore captures nothing of the printed log, and an earlier
    # version of this test failed for exactly that reason while the log was in fact printed to the step.
    # Write-Host goes to the host, so the host information stream is what has to be redirected: 6>&1.
    $output = ''
    try {
        $output = (& $runner -RegistryName testregistry -ImageTag test-tag -ResourceGroup rg-test -SubscriptionId sub-id) 2>&1 6>&1 | Out-String
    }
    catch {
        $threw = $true
        $output = $output + [Environment]::NewLine + $_.Exception.Message
    }

    # 1. Verdict unchanged - still fails on Failed. Pinned FIRST because it is the regression this change
    #    could plausibly cause, and a diagnostics helper must never make a failure look like a pass.
    if (-not $threw) { throw 'Migration script did not fail on a Failed execution; the verdict semantics changed' }

    # 2. The log was actually fetched, for the right execution, with a bounded tail.
    $calls = ($global:diagCalls | ForEach-Object { $_ -join ' ' }) -join [Environment]::NewLine
    Assert-Mentions $calls 'job logs show' 'The script never asked for the job logs; the change is missing'
    Assert-Mentions $calls '--execution caj-myvedicstar-migrate-prod-abc123' 'Log fetch must name the failed execution'
    Assert-Mentions $calls '--tail 100' 'Log fetch must be tail-limited so the step stays readable'

    # 3. The EF error reached the step output - the defect being fixed.
    Assert-Mentions $output 'Applying migration' 'The EF migration name must appear in the step log'
    Assert-Mentions $output '42P07: relation "KundaliComparisons" already exists' 'The SQLSTATE and failing statement must appear in the step log'
    Assert-Mentions $output '--- Migration job log' 'The printed log must be delimited so it is legible beside wrapper output'

    # 4. The throw still names the execution and keeps the original wording.
    Assert-Mentions $output 'caj-myvedicstar-migrate-prod-abc123' 'The failure message must name the execution'
    Assert-Mentions $output 'API deployment stopped' 'Original wording must survive so anything keying on it still works'

    # 5. A log that cannot be read must not change the verdict, and must say what it tried.
    $global:diagLogFails = $true
    $global:diagDeclaredContainers = 'real-migrator-container'
    $global:diagCalls.Clear()
    $threw2 = $false
    $output2 = ''
    try {
        $output2 = (& $runner -RegistryName testregistry -ImageTag test-tag -ResourceGroup rg-test -SubscriptionId sub-id) 2>&1 6>&1 | Out-String
    }
    catch { $threw2 = $true; $output2 = $output2 + [Environment]::NewLine + $_.Exception.Message }
    if (-not $threw2) { throw 'An unreadable log changed the verdict: a diagnostics failure must never turn a failed migration into a pass' }
    Assert-Mentions $output2 'Could not read logs for container' 'An unreadable log must be reported, not silently skipped'
    Assert-Mentions $output2 'real-migrator-container' 'The fallback must name the containers the job actually declares'
    Assert-Mentions $output2 'API deployment stopped' 'The verdict wording must survive a diagnostics failure too'

    Write-Host 'Migration failure diagnostics: log fetched and printed, EF error in step output, fallback names real containers, verdict unchanged in both branches.'
}
finally {
    Remove-Item Function:global:az -ErrorAction SilentlyContinue
    Remove-Variable diagCalls, diagStatus, diagLogFails, diagDeclaredContainers -Scope Global -ErrorAction SilentlyContinue
}