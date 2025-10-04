# Deploy Strategies to ATAS
$ErrorActionPreference = "Stop"

Write-Host "Cleaning + Building MyAtas.Strategies..." -ForegroundColor Yellow
Set-Location "$PSScriptRoot\..\src\MyAtas.Strategies"
dotnet clean
dotnet build -c Debug

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

# Set destination path
$destinationPath = "C:\Users\AlexJ\AppData\Roaming\ATAS\Strategies"
Write-Host "Deploying to $destinationPath..." -ForegroundColor Green

# Create directory if not exists
if (-not (Test-Path $destinationPath)) {
    New-Item -ItemType Directory -Path $destinationPath -Force | Out-Null
}

# Copy DLLs and PDBs
$binPath = "bin\Debug\net8.0-windows"
if (-not (Test-Path $binPath)) {
    Write-Host "Build output not found at $binPath" -ForegroundColor Red
    exit 1
}

Copy-Item "$binPath\MyAtas.Strategies.dll" $destinationPath -Force
Copy-Item "$binPath\MyAtas.Strategies.pdb" $destinationPath -Force
Copy-Item "$binPath\MyAtas.Indicators.dll" $destinationPath -Force
Copy-Item "$binPath\MyAtas.Shared.dll" $destinationPath -Force
Copy-Item "$binPath\MyAtas.Risk.dll" $destinationPath -Force

Write-Host "Strategies deployed successfully to $destinationPath" -ForegroundColor Green
Write-Host "   - MyAtas.Strategies.dll" -ForegroundColor Gray
Write-Host "   - MyAtas.Strategies.pdb" -ForegroundColor Gray
Write-Host "   - MyAtas.Indicators.dll" -ForegroundColor Gray
Write-Host "   - MyAtas.Shared.dll" -ForegroundColor Gray
Write-Host "   - MyAtas.Risk.dll" -ForegroundColor Gray