@echo off
REM Script para cambiar entre escenarios de testing
REM Uso: setup_escenario.bat [A|B|C|D|E|F]

if "%1"=="" (
    echo.
    echo ESCENARIOS DISPONIBLES:
    echo A - BASELINE: ambas confluencias activas
    echo B - CONF1_ONLY: solo GL slope
    echo C - CONF2_ONLY: solo EMA8 vs Wilder8 Window
    echo D - CONF2_STRICT: EMA8 vs Wilder8 estricto
    echo E - STRICT_N1: control de timing estricto
    echo F - GUARD_TEST: test de OnlyOnePosition guard
    echo.
    echo Uso: setup_escenario.bat [A^|B^|C^|D^|E^|F]
    echo.
    goto :eof
)

set ESCENARIO=%1

REM Backup del log actual si existe
if exist "ATAS_SESSION_LOG.txt" (
    echo Backing up current session log...
    move "ATAS_SESSION_LOG.txt" "ATAS_SESSION_LOG_backup_%date:~-4,4%%date:~-10,2%%date:~-7,2%_%time:~0,2%%time:~3,2%%time:~6,2%.txt" 2>nul
)

REM Setup del escenario específico
if /I "%ESCENARIO%"=="A" (
    echo Configurando ESCENARIO A - BASELINE...
    copy "ATAS_SESSION_LOG_A_baseline.txt" "ATAS_SESSION_LOG.txt"
    echo.
    echo CONFIG REQUERIDA:
    echo - Require GenialLine slope = ON
    echo - Require EMA8 vs Wilder8 = ON
    echo - EMA vs Wilder rule = Window
    echo - Pre-cross tolerance = 2
    echo - Count equality = ON
    echo.
    echo GREP: grep -nE "CAPTURE: N=|CONF#1 .* -^> |CONF#2 .* -^> |GUARD OnlyOnePosition|MARKET ORDER SENT|BRACKETS ATTACHED" ATAS_SESSION_LOG.txt
) else if /I "%ESCENARIO%"=="B" (
    echo Configurando ESCENARIO B - CONF1_ONLY...
    copy "ATAS_SESSION_LOG_B_conf1_only.txt" "ATAS_SESSION_LOG.txt"
    echo.
    echo CONFIG REQUERIDA:
    echo - Require GenialLine slope = ON
    echo - Require EMA8 vs Wilder8 = OFF
    echo.
    echo GREP: grep -nE "CAPTURE: N=|CONF#1 .* -^> ^(OK^|FAIL^)|ABORT ENTRY: Conf#1|MARKET ORDER SENT" ATAS_SESSION_LOG.txt
) else if /I "%ESCENARIO%"=="C" (
    echo Configurando ESCENARIO C - CONF2_ONLY...
    copy "ATAS_SESSION_LOG_C_conf2_only.txt" "ATAS_SESSION_LOG.txt"
    echo.
    echo CONFIG REQUERIDA:
    echo - Require GenialLine slope = OFF
    echo - Require EMA8 vs Wilder8 = ON
    echo - Pre-cross tolerance = 4
    echo - Count equality = ON
    echo.
    echo GREP: grep -nE "CAPTURE: N=|CONF#2 .* -^> ^(OK^|FAIL^)|ABORT ENTRY: Conf#2|MARKET ORDER SENT" ATAS_SESSION_LOG.txt
) else if /I "%ESCENARIO%"=="D" (
    echo Configurando ESCENARIO D - CONF2_STRICT...
    copy "ATAS_SESSION_LOG_D_conf2_strict.txt" "ATAS_SESSION_LOG.txt"
    echo.
    echo CONFIG REQUERIDA:
    echo - Require GL slope = OFF
    echo - Require EMA vs Wilder = ON
    echo - Pre-cross tolerance = 0
    echo - Count equality = OFF
    echo.
    echo GREP: grep -nE "CONF#2 .* -^> FAIL|ABORT ENTRY: Conf#2 failed" ATAS_SESSION_LOG.txt
) else if /I "%ESCENARIO%"=="E" (
    echo Configurando ESCENARIO E - STRICT_N1...
    copy "ATAS_SESSION_LOG_E_strict_n1.txt" "ATAS_SESSION_LOG.txt"
    echo.
    echo CONFIG REQUERIDA:
    echo - Strict N+1 = ON
    echo - Open tolerance ^(ticks^) = 0
    echo - Confluences: ambas ON
    echo.
    echo GREP: grep -nE "PENDING ARMED|PROCESSING PENDING @N\\+1|PENDING EXPIRED|beyond open tolerance|first tick" ATAS_SESSION_LOG.txt
) else if /I "%ESCENARIO%"=="F" (
    echo Configurando ESCENARIO F - GUARD_TEST...
    copy "ATAS_SESSION_LOG_F_guard_test.txt" "ATAS_SESSION_LOG.txt"
    echo.
    echo CONFIG REQUERIDA:
    echo - Como Escenario A ^(ambas confluencias ON^)
    echo - Cooldown = 2 barras
    echo.
    echo GREP: grep -nE "GUARD OnlyOnePosition|Trade lock RELEASED" ATAS_SESSION_LOG.txt
) else (
    echo ERROR: Escenario '%ESCENARIO%' no válido
    echo Usar: A, B, C, D, E, o F
    goto :eof
)

echo.
echo ✅ Escenario %ESCENARIO% configurado
echo ✅ Archivo ATAS_SESSION_LOG.txt preparado
echo.
echo SIGUIENTE PASO:
echo 1. Configura la estrategia en ATAS con los parámetros mostrados
echo 2. Reactiva la estrategia ^(OFF→ON^) estando flat
echo 3. Comprueba que aparece "INIT OK ^(Indicator attached...^)" en el log
echo 4. Espera a que aparezca 1 entrada y cierre ^(TP/SL^)
echo.