# Api#193: a skipped image build must not read as a passed one.
#
# The defect this pins: build-image is gated to main, so every PR rollup showed it as SKIPPED, and a broken
# image build was first discovered on main. The fix adds opt-in triggers and a job whose NAME states whether
# the build ran. These assertions lock both halves so a later edit cannot quietly restore the blind spot.
#
# No workflow is dispatched and no Azure resource is contacted: the workflow file is parsed as text.
$ErrorActionPreference = 'Stop'
$repositoryPath = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$workflowPath = Join-Path $repositoryPath '.github/workflows/shared-deploy.yml'
if (-not (Test-Path -LiteralPath $workflowPath)) { throw "Missing workflow: $workflowPath" }
$workflow = Get-Content -LiteralPath $workflowPath -Raw

# Extracts the body of one top-level job, from its two-space indented name to the next top-level job.
function Get-JobBlock([string] $Name, [string] $Text) {
    $pattern = "(?ms)^  " + [regex]::Escape($Name) + ":\s*$.*?(?=^  [a-zA-Z0-9_-]+:\s*$|\z)"
    $match = [regex]::Match($Text, $pattern)
    if (-not $match.Success) { throw "Job '$Name' not found in shared-deploy.yml" }
    return $match.Value
}

function Assert-Contains([string] $Haystack, [string] $Needle, [string] $Because) {
    if (-not $Haystack.Contains($Needle)) { throw "$Because (missing: $Needle)" }
}

function Assert-NotContains([string] $Haystack, [string] $Needle, [string] $Because) {
    if ($Haystack.Contains($Needle)) { throw "$Because (unexpected: $Needle)" }
}

# --- No duplicate top-level trigger keys ----------------------------------------------------------------
# YAML takes the LAST of a duplicated key and discards the earlier one, silently. The first version of this
# change added a second `pull_request:` block and thereby deleted the original trigger's conditions: the
# workflow still parsed, but its pull_request configuration was not the one anyone had written. A syntax check
# cannot catch that, so it is asserted here.
$triggerSection = $workflow.Substring(0, $workflow.IndexOf('permissions:'))
foreach ($key in @('push:', 'pull_request:', 'workflow_dispatch:')) {
    $occurrences = ([regex]::Matches($triggerSection, "(?m)^  $([regex]::Escape($key))\s*$")).Count
    if ($occurrences -gt 1) { throw "shared-deploy.yml declares '$key' $occurrences times; YAML keeps only the last and silently drops the rest" }
}

$buildImage = Get-JobBlock 'build-image' $workflow
$deployProd = Get-JobBlock 'deploy-prod' $workflow
$verifyJob = Get-JobBlock 'verify-image-build' $workflow
$triggers = $workflow.Substring(0, $workflow.IndexOf('permissions:'))

# --- Opt-in surface -------------------------------------------------------------------------------------
Assert-Contains $triggers 'verify_image_build:' 'workflow_dispatch must expose a verify_image_build input'
Assert-Contains $triggers 'default: false' 'the verify_image_build input must default to false (off by default)'
Assert-Contains $triggers 'types: [opened, synchronize, reopened, labeled]' 'pull_request must re-run on label changes, or adding the label never triggers a run'
Assert-Contains $buildImage "contains(github.event.pull_request.labels.*.name" 'build-image must be reachable from a labelled pull request'

# --- The main-only deploy gate must survive untouched ---------------------------------------------------
Assert-Contains $buildImage "github.ref == 'refs/heads/main' && github.event_name != 'pull_request'" 'the original main-only build gate must remain, unweakened'
Assert-Contains $deployProd "github.ref == 'refs/heads/main'" 'deploy-prod must still require main'
Assert-Contains $deployProd "github.event_name == 'push'" 'deploy-prod must still require a push, so no PR can deploy'
Assert-Contains $deployProd "needs.build-image.result == 'success'" 'deploy-prod must still require a successful build'
Assert-NotContains $deployProd "github.event_name == 'workflow_dispatch'" 'a manual verify run must never deploy'
Assert-NotContains $deployProd "labels.*.name" 'a labelled PR must never deploy'

