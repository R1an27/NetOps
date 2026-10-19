<#
.SYNOPSIS
    Automated test execution, categorization filtering, code coverage collection, and reliability testing for NetOps.
.DESCRIPTION
    Runs test suites with xUnit traits filtering (Category, Area), collects Coverlet code coverage,
    exports Trx test reports, and supports repeated execution loops for zero-flakiness verification.
.PARAMETER Category
    Filter tests by Category trait: All (default), Unit, Integration, Stress, Resilience.
.PARAMETER Area
    Filter tests by Area trait: All (default), Protocol, Agent, Master, Client.
.PARAMETER Configuration
    Build configuration: Release (default) or Debug.
.PARAMETER Coverage
    Collect code coverage using tests/coverlet.runsettings and display a summary table.
.PARAMETER Repeat
    Number of execution rounds (default: 1) for flakiness detection and stress verification.
.PARAMETER Trx
    Export test results in Visual Studio .trx format into TestResults/.
.PARAMETER NoBuild
    Skip build step before running tests.
.EXAMPLE
    powershell -File ./scripts/run-tests.ps1
    powershell -File ./scripts/run-tests.ps1 -Category Unit
    powershell -File ./scripts/run-tests.ps1 -Coverage
    powershell -File ./scripts/run-tests.ps1 -Repeat 5
#>

