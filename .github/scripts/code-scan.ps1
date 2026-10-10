<#
.SYNOPSIS
    Advisory code scan: reports findings as GitHub annotations and in the job summary, and never fails.

.DESCRIPTION
    One check per run, so each appears as its own workflow step:
      Analyzers   compiler and .NET analyzer warnings/errors from a Release build (needs a prior restore)
      Format      code-style deviations from .editorconfig (dotnet format --verify-no-changes)
      Packages    known-vulnerable direct and transitive NuGet packages
      Scripts     PSScriptAnalyzer findings in deploy/scripts

    Findings never change the exit code: the scan informs, the test job gates. A check that cannot run (for
    example a tool error) is reported as a warning, not a failure. Locally the annotations print as plain lines.

.EXAMPLE
    ./.github/scripts/code-scan.ps1 -Check Packages
#>
#Requires -Version 7
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Analyzers', 'Format', 'Packages', 'Scripts')]
    [string] $Check,

    [string] $Solution = 'MyVedicStar.slnx',

    # Annotations beyond this many per check are only counted, so a noisy check cannot flood the run.
    [int] $MaxAnnotations = 50
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$script:annotationCount = 0

function ConvertTo-AnnotationText([string] $Text) {
    # Workflow-command escaping: % first, then line breaks.
    return $Text.Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A')
}

