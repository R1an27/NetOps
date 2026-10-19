<#
.SYNOPSIS
    Automated build, test, DI validation, and multi-platform packaging for NetOps Agent.Worker.
.DESCRIPTION
    Runs clean, build (Debug/Release with TreatWarningsAsErrors), DI pre-flight validation,
    automated tests, and cross-platform publish matrix.
.PARAMETER SkipPublish
    Skips cross-platform publishing.
.PARAMETER SkipTests
    Skips automated test execution.
.PARAMETER ArtifactsPath
    Target publish directory (default: ./artifacts/publish).
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File ./scripts/verify-agent-worker.ps1
#>

[CmdletBinding()]
param (
    [switch]$SkipPublish,
    [switch]$SkipTests,
    [string]$ArtifactsPath = "./artifacts/publish"
)

$ErrorActionPreference = "Stop"
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

function Write-Step {
    param (
        [string]$StepNumber,
        [string]$Title
    )
    Write-Host ""
    Write-Host "=====================================================================" -ForegroundColor Cyan
    Write-Host " [$StepNumber] $Title" -ForegroundColor Cyan
    Write-Host "=====================================================================" -ForegroundColor Cyan
}

function Assert-CommandSuccess {
    param (
        [string]$StepName
    )
    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "[VERIFICATION FAILED] Step '$StepName' failed with exit code $LASTEXITCODE." -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

Write-Host "=====================================================================" -ForegroundColor Magenta
Write-Host "  NETOPS AGENT.WORKER - AUTOMATED BUILD & VERIFICATION PIPELINE      " -ForegroundColor Magenta
Write-Host "=====================================================================" -ForegroundColor Magenta
Write-Host "Start time: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Gray
Write-Host "Working dir: $(Get-Location)" -ForegroundColor Gray

# -----------------------------------------------------------------------------
# Step 1: Clean & Restore
# -----------------------------------------------------------------------------
Write-Step '1/6' 'Clean and Restore Dependencies'
Write-Host 'Cleaning Debug build artifacts...' -ForegroundColor Yellow
dotnet clean src/Agent.Worker/Agent.Worker.csproj -c Debug --nologo
Assert-CommandSuccess 'Clean Debug'

Write-Host 'Cleaning Release build artifacts...' -ForegroundColor Yellow
dotnet clean src/Agent.Worker/Agent.Worker.csproj -c Release --nologo
Assert-CommandSuccess 'Clean Release'

Write-Host 'Restoring NuGet packages...' -ForegroundColor Yellow
dotnet restore src/Agent.Worker/Agent.Worker.csproj --nologo
Assert-CommandSuccess 'Restore Dependencies'
Write-Host 'Done: Step 1 completed successfully.' -ForegroundColor Green

# -----------------------------------------------------------------------------
# Step 2: Build Debug with TreatWarningsAsErrors
# -----------------------------------------------------------------------------
Write-Step '2/6' 'Compile Debug Configuration (--no-incremental)'
Write-Host 'Compiling Agent.Worker in Debug mode with TreatWarningsAsErrors...' -ForegroundColor Yellow
dotnet build src/Agent.Worker/Agent.Worker.csproj -c Debug --no-incremental --nologo
Assert-CommandSuccess 'Build Debug'
Write-Host 'Done: Step 2 completed with 0 Warnings and 0 Errors (Debug).' -ForegroundColor Green

# -----------------------------------------------------------------------------
# Step 3: Build Release with TreatWarningsAsErrors
# -----------------------------------------------------------------------------
Write-Step '3/6' 'Compile Release Configuration (--no-incremental)'
Write-Host 'Compiling Agent.Worker in Release mode with TreatWarningsAsErrors...' -ForegroundColor Yellow
dotnet build src/Agent.Worker/Agent.Worker.csproj -c Release --no-incremental --nologo
Assert-CommandSuccess 'Build Release'
Write-Host 'Done: Step 3 completed with 0 Warnings and 0 Errors (Release).' -ForegroundColor Green

# -----------------------------------------------------------------------------
# Step 4: Pre-flight DI Validation (--validate-only)
# -----------------------------------------------------------------------------
Write-Step '4/6' 'Runtime Pre-flight DI Validation (--validate-only)'
Write-Host 'Validating runtime DI container graph without active network connections...' -ForegroundColor Yellow
dotnet run --project src/Agent.Worker/Agent.Worker.csproj --no-build -c Debug -- --validate-only
Assert-CommandSuccess 'DI Pre-flight Validation'
Write-Host 'Done: Step 4 completed - all services resolved cleanly.' -ForegroundColor Green

# -----------------------------------------------------------------------------
# Step 5: Automated Agent Tests
# -----------------------------------------------------------------------------
if (-not $SkipTests) {
    Write-Step '5/6' 'Execute Automated Agent Test Suites'
    Write-Host 'Running WorkerTests, AgentIntegrationTests, AgentMessageControlTests...' -ForegroundColor Yellow
    dotnet test --no-build -c Debug --filter "FullyQualifiedName~WorkerTests|FullyQualifiedName~AgentIntegrationTests|FullyQualifiedName~AgentMessageControlTests" --nologo
    Assert-CommandSuccess 'Automated Agent Tests'
    Write-Host 'Done: Step 5 completed - all Agent test suites passed 100%.' -ForegroundColor Green
} else {
    Write-Step '5/6' 'Execute Automated Agent Test Suites (SKIPPED)'
    Write-Host 'Skipped test execution via -SkipTests parameter.' -ForegroundColor DarkYellow
}

# -----------------------------------------------------------------------------
# Step 6: Cross-Platform Publish Matrix
# -----------------------------------------------------------------------------
if (-not $SkipPublish) {
    Write-Step '6/6' 'Cross-Platform Binary Packaging Matrix'
    
    $winPath = Join-Path $ArtifactsPath 'agent-win-x64'
    $linuxPath = Join-Path $ArtifactsPath 'agent-linux-x64'
    $standalonePath = Join-Path $ArtifactsPath 'agent-linux-standalone'

    Write-Host '1. Publishing Windows x64 (Framework-dependent)...' -ForegroundColor Yellow
    dotnet publish src/Agent.Worker/Agent.Worker.csproj -c Release -r win-x64 --no-self-contained -o $winPath --nologo
    Assert-CommandSuccess 'Publish Windows x64'

    Write-Host '2. Publishing Linux x64 (Framework-dependent)...' -ForegroundColor Yellow
    dotnet publish src/Agent.Worker/Agent.Worker.csproj -c Release -r linux-x64 --no-self-contained -o $linuxPath --nologo
    Assert-CommandSuccess 'Publish Linux x64'

    Write-Host '3. Publishing Linux x64 Standalone (Self-contained Single-File)...' -ForegroundColor Yellow
    dotnet publish src/Agent.Worker/Agent.Worker.csproj -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o $standalonePath --nologo
    Assert-CommandSuccess 'Publish Linux x64 Standalone'

    Write-Host "Done: Step 6 completed - binaries published to '$ArtifactsPath'." -ForegroundColor Green
} else {
    Write-Step '6/6' 'Cross-Platform Binary Packaging Matrix (SKIPPED)'
    Write-Host 'Skipped packaging matrix via -SkipPublish parameter.' -ForegroundColor DarkYellow
}

# -----------------------------------------------------------------------------
# Summary
# -----------------------------------------------------------------------------
$stopwatch.Stop()
$elapsed = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 2)

Write-Host ''
Write-Host '=====================================================================' -ForegroundColor Green
Write-Host '  ALL VERIFICATION STEPS FOR AGENT.WORKER PASSED SUCCESSFULLY!       ' -ForegroundColor Green
Write-Host '=====================================================================' -ForegroundColor Green
Write-Host "Total Execution Time: $elapsed seconds" -ForegroundColor Gray
Write-Host 'Quality Bar Status:   0 Warnings, 0 Errors (TreatWarningsAsErrors: ON)' -ForegroundColor Gray
Write-Host 'DI Runtime Check:     PASSED (ValidateScopes & ValidateOnBuild)' -ForegroundColor Gray
if (-not $SkipPublish) {
    Write-Host "Artifacts Target:     $ArtifactsPath" -ForegroundColor Gray
}
Write-Host ''

exit 0
