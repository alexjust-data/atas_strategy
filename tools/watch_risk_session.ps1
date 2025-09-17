param(
  [Parameter(Mandatory=$true)]
  [string]$LogPath,

  [int]$Tail = 0,                 # 0 = full scan, otherwise tail N lines
  [int]$RefreshMs = 1000          # watch interval
)

Write-Host "== Risk Session Watcher ==" -ForegroundColor Cyan
Write-Host "Log: $LogPath" -ForegroundColor DarkGray

if (-not (Test-Path $LogPath)) {
  Write-Error "Log file not found: $LogPath"
  exit 1
}

$seenSnapshot = $false
$seenAutoUse  = $false

function Scan-Block($lines) {
  foreach ($ln in $lines) {
    if ($ln -match '468/RISK.*INIT flags.*EnableRiskManagement=(\w+).*RiskDryRun=(\w+)') {
      $rm = $Matches[1]; $dry = $Matches[2]
      Write-Host "[INIT] RM=$rm DryRun=$dry" -ForegroundColor Yellow
    }

    if ($ln -match '468/CALC.*SNAPSHOT') { $global:seenSnapshot = $true }

    if ($ln -match '468/STR.*ENTRY qty source=AUTO') {
      $global:seenAutoUse = $true
      Write-Host "[AUTO] $ln" -ForegroundColor Green
    }
    elseif ($ln -match '468/STR.*ENTRY qty source=MANUAL') {
      Write-Host "[MANUAL] $ln" -ForegroundColor DarkYellow
    }
    elseif ($ln -match '468/STR.*ENTRY ABORTED: autoQty<=0') {
      Write-Host "[ABORT] $ln" -ForegroundColor Red
    }

    if ($ln -match '468/CALC.*Underfunded') {
      Write-Host "[UNDERFUNDED] $ln" -ForegroundColor Red
    }
    if ($ln -match '468/CALC.*SL DRIFT WARNING') {
      Write-Host "[SL-DRIFT] $ln" -ForegroundColor Magenta
    }
  }
}

$all = Get-Content -Path $LogPath -Encoding UTF8
if ($Tail -gt 0 -and $Tail -lt $all.Length) {
  $all = $all[-$Tail..-1]
}
Scan-Block $all

$lastSize = (Get-Item $LogPath).Length
while ($true) {
  Start-Sleep -Milliseconds $RefreshMs
  $size = (Get-Item $LogPath).Length
  if ($size -gt $lastSize) {
    $stream = [System.IO.File]::Open($LogPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    try {
      $stream.Seek($lastSize, [System.IO.SeekOrigin]::Begin) | Out-Null
      $reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::UTF8)
      $chunk = $reader.ReadToEnd()
      $reader.Close()
    } finally {
      $stream.Close()
    }
    $lines = $chunk -split "`r?`n" | Where-Object { $_ -ne '' }
    Scan-Block $lines
    $lastSize = $size
  }

  if (-not $seenSnapshot) {
    Write-Host "[HINT] No 468/CALC SNAPSHOT yet. If RM is ON, ensure RiskDryRun OFF to test soft-engage." -ForegroundColor DarkGray
  }
}
