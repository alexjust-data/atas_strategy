# Clean old MyAtas DLLs from ATAS folders
$ErrorActionPreference = "Stop"

Write-Host "🧹 Cleaning old MyAtas files from ATAS..." -ForegroundColor Yellow

# Ensure APPDATA is available
if (-not $env:APPDATA) {
    Write-Host "❌ APPDATA environment variable not found" -ForegroundColor Red
    exit 1
}

$indicators = "$env:APPDATA\ATAS\Indicators"
$strategies = "$env:APPDATA\ATAS\Strategies"

# Remove old files
$filesToRemove = @(
    "$indicators\MyAtas.*.dll",
    "$indicators\MyAtas.*.pdb",
    "$indicators\MyAtasIndicator.*",
    "$strategies\MyAtas.*.dll",
    "$strategies\MyAtas.*.pdb",
    "$strategies\MyAtasIndicator.*"
)

$removedCount = 0
foreach ($pattern in $filesToRemove) {
    $files = Get-ChildItem -Path $pattern -ErrorAction SilentlyContinue
    foreach ($file in $files) {
        Remove-Item $file.FullName -Force
        Write-Host "   ❌ Removed: $($file.Name)" -ForegroundColor Red
        $removedCount++
    }
}

if ($removedCount -eq 0) {
    Write-Host "   ℹ️  No old MyAtas files found" -ForegroundColor Gray
} else {
    Write-Host "   🗑️  Removed $removedCount files" -ForegroundColor Yellow
}

Write-Host "✅ ATAS folders cleaned!" -ForegroundColor Green