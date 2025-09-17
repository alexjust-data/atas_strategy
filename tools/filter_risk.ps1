# tools/filter_risk.ps1
# Create filtered views from Risk Management logs
param(
    [Parameter(Mandatory)]
    [string]$Path,
    [string[]]$Tags = @('468/CALC', '468/RISK', '468/SL', '468/STR', '468/ORD', '468/POS'),
    [string]$OutputDir = ""
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Path)) {
    Write-Error "Log file not found: $Path"
    exit 1
}

# Auto-determine output directory
if ([string]::IsNullOrEmpty($OutputDir)) {
    $OutputDir = Split-Path $Path -Parent
}

$baseName = [System.IO.Path]::GetFileNameWithoutExtension($Path)
$timestamp = Get-Date -Format 'yyyyMMdd_HHmmss'

Write-Host "🔍 Filtering Risk Management logs..." -ForegroundColor Cyan
Write-Host "   Source:    $Path" -ForegroundColor Gray
Write-Host "   Output:    $OutputDir" -ForegroundColor Gray
Write-Host ""

$results = @{}

foreach ($tag in $Tags) {
    Write-Host "   Processing tag: $tag" -ForegroundColor Yellow

    $outputFile = Join-Path $OutputDir "$($baseName)_$($tag.Replace('/', '_'))_$timestamp.log"
    $matches = Get-Content $Path | Select-String -Pattern $tag

    if ($matches.Count -gt 0) {
        $matches | Set-Content -Path $outputFile -Encoding UTF8
        $results[$tag] = @{
            File = $outputFile
            Count = $matches.Count
        }
        Write-Host "     → $($matches.Count) lines → $outputFile" -ForegroundColor Green
    } else {
        Write-Host "     → No matches found" -ForegroundColor Gray
    }
}

# Create combined calculation view (CALC + SL)
if ($Tags -contains '468/CALC' -or $Tags -contains '468/SL') {
    Write-Host "   Creating combined calc view..." -ForegroundColor Yellow
    $calcFile = Join-Path $OutputDir "$($baseName)_combined_calc_$timestamp.log"
    $calcMatches = Get-Content $Path | Select-String -Pattern '468/CALC|468/SL'

    if ($calcMatches.Count -gt 0) {
        $calcMatches | Set-Content -Path $calcFile -Encoding UTF8
        $results['Combined_Calc'] = @{
            File = $calcFile
            Count = $calcMatches.Count
        }
        Write-Host "     → $($calcMatches.Count) lines → $calcFile" -ForegroundColor Green
    }
}

# Create summary report
Write-Host ""
Write-Host "📊 Filter Summary:" -ForegroundColor White
$totalLines = (Get-Content $Path).Count
Write-Host "   Total lines in source: $totalLines" -ForegroundColor Gray
Write-Host ""

foreach ($tag in $results.Keys) {
    $info = $results[$tag]
    $percentage = [math]::Round(($info.Count / $totalLines) * 100, 2)
    Write-Host "   $tag" -ForegroundColor White
    Write-Host "     Lines:    $($info.Count) ($percentage%)" -ForegroundColor Gray
    Write-Host "     File:     $($info.File)" -ForegroundColor Gray
    Write-Host ""
}

Write-Host "✅ Filtering completed" -ForegroundColor Green