function Write-Annotation([ValidateSet('error', 'warning', 'notice')] [string] $Level, [string] $Message, [string] $File, [int] $Line) {
    $script:annotationCount++
    if ($script:annotationCount -gt $MaxAnnotations) { return }

    $properties = @()
    if ($File) {
        $relative = [System.IO.Path]::GetRelativePath($repositoryRoot, [System.IO.Path]::GetFullPath($File, $repositoryRoot)).Replace('\', '/')
        $properties += "file=$relative"
        if ($Line -gt 0) { $properties += "line=$Line" }
    }
    $prefix = if ($properties) { "::$Level $($properties -join ',')::" } else { "::$Level::" }
    Write-Host "$prefix$(ConvertTo-AnnotationText $Message)"
}

function Write-Summary([string] $Title, [int] $Errors, [int] $Warnings, [string] $Details) {
    $status = if ($Errors -gt 0) { 'errors' } elseif ($Warnings -gt 0) { 'warnings' } else { 'clean' }
    $markdown = "### Code scan: $Title ($status)`n`n| Errors | Warnings |`n|---:|---:|`n| $Errors | $Warnings |`n"
    if ($Details) { $markdown += "`n$Details`n" }
    if ($script:annotationCount -gt $MaxAnnotations) {
        $markdown += "`nOnly the first $MaxAnnotations findings are annotated; see the step log for the rest.`n"
    }

    if ($env:GITHUB_STEP_SUMMARY) {
        Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $markdown -Encoding utf8
    }
    Write-Host "$Title`: $Errors error(s), $Warnings warning(s)"
}

function Invoke-AnalyzersCheck {
    # actions/setup-dotnet registers a problem matcher, so build diagnostics are already annotated in Actions;
    # here they are only counted. MSBuild repeats each diagnostic in its final summary, hence the de-duplication.
    & dotnet build $Solution --no-restore --configuration Release -nologo 2>&1 | Tee-Object -Variable output | Out-Host
    $buildExitCode = $LASTEXITCODE
    $diagnostics = @($output | ForEach-Object { "$_" } |
            Where-Object { $_ -match ':\s(warning|error)\s[A-Z]+[0-9]+:' } |
            ForEach-Object { ($_ -replace '\s+\[[^\]]+\]\s*$', '').Trim() } |
            Sort-Object -Unique)
    $errors = @($diagnostics | Where-Object { $_ -match ':\serror\s' }).Count
    $warnings = $diagnostics.Count - $errors

    if ($buildExitCode -ne 0 -and $errors -eq 0) {
        Write-Annotation warning "The build failed without compiler diagnostics (exit code $buildExitCode); see the step log."
    }

    $byRule = $diagnostics |
        ForEach-Object { if ($_ -match ':\s(?:warning|error)\s([A-Z]+[0-9]+):') { $Matches[1] } } |
        Group-Object | Sort-Object Count -Descending | Select-Object -First 10
    $details = if ($byRule) { "| Rule | Count |`n|---|---:|`n" + (($byRule | ForEach-Object { "| $($_.Name) | $($_.Count) |" }) -join "`n") } else { '' }
    Write-Summary 'compiler and .NET analyzers' $errors $warnings $details
}

function Invoke-FormatCheck {
    $report = Join-Path ([System.IO.Path]::GetTempPath()) "format-report-$PID.json"
    try {
        & dotnet format $Solution --verify-no-changes --no-restore --report $report --verbosity quiet *> $null
        $documents = if (Test-Path -LiteralPath $report) { @(Get-Content -Raw -LiteralPath $report | ConvertFrom-Json) } else { @() }
    }
    finally {
        Remove-Item -LiteralPath $report -Force -ErrorAction SilentlyContinue
    }

    $changes = 0
    foreach ($document in $documents) {
        foreach ($change in @($document.FileChanges)) {
            $changes++
            Write-Annotation warning "$($change.DiagnosticId): $($change.FormatDescription)" $document.FilePath ([int] $change.LineNumber)
        }
    }
    Write-Summary 'code style (dotnet format)' 0 $changes "Fix locally with ``dotnet format $Solution``."
}

function Get-JsonArray($Object, [string] $Name) {
    # The JSON report omits empty lists (for example "frameworks" for a project without findings).
    $property = $Object.PSObject.Properties[$Name]
    if ($property -and $property.Value) { return @($property.Value) }
    return @()
}

function Invoke-PackagesCheck {
    # stdout only; the CLI may still print "warn :" lines before the JSON document.
    $output = & dotnet list $Solution package --vulnerable --include-transitive --format json 2> $null | Out-String
    $exitCode = $LASTEXITCODE
    $start = $output.IndexOf('{')
    if ($exitCode -ne 0 -or $start -lt 0) {
        Write-Host $output
        Write-Annotation warning "Vulnerable-package check could not run (exit code $exitCode); see the step log."
        Write-Summary 'vulnerable packages' 0 1 'The check could not run; see the step log.'
        return
    }

    # The same package can be reported by several projects and frameworks: report each package once.
    $findings = @{}
    foreach ($project in @(Get-JsonArray ($output.Substring($start) | ConvertFrom-Json) 'projects')) {
        foreach ($framework in @(Get-JsonArray $project 'frameworks')) {
            $packages = @(Get-JsonArray $framework 'topLevelPackages') + @(Get-JsonArray $framework 'transitivePackages')
            foreach ($package in $packages) {
                foreach ($vulnerability in @(Get-JsonArray $package 'vulnerabilities')) {
                    $key = "$($package.id)|$($package.resolvedVersion)|$($vulnerability.advisoryurl)"
                    $findings[$key] = [pscustomobject]@{
                        Package  = $package.id
                        Version  = $package.resolvedVersion
                        Severity = $vulnerability.severity
                        Advisory = $vulnerability.advisoryurl
                    }
                }
            }
        }
    }

    $errors = 0
    $warnings = 0
    foreach ($finding in $findings.Values | Sort-Object Package) {
        $level = if ($finding.Severity -in 'High', 'Critical') { $errors++; 'error' } else { $warnings++; 'warning' }
        Write-Annotation $level "$($finding.Package) $($finding.Version) has a $($finding.Severity) vulnerability: $($finding.Advisory)"
    }

    $details = if ($findings.Count -gt 0) {
        "| Package | Version | Severity | Advisory |`n|---|---|---|---|`n" +
        (($findings.Values | Sort-Object Package | ForEach-Object { "| $($_.Package) | $($_.Version) | $($_.Severity) | $($_.Advisory) |" }) -join "`n")
    }
    else { '' }
    Write-Summary 'vulnerable packages (High/Critical = error)' $errors $warnings $details
}

function Invoke-ScriptsCheck {
    if (-not (Get-Module -ListAvailable PSScriptAnalyzer)) {
        Install-Module PSScriptAnalyzer -Scope CurrentUser -Force
    }
    $findings = @(Invoke-ScriptAnalyzer -Path (Join-Path $repositoryRoot 'deploy/scripts') -Recurse -Severity Error, Warning `
            -ExcludeRule PSAvoidUsingWriteHost, PSUseShouldProcessForStateChangingFunctions)

    foreach ($finding in $findings) {
        $level = if ("$($finding.Severity)" -eq 'Error') { 'error' } else { 'warning' }
        Write-Annotation $level "$($finding.RuleName): $($finding.Message)" $finding.ScriptPath $finding.Line
    }
    $errors = @($findings | Where-Object { "$($_.Severity)" -eq 'Error' }).Count
    Write-Summary 'deployment scripts (PSScriptAnalyzer)' $errors ($findings.Count - $errors) ''
}

Push-Location $repositoryRoot
try {
    switch ($Check) {
        'Analyzers' { Invoke-AnalyzersCheck }
        'Format' { Invoke-FormatCheck }
        'Packages' { Invoke-PackagesCheck }
        'Scripts' { Invoke-ScriptsCheck }
    }
}
catch {
    # Advisory by design: a scan that cannot run is reported, never fatal.
    Write-Annotation warning "Code scan '$Check' could not complete: $($_.Exception.Message)"
    Write-Summary $Check 0 1 'The check could not complete; see the step log.'
}
finally {
    Pop-Location
}
exit 0
