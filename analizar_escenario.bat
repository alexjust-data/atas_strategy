@echo off
REM Script para analizar resultados de escenarios
REM Uso: analizar_escenario.bat [A|B|C|D|E|F]

if "%1"=="" (
    echo.
    echo ANÁLISIS DE ESCENARIOS DISPONIBLES:
    echo A - BASELINE: analiza ambas confluencias + guard + ejecuciones
    echo B - CONF1_ONLY: analiza solo confluence GL slope
    echo C - CONF2_ONLY: analiza solo confluence EMA8 vs Wilder8
    echo D - CONF2_STRICT: analiza fallos de confluence EMA8 estricta
    echo E - STRICT_N1: analiza timing y expiraciones N+1
    echo F - GUARD_TEST: analiza comportamiento de OnlyOnePosition guard
    echo.
    echo Uso: analizar_escenario.bat [A^|B^|C^|D^|E^|F]
    echo.
    goto :eof
)

set ESCENARIO=%1

echo ===================================================================
echo ANÁLISIS ESCENARIO %ESCENARIO%
echo ===================================================================
echo.

if /I "%ESCENARIO%"=="A" (
    echo BASELINE - Ambas confluencias activas:
    echo.
    echo 1. Buscando señales completas ^(CONF#1 + CONF#2 + GUARD + EJECUCIÓN^):
    grep -nE "CAPTURE: N=|CONF#1 .* -> |CONF#2 .* -> |GUARD OnlyOnePosition|MARKET ORDER SENT|BRACKETS ATTACHED" ATAS_SESSION_LOG.txt
    echo.
    echo 2. Resumen de confluencias:
    grep -c "CONF#1.*-> OK" ATAS_SESSION_LOG.txt && echo " CONF#1 OK"
    grep -c "CONF#1.*-> FAIL" ATAS_SESSION_LOG.txt && echo " CONF#1 FAIL"
    grep -c "CONF#2.*-> OK" ATAS_SESSION_LOG.txt && echo " CONF#2 OK"
    grep -c "CONF#2.*-> FAIL" ATAS_SESSION_LOG.txt && echo " CONF#2 FAIL"
    echo.
    echo 3. Decisiones del guard:
    grep -c "GUARD.*-> PASS" ATAS_SESSION_LOG.txt && echo " GUARD PASS"
    grep -c "GUARD.*-> BLOCK" ATAS_SESSION_LOG.txt && echo " GUARD BLOCK"

) else if /I "%ESCENARIO%"=="B" (
    echo CONF1_ONLY - Solo GenialLine slope:
    echo.
    echo 1. Análisis de CONF#1:
    grep -nE "CAPTURE: N=|CONF#1 .* -> (OK|FAIL)|ABORT ENTRY: Conf#1|MARKET ORDER SENT" ATAS_SESSION_LOG.txt
    echo.
    echo 2. Contadores:
    grep -c "CONF#1.*-> OK" ATAS_SESSION_LOG.txt && echo " CONF#1 válidas"
    grep -c "CONF#1.*-> FAIL" ATAS_SESSION_LOG.txt && echo " CONF#1 fallos"
    grep -c "ABORT ENTRY: Conf#1" ATAS_SESSION_LOG.txt && echo " Abortos por CONF#1"

) else if /I "%ESCENARIO%"=="C" (
    echo CONF2_ONLY - Solo EMA8 vs Wilder8:
    echo.
    echo 1. Análisis de CONF#2:
    grep -nE "CAPTURE: N=|CONF#2 .* -> (OK|FAIL)|ABORT ENTRY: Conf#2|MARKET ORDER SENT" ATAS_SESSION_LOG.txt
    echo.
    echo 2. Detalles de ventana:
    grep -n "CONF#2.*diff=" ATAS_SESSION_LOG.txt
    echo.
    echo 3. Contadores:
    grep -c "CONF#2.*-> OK" ATAS_SESSION_LOG.txt && echo " CONF#2 válidas"
    grep -c "CONF#2.*-> FAIL" ATAS_SESSION_LOG.txt && echo " CONF#2 fallos"

) else if /I "%ESCENARIO%"=="D" (
    echo CONF2_STRICT - EMA8 vs Wilder8 estricto:
    echo.
    echo 1. Análisis de fallos esperados:
    grep -nE "CONF#2 .* -> FAIL|ABORT ENTRY: Conf#2 failed" ATAS_SESSION_LOG.txt
    echo.
    echo 2. Razones de fallo:
    grep -n "CONF#2.*diff=" ATAS_SESSION_LOG.txt
    echo.
    echo 3. Total de rechazos:
    grep -c "CONF#2.*-> FAIL" ATAS_SESSION_LOG.txt && echo " Total fallos CONF#2"

) else if /I "%ESCENARIO%"=="E" (
    echo STRICT_N1 - Control de timing:
    echo.
    echo 1. Análisis de timing y expiraciones:
    grep -nE "PENDING ARMED|PROCESSING PENDING @N\+1|PENDING EXPIRED|beyond open tolerance|first tick" ATAS_SESSION_LOG.txt
    echo.
    echo 2. Contadores de timing:
    grep -c "PENDING ARMED" ATAS_SESSION_LOG.txt && echo " Señales armadas"
    grep -c "PENDING EXPIRED" ATAS_SESSION_LOG.txt && echo " Señales expiradas"
    grep -c "beyond open tolerance" ATAS_SESSION_LOG.txt && echo " Fuera de tolerancia"

) else if /I "%ESCENARIO%"=="F" (
    echo GUARD_TEST - Test de OnlyOnePosition:
    echo.
    echo 1. Análisis completo del guard:
    grep -nE "GUARD OnlyOnePosition|Trade lock RELEASED" ATAS_SESSION_LOG.txt
    echo.
    echo 2. Patrones de bloqueo/liberación:
    echo "--- Decisiones del guard: ---"
    grep -n "GUARD.*active=" ATAS_SESSION_LOG.txt
    echo.
    echo "--- Liberaciones del lock: ---"
    grep -n "Trade lock RELEASED" ATAS_SESSION_LOG.txt
    echo.
    echo 3. Estadísticas:
    grep -c "GUARD.*-> PASS" ATAS_SESSION_LOG.txt && echo " Guard permite entrada"
    grep -c "GUARD.*-> BLOCK" ATAS_SESSION_LOG.txt && echo " Guard bloquea entrada"
    grep -c "Trade lock RELEASED" ATAS_SESSION_LOG.txt && echo " Lock liberado"

) else (
    echo ERROR: Escenario '%ESCENARIO%' no válido
    echo Usar: A, B, C, D, E, o F
    goto :eof
)

echo.
echo ===================================================================
echo FIN ANÁLISIS ESCENARIO %ESCENARIO%
echo ===================================================================