# Deploy Indicators to ATAS
$ErrorActionPreference = "Stop"

Write-Host "🔨 Building MyAtas.Indicators..." -ForegroundColor Yellow
Set-Location "$PSScriptRoot\..\src\MyAtas.Indicators"
dotnet build -c Debug

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Build failed!" -ForegroundColor Red
    exit 1
}

# Set destination path directly
$dst = "C:\Users\AlexJ\AppData\Roaming\ATAS\Indicators"
Write-Host "📦 Deploying to $dst..." -ForegroundColor Green

# Create directory if not exists
if (-not (Test-Path $dst)) {
    New-Item -ItemType Directory -Path $dst -Force | Out-Null
}

# Copy DLLs and PDBs
$binPath = "bin\Debug\net8.0-windows"
if (-not (Test-Path $binPath)) {
    Write-Host "❌ Build output not found at $binPath" -ForegroundColor Red
    exit 1
}

Copy-Item "$binPath\MyAtas.Indicators.dll" $dst -Force
Copy-Item "$binPath\MyAtas.Indicators.pdb" $dst -Force
Copy-Item "$binPath\MyAtas.Shared.dll" $dst -Force

Write-Host "✅ Indicadores desplegados en $dst" -ForegroundColor Green
Write-Host "   - MyAtas.Indicators.dll" -ForegroundColor Gray
Write-Host "   - MyAtas.Indicators.pdb" -ForegroundColor Gray
Write-Host "   - MyAtas.Shared.dll" -ForegroundColor Gray