# --- The skip must be self-describing -------------------------------------------------------------------
# The job that reports the state must not carry an if: of its own, or it could skip and say nothing.
Assert-NotContains $verifyJob '    if:' 'verify-image-build must always run; an if: would let it skip silently'
Assert-Contains $verifyJob 'SKIPPED (not requested)' 'the skip state must be named, not left as a bare skipped'
Assert-Contains $verifyJob 'REQUIRED' 'the required state must be named'
Assert-Contains $verifyJob 'GITHUB_STEP_SUMMARY' 'the reason must be recorded on the job summary a reviewer reads'

# --- Behaviour of the decision logic, executed rather than described ------------------------------------
#
# LIMITATION, stated here because this is where a future editor will be looking:
# Test-ShouldBuild below is a MIRROR of shared-deploy.yml's build-image `if:` condition, not the condition
# itself. It proves the DECISION TABLE is right; it cannot prove the YAML implements that table. If someone
# edits the workflow's `if:` and does not edit this function, these cases keep passing while the real gate
# has changed.
#
# The only thing binding the two is the Assert-Contains checks on the YAML body above, and those are
# substring assertions - they catch a removed or reworded gate, not a subtly altered one.
#
# So: changing the workflow's `if:` MUST be paired with a change to Test-ShouldBuild. Nothing will fail if
# you forget; that is the cost of testing a workflow file without being able to run GitHub's evaluator.
#
# Mirrors the step's conditions so the default/opt-in split is asserted by running it, not by reading it.
# Mirrors the step's conditions so the default/opt-in split is asserted by running it, not by reading it.
function Test-ShouldBuild {
    param([string] $EventName, [string] $Ref, [string] $InputVerify, [string[]] $Labels, [string] $LabelName = 'verify-image-build')
    if ($Ref -eq 'refs/heads/main' -and $EventName -ne 'pull_request') { return $true }
    if ($InputVerify -eq 'true') { return $true }
    return ($Labels -contains $LabelName)
}

@(
    [pscustomobject]@{ Name = 'push to main';                EventName = 'push';              Ref = 'refs/heads/main';         InputVerify = '';      Labels = @();                      Expected = $true }
    [pscustomobject]@{ Name = 'ordinary pull request';       EventName = 'pull_request';      Ref = 'refs/pull/1/merge';       InputVerify = '';      Labels = @();                      Expected = $false }
    [pscustomobject]@{ Name = 'PR with unrelated label';     EventName = 'pull_request';      Ref = 'refs/pull/1/merge';       InputVerify = '';      Labels = @('documentation');       Expected = $false }
    [pscustomobject]@{ Name = 'PR with the opt-in label';    EventName = 'pull_request';      Ref = 'refs/pull/1/merge';       InputVerify = '';      Labels = @('verify-image-build');  Expected = $true }
    [pscustomobject]@{ Name = 'dispatch without the input';  EventName = 'workflow_dispatch'; Ref = 'refs/heads/some-branch';  InputVerify = '';      Labels = @();                      Expected = $false }
    [pscustomobject]@{ Name = 'dispatch with input false';   EventName = 'workflow_dispatch'; Ref = 'refs/heads/some-branch';  InputVerify = 'false'; Labels = @();                      Expected = $false }
    [pscustomobject]@{ Name = 'dispatch with the input set'; EventName = 'workflow_dispatch'; Ref = 'refs/heads/some-branch';  InputVerify = 'true';  Labels = @();                      Expected = $true }
) | ForEach-Object {
    $actual = Test-ShouldBuild $_.EventName $_.Ref $_.InputVerify $_.Labels
    if ($actual -ne $_.Expected) { throw "Image build decision wrong for '$($_.Name)': expected $($_.Expected), got $actual" }
}

# The default case is the one that let #193 through: prove it explicitly rather than relying on the loop.
if (Test-ShouldBuild 'pull_request' 'refs/pull/193/merge' '' @()) {
    throw 'Default pull request must NOT build the deploy image; that gate is what made #193 invisible'
}
if (-not (Test-ShouldBuild 'pull_request' 'refs/pull/193/merge' '' @('verify-image-build'))) {
    throw 'A pull request labelled verify-image-build MUST build the deploy image, or the opt-in is decorative'
}

Write-Host "Image build gate: 7 decision cases pass; skip is self-describing; deploy-prod still main-only."
