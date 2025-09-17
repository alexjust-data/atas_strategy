# tools/deploy_risk_management.ps1
# Risk Management Build, Deploy & Log Capture Tool
param(
    [switch]$Release = $false,
    [int]$TailSeconds = 0,  # 0 para no 'tail'; >0 para leer runtime en caliente
    [string]$RuntimeLogPath = ""  # Ruta del log runtime (auto-detect si vacío)
)

$ErrorActionPreference = 'Stop'
$ts = Get-Date -Format 'yyyyMMdd_HHmmss'
$day = Get-Date -Format 'yyyyMMdd'
$logRoot = Join-Path $PSScriptRoot "..\out\logs\risk\$day"
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null

$fullLog   = Join-Path $logRoot "full_risk_$ts.log"
$calcLog   = Join-Path $logRoot "risk_calc_$ts.log"
$diagLog   = Join-Path $logRoot "risk_diag_$ts.log"
$posordLog = Join-Path $logRoot "risk_pos_ord_$ts.log"

Write-Host "🚀 Risk Management Build & Deploy..." -ForegroundColor Cyan
Write-Host "   Timestamp: $ts" -ForegroundColor Gray
Write-Host "   Log Root:  $logRoot" -ForegroundColor Gray

# 1) Build
$cfg = $Release.IsPresent ? 'Release' : 'Debug'
$sln = Resolve-Path "..\src\MyAtas.Strategies\MyAtas.Strategies.csproj"
Write-Host "📦 Building $cfg configuration..." -ForegroundColor Yellow
dotnet build $sln -c $cfg 2>&1 | Tee-Object -FilePath $fullLog

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Build failed!" -ForegroundColor Red
    exit 1
}

# 2) Deploy (reuse existing deploy script)
Write-Host "🚚 Deploying to ATAS..." -ForegroundColor Yellow
& "$PSScriptRoot\deploy_all.ps1" 2>&1 | Tee-Object -FilePath $fullLog -Append

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Deploy failed!" -ForegroundColor Red
    exit 1
}

# 3) Auto-detect runtime log path if not provided
if ([string]::IsNullOrEmpty($RuntimeLogPath)) {
    $possiblePaths = @(
        "..\logs\current\ATAS_SESSION_LOG.txt",
        "..\ATAS_SESSION_LOG.txt",
        "$env:LOCALAPPDATA\MyAtas\logs\atas_risk_runtime.log"
    )

    foreach ($path in $possiblePaths) {
        $resolved = Resolve-Path $path -ErrorAction SilentlyContinue
        if ($resolved -and (Test-Path $resolved)) {
            $RuntimeLogPath = $resolved
            break
        }
    }
}

# 4) Process runtime logs if available
if ($RuntimeLogPath -and (Test-Path $RuntimeLogPath)) {
    Write-Host "📄 Processing runtime logs from: $RuntimeLogPath" -ForegroundColor Yellow

    # Snapshot estático
    Get-Content $RuntimeLogPath | Tee-Object -FilePath $fullLog -Append | Out-Null

    # Vistas filtradas
    Write-Host "🔍 Creating filtered views..." -ForegroundColor Cyan

    # Risk calculation logs (468/CALC, 468/SL)
    Get-Content $RuntimeLogPath | Select-String -Pattern '468/CALC|468/SL' |
        Set-Content -Encoding UTF8 $calcLog

    # Risk diagnostics logs (468/RISK)
    Get-Content $RuntimeLogPath | Select-String -Pattern '468/RISK' |
        Set-Content -Encoding UTF8 $diagLog

    # Position/Order/Strategy logs (468/ORD, 468/POS, 468/STR)
    Get-Content $RuntimeLogPath | Select-String -Pattern '468/ORD|468/POS|468/STR' |
        Set-Content -Encoding UTF8 $posordLog

    Write-Host "✅ Filtered views created" -ForegroundColor Green
} else {
    Write-Warning "Runtime log not found or not specified: $RuntimeLogPath"
    Write-Host "   Use -RuntimeLogPath parameter to specify log location" -ForegroundColor Gray
}

# 5) Tail "en caliente" opcional (durante N segundos)
if ($TailSeconds -gt 0 -and $RuntimeLogPath -and (Test-Path $RuntimeLogPath)) {
    Write-Host "📡 Tailing runtime for $TailSeconds seconds..." -ForegroundColor Yellow
    $end = (Get-Date).AddSeconds($TailSeconds)
    $lastSize = (Get-Item $RuntimeLogPath).Length

    while (Get-Date -lt $end) {
        $currentSize = (Get-Item $RuntimeLogPath).Length
        if ($currentSize -gt $lastSize) {
            $newLines = Get-Content $RuntimeLogPath -Tail 50 | Select-Object -Last 10
            $newLines | Tee-Object -FilePath $fullLog -Append | Out-Null

            # Show risk-related lines in console
            $riskLines = $newLines | Select-String -Pattern '468/CALC|468/RISK|468/SL'
            if ($riskLines) {
                $riskLines | ForEach-Object { Write-Host $_ -ForegroundColor Cyan }
            }
        }
        $lastSize = $currentSize
        Start-Sleep -Milliseconds 500
    }
    Write-Host "📡 Tail completed" -ForegroundColor Green
}

# 6) Summary
Write-Host ""
Write-Host "✅ Risk Management Deploy Complete!" -ForegroundColor Green
Write-Host "📂 Log files created:" -ForegroundColor White
Write-Host "   📄 Full:        $fullLog" -ForegroundColor Gray
Write-Host "   🧮 Calc:       $calcLog" -ForegroundColor Gray
Write-Host "   🔍 Diag:       $diagLog" -ForegroundColor Gray
Write-Host "   📊 Pos/Ord:    $posordLog" -ForegroundColor Gray
Write-Host ""
Write-Host "🚀 Next steps:" -ForegroundColor Yellow
Write-Host "   1. Restart ATAS to load new DLLs" -ForegroundColor White
Write-Host "   2. Enable strategy with 'Enable detailed risk logging = ✓'" -ForegroundColor White
Write-Host "   3. Use tools/tail_risk.ps1 for live monitoring" -ForegroundColor White
Write-Host ""