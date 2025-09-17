
# DRY-RUN (solo muestra cambios)
# powershell -ExecutionPolicy Bypass -File tools\refactor_risk_logs.ps1 -DryRun

# Aplicar cambios
# powershell -ExecutionPolicy Bypass -File tools\refactor_risk_logs.ps1





Param(
  [string]$Root = "$(Split-Path -Parent $PSScriptRoot)\src\MyAtas.Strategies",
  [switch]$DryRun
)

Write-Host "Refactor logs bajo: $Root"

# Archivos a tocar
$files = Get-ChildItem -Path $Root -Filter "FourSixEightConfluencesStrategy_Simple.cs" -Recurse

if (-not $files) {
  Write-Host "No se encontró el archivo objetivo." -ForegroundColor Yellow
  exit 1
}

foreach ($f in $files) {
  $text = Get-Content -Raw -Path $f.FullName

  # Backups
  $bak = "$($f.FullName).bak_refactor_$(Get-Date -Format 'yyyyMMdd_HHmmss')"
  if (-not $DryRun) {
    Copy-Item $f.FullName $bak -Force
  }

  # Reemplazos con regex (solo tags literales "468/RISK" y "468/CALC")
  $new = $text `
    -replace 'DebugLog\.W\(\s*"468/RISK"\s*,', 'RiskLog("468/RISK",' `
    -replace 'DebugLog\.W\(\s*"468/CALC"\s*,', 'CalcLog("468/CALC",'

  if ($DryRun) {
    Write-Host "=== DRY-RUN: Diff simulado para $($f.Name) ==="
    # Muestra diferencias simples
    $a = $text -split "`r?`n"
    $b = $new  -split "`r?`n"
    # Imprime líneas cambiadas (modo simple)
    for ($i=0; $i -lt [Math]::Max($a.Length,$b.Length); $i++) {
      if ($i -lt $a.Length -and $i -lt $b.Length) {
        if ($a[$i] -ne $b[$i]) {
          Write-Host "- $($a[$i])" -ForegroundColor DarkGray
          Write-Host "+ $($b[$i])" -ForegroundColor Green
        }
      }
    }
  } else {
    Set-Content -Path $f.FullName -Value $new -NoNewline
    Write-Host "Refactor aplicado a $($f.Name). Backup: $([System.IO.Path]::GetFileName($bak))" -ForegroundColor Green
  }
}

Write-Host "Hecho."