[CmdletBinding()]
param (
    [ValidateSet('All', 'Unit', 'Integration', 'Stress', 'Resilience')]
    [string]$Category = 'All',

    [ValidateSet('All', 'Protocol', 'Agent', 'Master', 'Client')]
    [string]$Area = 'All',

    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',

    [switch]$Coverage,

    [ValidateRange(1, 100)]
    [int]$Repeat = 1,

    [switch]$Trx,

    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$testProjectPath = 'tests/NetOps.Tests/NetOps.Tests.csproj'
$testResultsDir = 'tests/NetOps.Tests/TestResults'

Write-Host ''
Write-Host '=====================================================================' -ForegroundColor Magenta
Write-Host '  NETOPS AUTOMATED TEST RUNNER & VERIFICATION ENGINE                 ' -ForegroundColor Magenta
Write-Host '=====================================================================' -ForegroundColor Magenta
Write-Host "Execution Time  : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Gray
Write-Host "Configuration   : $Configuration" -ForegroundColor Gray
Write-Host "Category Filter : $Category" -ForegroundColor Gray
Write-Host "Area Filter     : $Area" -ForegroundColor Gray
Write-Host "Coverage Mode   : $(if ($Coverage) { 'Enabled (tests/coverlet.runsettings)' } else { 'Disabled' })" -ForegroundColor Gray
Write-Host "Repeat Rounds   : $Repeat" -ForegroundColor Gray
Write-Host '=====================================================================' -ForegroundColor Magenta

# -----------------------------------------------------------------------------
# Step 1: Build Filter Arguments
# -----------------------------------------------------------------------------
$filterParts = @()
if ($Category -ne 'All') {
    $filterParts += "Category=$Category"
}
if ($Area -ne 'All') {
    $filterParts += "Area=$Area"
}
$filterString = if ($filterParts.Count -gt 0) { $filterParts -join '&' } else { $null }

# -----------------------------------------------------------------------------
# Step 2: Clean old test results if coverage or trx is requested
# -----------------------------------------------------------------------------
if ($Coverage -or $Trx) {
    if (Test-Path $testResultsDir) {
        Write-Host "Cleaning previous test results in $testResultsDir..." -ForegroundColor Yellow
        Remove-Item -Path $testResultsDir -Recurse -Force
    }
}

# -----------------------------------------------------------------------------
# Step 3: Optional Pre-Build
# -----------------------------------------------------------------------------
if (-not $NoBuild) {
    Write-Host ''
    Write-Host "Building $testProjectPath ($Configuration)..." -ForegroundColor Cyan
    dotnet build $testProjectPath -c $Configuration --warnaserror --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[BUILD FAILED] Test project compilation failed with exit code $LASTEXITCODE." -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

# -----------------------------------------------------------------------------
# Step 4: Test Execution Loop (Repeat Support)
# -----------------------------------------------------------------------------
for ($round = 1; $round -le $Repeat; $round++) {
    if ($Repeat -gt 1) {
        Write-Host ''
        Write-Host ">>> [ROUND $round / $Repeat] Executing test suite..." -ForegroundColor Cyan
    } else {
        Write-Host ''
        Write-Host 'Executing test suite...' -ForegroundColor Cyan
    }

    $cmdArgs = @('test', $testProjectPath, '-c', $Configuration, '--nologo')

    if ($NoBuild) {
        $cmdArgs += '--no-build'
    }

    if ($null -ne $filterString) {
        $cmdArgs += @('--filter', $filterString)
    }

    if ($Coverage) {
        $cmdArgs += @('--settings', 'tests/coverlet.runsettings', '--collect:XPlat Code Coverage')
    }

    if ($Trx) {
        $trxName = if ($Repeat -gt 1) { "test-results-round-$round.trx" } else { "test-results.trx" }
        $cmdArgs += @('--logger', "trx;LogFileName=$trxName")
    }

    & dotnet $cmdArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Host ''
        Write-Host "[TESTS FAILED] Test run failed on round $round with exit code $LASTEXITCODE." -ForegroundColor Red
        exit $LASTEXITCODE
    }

    if ($Repeat -gt 1) {
        Write-Host "Round $round / $Repeat completed successfully (100% Passed)." -ForegroundColor Green
    }
}

# -----------------------------------------------------------------------------
# Step 5: Process and Display Code Coverage Summary
# -----------------------------------------------------------------------------
if ($Coverage) {
    Write-Host ''
    Write-Host '=====================================================================' -ForegroundColor Cyan
    Write-Host '  CODE COVERAGE REPORT (Coverlet / Cobertura XML)                    ' -ForegroundColor Cyan
    Write-Host '=====================================================================' -ForegroundColor Cyan

    $coverageFiles = Get-ChildItem -Path $testResultsDir -Recurse -Filter 'coverage.cobertura.xml' -ErrorAction SilentlyContinue
    if ($coverageFiles -and $coverageFiles.Count -gt 0) {
        $coberturaFile = $coverageFiles[0].FullName
        Write-Host "Report file: $coberturaFile" -ForegroundColor Gray
        Write-Host ''

        try {
            [xml]$xml = Get-Content $coberturaFile
            $packages = $xml.coverage.packages.package

            Write-Host ('{0,-28} | {1,14} | {2,10}' -f 'Module / Assembly', 'Line Coverage', 'Status') -ForegroundColor White
            Write-Host ('{0,-28}-|-{1,14}-|-{2,10}' -f ('-' * 28), ('-' * 14), ('-' * 10)) -ForegroundColor Gray

            foreach ($pkg in $packages) {
                $pkgName = $pkg.name
                $lineRate = [double]$pkg.GetAttribute('line-rate')
                $pct = [math]::Round($lineRate * 100, 2)
                $status = if ($pct -ge 80.0) { 'EXCELLENT' } elseif ($pct -ge 65.0) { 'GOOD' } else { 'FAIR' }
                $color = if ($pct -ge 80.0) { 'Green' } elseif ($pct -ge 65.0) { 'Yellow' } else { 'Red' }

                Write-Host ('{0,-28} | {1,13}% | ' -f $pkgName, $pct) -NoNewline
                Write-Host ('{0,-10}' -f $status) -ForegroundColor $color
            }

            $totalLineRate = [double]$xml.coverage.GetAttribute('line-rate')
            $totalPct = [math]::Round($totalLineRate * 100, 2)
            Write-Host ('{0,-28}-|-{1,14}-|-{2,10}' -f ('-' * 28), ('-' * 14), ('-' * 10)) -ForegroundColor Gray
            Write-Host ('{0,-28} | {1,13}% | ' -f 'Total Solution Core', $totalPct) -NoNewline
            Write-Host ('{0,-10}' -f 'OVERALL') -ForegroundColor Cyan
        } catch {
            Write-Host "[WARNING] Could not parse coverage summary: $_" -ForegroundColor Yellow
        }
    } else {
        Write-Host '[WARNING] No coverage.cobertura.xml file found in TestResults.' -ForegroundColor Yellow
    }
}

# -----------------------------------------------------------------------------
# Final Summary
# -----------------------------------------------------------------------------
$stopwatch.Stop()
$elapsed = [math]::Round($stopwatch.Elapsed.TotalSeconds, 2)

Write-Host ''
Write-Host '=====================================================================' -ForegroundColor Green
Write-Host '  VERIFICATION SUCCESSFUL (100% PASSED)                              ' -ForegroundColor Green
Write-Host '=====================================================================' -ForegroundColor Green
Write-Host "Total Rounds Executed : $Repeat" -ForegroundColor White
Write-Host "Category Filter       : $Category" -ForegroundColor White
Write-Host "Area Filter           : $Area" -ForegroundColor White
Write-Host "Total Time Elapsed    : ${elapsed}s" -ForegroundColor White
Write-Host '=====================================================================' -ForegroundColor Green
Write-Host ''
exit 0
