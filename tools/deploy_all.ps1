# Deploy All (Indicators + Strategies) to ATAS
$ErrorActionPreference = "Stop"

Write-Host "Deploying MyAtas.Indicators + MyAtas.Strategies to ATAS..." -ForegroundColor Cyan
Write-Host ("=" * 60) -ForegroundColor DarkGray

# Deploy Indicators first
Write-Host "Phase 1: Deploying Indicators..." -ForegroundColor Yellow
& "$PSScriptRoot\deploy_indicators_fixed.ps1"

if ($LASTEXITCODE -ne 0) {
    Write-Host "Indicators deployment failed!" -ForegroundColor Red
    exit 1
}

Write-Host ""

# Deploy Strategies second
Write-Host "Phase 2: Deploying Strategies..." -ForegroundColor Yellow
& "$PSScriptRoot\deploy_strategies.ps1"

if ($LASTEXITCODE -ne 0) {
    Write-Host "Strategies deployment failed!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "All deployments completed successfully!" -ForegroundColor Green
Write-Host "Remember to restart ATAS to load the new versions." -ForegroundColor Cyan