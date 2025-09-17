# tools/deploy_risk_management.ps1
# Risk Management Build, Deploy & Log Capture Tool

[CmdletBinding()]
param(
    [switch]$Release = $false,                 # Build Release (si no, Debug)
    [switch]$RefactorRiskLogs = $true,         # Reemplaza DebugLog.W("468/RISK|CALC", ...) -> RiskLog/CalcLog
    [int]$TailSeconds = 0,                     # 0 = no 'tail'; >0 = tail en vivo N segundos
    [string]$RuntimeLogPath = ""               # Log runtime (auto-detect si vacío)
)

$ErrorActionPreference = 'Stop'

function Write-Step($msg, [ConsoleColor]$color = 'Cyan') {
    Write-Host "==> $msg" -ForegroundColor $color
}
function Ensure-Dir($path) {
    if (-not (Test-Path $path)) {
        New-Item -ItemType Directory -Force -Path $path | Out-Null
    }
}
function Try-GitInfo {
    try {
        $sha = (git rev-parse --short HEAD) 2>$null
        if ($LASTEXITCODE -eq 0 -and $sha) { return $sha.Trim() }
    } catch { }
    return "nogit"
}

# -------------------------------------------------------------------------------------
# 0) Contexto y rutas
# -------------------------------------------------------------------------------------
$ts  = Get-Date -Format 'yyyyMMdd_HHmmss'
$day = Get-Date -Format 'yyyyMMdd'
$root = Split-Path -Parent $PSScriptRoot

$logRoot = Join-Path $root "out\logs\risk\$day"
Ensure-Dir $logRoot

$fullLog   = Join-Path $logRoot "full_risk_$ts.log"
$calcLog   = Join-Path $logRoot "risk_calc_$ts.log"
$diagLog   = Join-Path $logRoot "risk_diag_$ts.log"
$posordLog = Join-Path $logRoot "pos_ord_$ts.log"

$solution = Join-Path $root "01_ATAS_strategy.sln"
$projStrategies = Join-Path $root "src\MyAtas.Strategies\MyAtas.Strategies.csproj"
$projIndicators = Join-Path $root "src\MyAtas.Indicators\MyAtas.Indicators.csproj"

# Log runtime por defecto (lo genera tu infraestructura actual)
if ([string]::IsNullOrWhiteSpace($RuntimeLogPath)) {
    $RuntimeLogPath = Join-Path $root "logs\current\ATAS_SESSION_LOG.txt"
}

$cfg = $Release.IsPresent ? "Release" : "Debug"
$git = Try-GitInfo

Write-Step "RM Deploy — config=$cfg, git=$git, refactor=$RefactorRiskLogs, tail=$TailSeconds s"

# -------------------------------------------------------------------------------------
# 1) Refactor (opcional): gatear logs 468/RISK y 468/CALC
# -------------------------------------------------------------------------------------
if ($RefactorRiskLogs) {
    $refactor = Join-Path $root "tools\refactor_risk_logs.ps1"
    if (Test-Path $refactor) {
        Write-Step "Refactor de logs (RiskLog/CalcLog) en estrategia…" "DarkCyan"
        powershell -ExecutionPolicy Bypass -File $refactor
    } else {
        Write-Host "   (omitido) No existe: $refactor" -ForegroundColor DarkYellow
    }
}

# -------------------------------------------------------------------------------------
# 2) Build
# -------------------------------------------------------------------------------------
Write-Step "Compilando solución ($cfg)…"
& dotnet build $solution -c $cfg
if ($LASTEXITCODE -ne 0) {
    throw "Build failed (solution)."
}

# (Opcional) build proyectos directos si quieres mensajes separados:
# Write-Step "Compilando MyAtas.Indicators…"
# & dotnet build $projIndicators -c $cfg
# if ($LASTEXITCODE -ne 0) { throw "Build failed (Indicators)." }
#
# Write-Step "Compilando MyAtas.Strategies…"
# & dotnet build $projStrategies -c $cfg
# if ($LASTEXITCODE -ne 0) { throw "Build failed (Strategies)." }

# -------------------------------------------------------------------------------------
# 3) Despliegue a ATAS (reusa tu tooling existente)
# -------------------------------------------------------------------------------------
$deployStrategies = Join-Path $root "tools\deploy_strategies.ps1"
if (Test-Path $deployStrategies) {
    Write-Step "Desplegando Strategies a ATAS…"
    powershell -ExecutionPolicy Bypass -File $deployStrategies
    if ($LASTEXITCODE -ne 0) { throw "Deploy strategies failed." }
} else {
    Write-Host "   (omitido) No existe: $deployStrategies" -ForegroundColor DarkYellow
}

# -------------------------------------------------------------------------------------
# 4) Cabecera de sesión de logs
# -------------------------------------------------------------------------------------
$hdr = @(
"=== RM SESSION $ts ===",
"config=$cfg, git=$git",
"root=$root",
"runtimeLog=$RuntimeLogPath",
"======================"
) -join [Environment]::NewLine
$hdr | Out-File -FilePath $fullLog -Encoding UTF8
$hdr | Out-File -FilePath $calcLog -Encoding UTF8
$hdr | Out-File -FilePath $diagLog -Encoding UTF8
$hdr | Out-File -FilePath $posordLog -Encoding UTF8

