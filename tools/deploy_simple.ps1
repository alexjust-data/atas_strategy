# Simple Deploy Script (no colors for bash compatibility)
$ErrorActionPreference = "Stop"

Write-Host "Building and deploying MyAtas projects..."

# Ensure APPDATA is available
if (-not $env:APPDATA) {
    Write-Host "ERROR: APPDATA environment variable not found"
    exit 1
}

# Build and deploy Indicators
Write-Host "Building MyAtas.Indicators..."
Set-Location "$PSScriptRoot\..\src\MyAtas.Indicators"
dotnet build -c Debug

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Indicators build failed!"
    exit 1
}

$dst = "$env:APPDATA\ATAS\Indicators"
Write-Host "Deploying to $dst"

if (-not (Test-Path $dst)) {
    New-Item -ItemType Directory -Path $dst -Force | Out-Null
}

$binPath = "bin\Debug\net8.0-windows"
Copy-Item "$binPath\MyAtas.Indicators.dll" $dst -Force
Copy-Item "$binPath\MyAtas.Indicators.pdb" $dst -Force
Copy-Item "$binPath\MyAtas.Shared.dll" $dst -Force

Write-Host "Indicators deployed successfully"

# Build and deploy Strategies
Write-Host "Building MyAtas.Strategies..."
Set-Location "$PSScriptRoot\..\src\MyAtas.Strategies"
dotnet build -c Debug

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Strategies build failed!"
    exit 1
}

$dst = "$env:APPDATA\ATAS\Strategies"
Write-Host "Deploying to $dst"

if (-not (Test-Path $dst)) {
    New-Item -ItemType Directory -Path $dst -Force | Out-Null
}

$binPath = "bin\Debug\net8.0-windows"
Copy-Item "$binPath\MyAtas.Strategies.dll" $dst -Force
Copy-Item "$binPath\MyAtas.Strategies.pdb" $dst -Force
Copy-Item "$binPath\MyAtas.Indicators.dll" $dst -Force
Copy-Item "$binPath\MyAtas.Shared.dll" $dst -Force

Write-Host "Strategies deployed successfully"
Write-Host "Deployment complete! Remember to restart ATAS."