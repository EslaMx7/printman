#Requires -Version 5.1
<#
.SYNOPSIS
    Enforces the Printman unit-test coverage ratchet from a Cobertura report.

.DESCRIPTION
    Parses the Cobertura XML produced by Microsoft Code Coverage
    (`dotnet test --coverage --coverage-output-format cobertura`) and fails when line or
    branch coverage drops below the agreed floors.

    The floors are a RATCHET: they may only ever be raised as coverage improves, never
    lowered. Adding production code in a measured area without tests will fail this gate,
    which is intentional - add the tests or improve the existing ones.

    Measured areas are defined by `coverage.runsettings`; platform-bound code (Windows
    spooler, WinRT/GDI+ rendering, Kestrel hosting, console UI, sockets, composition root)
    is intentionally excluded and covered by the headless smoke tests in AGENTS.md instead.

.PARAMETER Path
    Cobertura file to check. Defaults to the most recently written
    `TestResults/*.cobertura.xml`.

.PARAMETER MinLine
    Minimum line coverage percentage required to pass.

.PARAMETER MinBranch
    Minimum branch coverage percentage required to pass.

.EXAMPLE
    dotnet test Printman.slnx --coverage --coverage-output-format cobertura --coverage-settings coverage.runsettings
    ./eng/check-coverage.ps1
#>
[CmdletBinding()]
param(
    [string]$Path,
    [double]$MinLine = 19,
    [double]$MinBranch = 16
)

$ErrorActionPreference = 'Stop'
$invariant = [System.Globalization.CultureInfo]::InvariantCulture

function Get-Rate {
    param([System.Xml.XmlElement]$Node, [string]$Attribute)
    $raw = $Node.GetAttribute($Attribute)
    if ([string]::IsNullOrWhiteSpace($raw)) { throw "Cobertura report is missing '$Attribute'." }
    return [double]::Parse($raw, $invariant) * 100.0
}

function Get-Int {
    param([System.Xml.XmlElement]$Node, [string]$Attribute)
    $raw = $Node.GetAttribute($Attribute)
    if ([string]::IsNullOrWhiteSpace($raw)) { return 0 }
    return [int]::Parse($raw, $invariant)
}

# Locate the report.
if (-not $Path) {
    $resultsDir = Join-Path (Join-Path $PSScriptRoot '..') 'TestResults'
    $candidate = Get-ChildItem -Path $resultsDir -Filter '*.cobertura.xml' -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if (-not $candidate) {
        throw "No Cobertura report found under '$resultsDir'. Run 'dotnet test ... --coverage --coverage-output-format cobertura' first."
    }
    $Path = $candidate.FullName
}

if (-not (Test-Path -LiteralPath $Path)) {
    throw "Coverage report not found: $Path"
}

[xml]$report = Get-Content -LiteralPath $Path -Raw
$coverage = $report.coverage
if (-not $coverage) { throw "Not a Cobertura report (no <coverage> root): $Path" }

$lineRate = Get-Rate -Node $coverage -Attribute 'line-rate'
$branchRate = Get-Rate -Node $coverage -Attribute 'branch-rate'
$linesCovered = Get-Int -Node $coverage -Attribute 'lines-covered'
$linesValid = Get-Int -Node $coverage -Attribute 'lines-valid'
$branchesCovered = Get-Int -Node $coverage -Attribute 'branches-covered'
$branchesValid = Get-Int -Node $coverage -Attribute 'branches-valid'

$linePass = $lineRate -ge $MinLine
$branchPass = $branchRate -ge $MinBranch
$passed = $linePass -and $branchPass

$lineStatus = if ($linePass) { 'PASS' } else { 'FAIL' }
$branchStatus = if ($branchPass) { 'PASS' } else { 'FAIL' }

Write-Host ''
Write-Host '=== Unit test coverage (ratchet) ===' -ForegroundColor Cyan
Write-Host ("  Report : {0}" -f (Resolve-Path -LiteralPath $Path).Path)
Write-Host ("  Lines    {0,6:N2}%  ({1}/{2})   floor {3,5:N2}%  [{4}]" -f $lineRate, $linesCovered, $linesValid, $MinLine, $lineStatus) -ForegroundColor $(if ($linePass) { 'Green' } else { 'Red' })
Write-Host ("  Branches {0,6:N2}%  ({1}/{2})   floor {3,5:N2}%  [{4}]" -f $branchRate, $branchesCovered, $branchesValid, $MinBranch, $branchStatus) -ForegroundColor $(if ($branchPass) { 'Green' } else { 'Red' })
Write-Host ''

# Job summary (GitHub Actions) and annotations.
$summaryPath = $env:GITHUB_STEP_SUMMARY
if ($summaryPath) {
    $markdown = @'
### Unit test coverage

| Metric | Coverage | Covered / Total | Floor | Result |
| :--- | ---: | ---: | ---: | :--- |
| Lines | {0:N2}% | {1} / {2} | {3:N2}% | {4} |
| Branches | {5:N2}% | {6} / {7} | {8:N2}% | {9} |

Coverage is a ratchet: the floors in `eng/check-coverage.ps1` may only be raised, never lowered.
'@ -f $lineRate, $linesCovered, $linesValid, $MinLine, $lineStatus, $branchRate, $branchesCovered, $branchesValid, $MinBranch, $branchStatus
    Add-Content -LiteralPath $summaryPath -Value $markdown
}

if (-not $passed) {
    Write-Host 'COVERAGE GATE FAILED: add or improve unit tests before merging.' -ForegroundColor Red
    $message = "Coverage below floor - lines {0:N2}% (floor {1:N2}%), branches {2:N2}% (floor {3:N2}%)." -f $lineRate, $MinLine, $branchRate, $MinBranch
    if ($env:GITHUB_ACTIONS -eq 'true') { Write-Host "::error::${message}" }
    exit 1
}

Write-Host 'COVERAGE GATE PASSED.' -ForegroundColor Green
exit 0