# -------------------------------------------------------------------------------------
# 5) Snapshot inmediato de logs existentes
# -------------------------------------------------------------------------------------
if (Test-Path $RuntimeLogPath) {
    Write-Step "Extrayendo snapshot de logs de riesgo desde runtime…"

    # Full subset de riesgo (RISK + CALC)
    Get-Content $RuntimeLogPath | Select-String -Pattern '468/RISK|468/CALC' |
        Out-File -FilePath $fullLog -Append -Encoding UTF8

    # Cálculo (SNAPSHOT, PULSE, MANUAL/FIXED/PERCENT, etc.)
    Get-Content $RuntimeLogPath | Select-String -Pattern '468/CALC' |
        Out-File -FilePath $calcLog -Append -Encoding UTF8

    # Diagnóstico de risk (INIT, SYMBOL, DIAG, TICK-VALUE, ACCOUNT, SL distance)
    Get-Content $RuntimeLogPath | Select-String -Pattern '468/RISK.*(INIT|SYMBOL|DIAG|TICK-VALUE|ACCOUNT|SL distance|fallback|auto-detected|override)' |
        Out-File -FilePath $diagLog -Append -Encoding UTF8

    # Pos/Order (no estricto a RM, pero útil para correlación)
    Get-Content $RuntimeLogPath | Select-String -Pattern '468/STR|468/ORD|ENTRY|TP|SL|BRACKETS' |
        Out-File -FilePath $posordLog -Append -Encoding UTF8
} else {
    Write-Host "⚠️  Runtime log no encontrado: $RuntimeLogPath" -ForegroundColor DarkYellow
}

# -------------------------------------------------------------------------------------
# 6) Tail en vivo (opcional)
# -------------------------------------------------------------------------------------
if ($TailSeconds -gt 0 -and (Test-Path $RuntimeLogPath)) {
    Write-Step "Tailing runtime $TailSeconds s…" "Yellow"
    $end = (Get-Date).AddSeconds($TailSeconds)
    $lastLen = (Get-Item $RuntimeLogPath).Length

    while (Get-Date) -lt $end {
        Start-Sleep -Milliseconds 500
        $len = (Get-Item $RuntimeLogPath).Length
        if ($len -gt $lastLen) {
            $delta = $len - $lastLen
            $lastLen = $len
            # Lee las últimas ~150 líneas para capturar el bloque reciente
            $chunk = Get-Content $RuntimeLogPath -Tail 150

            # Append a fullLog
            $chunk | Out-File -FilePath $fullLog -Append -Encoding UTF8

            # Filtrar a otros
            ($chunk | Select-String -Pattern '468/CALC') |
                Out-File -FilePath $calcLog -Append -Encoding UTF8
            ($chunk | Select-String -Pattern '468/RISK.*(INIT|SYMBOL|DIAG|TICK-VALUE|ACCOUNT|SL distance|fallback|auto-detected|override)') |
                Out-File -FilePath $diagLog -Append -Encoding UTF8
            ($chunk | Select-String -Pattern '468/STR|468/ORD|ENTRY|TP|SL|BRACKETS') |
                Out-File -FilePath $posordLog -Append -Encoding UTF8

            # Preview en consola (lo más relevante)
            $preview = $chunk | Select-String -Pattern '468/CALC|468/RISK'
            if ($preview) {
                Write-Host ($preview -join [Environment]::NewLine) -ForegroundColor Gray
            }
        }
    }
}

# -------------------------------------------------------------------------------------
# 7) Paquete de sesión (ZIP)
# -------------------------------------------------------------------------------------
try {
    $bundle = Join-Path $logRoot "rm_session_$ts.zip"
    if (Test-Path $bundle) { Remove-Item $bundle -Force }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]
    $tmp = Join-Path $logRoot "tmp_$ts"
    Ensure-Dir $tmp

    Copy-Item $fullLog   $tmp
    Copy-Item $calcLog   $tmp
    Copy-Item $diagLog   $tmp
    Copy-Item $posordLog $tmp

    $meta = @(
        "git=$git",
        "config=$cfg",
        "runtimeLog=$RuntimeLogPath",
        "created=$((Get-Date).ToString('s'))"
    ) -join [Environment]::NewLine
    $meta | Out-File -FilePath (Join-Path $tmp "session_meta.txt") -Encoding UTF8

    [IO.Compression.ZipFile]::CreateFromDirectory($tmp, $bundle)
    Remove-Item $tmp -Recurse -Force
    Write-Step "Bundle creado: $bundle" "Green"
} catch {
    Write-Host "⚠️  No se pudo crear ZIP de sesión: $($_.Exception.Message)" -ForegroundColor DarkYellow
}

# -------------------------------------------------------------------------------------
# 8) Summary
# -------------------------------------------------------------------------------------
Write-Host ""
Write-Host "✅ Risk Management Deploy Complete!" -ForegroundColor Green
Write-Host "📂 Log files created:" -ForegroundColor White
Write-Host "   📄 Full:        $fullLog" -ForegroundColor Gray
Write-Host "   🧮 Calc:       $calcLog" -ForegroundColor Gray
Write-Host "   🔍 Diag:       $diagLog" -ForegroundColor Gray
Write-Host "   📊 Pos/Ord:    $posordLog" -ForegroundColor Gray
Write-Host ""
Write-Host "📦 Bundle: $(Join-Path $logRoot "rm_session_$ts.zip")" -ForegroundColor Gray
Write-Host ""
Write-Host "🚀 Next steps:" -ForegroundColor Yellow
Write-Host "   1. Restart ATAS to load new DLLs" -ForegroundColor White
Write-Host "   2. Enable strategy with 'EnableRiskManagement = ON' y 'RiskDryRun = ON' (Capa 0)" -ForegroundColor White
Write-Host "   3. Reproduce/replay y usa '-TailSeconds 60' para ver SNAPSHOTs en vivo" -ForegroundColor White
Write-Host ""
