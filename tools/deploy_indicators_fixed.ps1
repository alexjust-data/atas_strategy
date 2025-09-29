# Deploy Indicators to ATAS - FIXED VERSION
$ErrorActionPreference = "Stop"

Write-Host "Building MyAtas.Indicators..." -ForegroundColor Yellow
Set-Location "$PSScriptRoot\..\src\MyAtas.Indicators"
dotnet build -c Debug

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

# Set destination path
$dstPath = "C:\Users\AlexJ\AppData\Roaming\ATAS\Indicators"
Write-Host "Deploying to $dstPath..." -ForegroundColor Green

# Create directory if not exists
if (-not (Test-Path $dstPath)) {
    New-Item -ItemType Directory -Path $dstPath -Force | Out-Null
}

# Copy DLLs and PDBs
$binPath = "bin\Debug\net8.0-windows"
if (-not (Test-Path $binPath)) {
    Write-Host "Build output not found at $binPath" -ForegroundColor Red
    exit 1
}

Copy-Item "$binPath\MyAtas.Indicators.dll" $dstPath -Force
Copy-Item "$binPath\MyAtas.Indicators.pdb" $dstPath -Force
Copy-Item "$binPath\MyAtas.Shared.dll" $dstPath -Force

Write-Host "Indicators deployed successfully to $dstPath" -ForegroundColor Green
Write-Host "   - MyAtas.Indicators.dll" -ForegroundColor Gray
Write-Host "   - MyAtas.Indicators.pdb" -ForegroundColor Gray
Write-Host "   - MyAtas.Shared.dll" -ForegroundColor Gray