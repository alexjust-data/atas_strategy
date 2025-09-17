# 05. Risk Management Troubleshooting Guide

## Table of Contents
- [Common Issues](#common-issues)
- [Diagnostic Procedures](#diagnostic-procedures)
- [Error Categories](#error-categories)
- [Recovery Procedures](#recovery-procedures)
- [Known Issues](#known-issues)
- [FAQ](#faq)

## Common Issues

### 1. Strategy Not Calculating Quantities

#### Symptoms
```
CALC OUT mode=FixedRiskUSD qtyFinal=0 note="orders still use manual Quantity=2 until STEP4"
```

#### Root Causes
1. **Underfunded Protection Active**
2. **Zero tick value detected**
3. **Missing account equity**
4. **SL distance calculation error**

#### Diagnostic Steps
```bash
# Check for underfunded events
grep -n "UNDERFUNDED" logs/current/ATAS_SESSION_LOG.txt | tail -3

# Verify calculation inputs
grep -n "CALC IN.*slTicks=.*tickCost=.*equity=" logs/current/ATAS_SESSION_LOG.txt | tail -1

# Check auto-detection status
grep -n "auto-detected\|fallback\|override" logs/current/ATAS_SESSION_LOG.txt | tail -5
```

#### Solutions
```bash
# For underfunded: Increase RiskPerTradeUsd or verify tick values
# For missing equity: Check Portfolio API or set manual override
# For zero SL: Check StopOffsetTicks configuration
```

### 2. Incorrect Tick Values

#### Symptoms
```
RISK_FALLBACK used=tickCost value=0.5
WARNING: Using fallback tick value
```

#### Root Causes
1. **Security.TickCost returns null**
2. **Symbol not in CSV overrides**
3. **API connection issues**

#### Diagnostic Steps
```bash
# Check tick value source
grep -n "tickCost.*source=\|TickCost.*null" logs/current/ATAS_SESSION_LOG.txt

# Verify CSV override parsing
grep -n "OVERRIDE.*raw=.*hit=" logs/current/ATAS_SESSION_LOG.txt

# Check instrument symbol detection
grep -n "instr=.*code=" logs/current/ATAS_SESSION_LOG.txt
```

#### Solutions
```bash
# Add symbol to TickValueCsvOverrides: "MNQ=0.5;NQ=5;ES=12.5"
# Verify symbol name matches exactly
# Check ATAS connection to data feed
```

### 3. Account Equity Detection Failures

#### Symptoms
```
DIAG equity(auto)=0.00USD equitySrc=Balance
WARNING: Portfolio.BalanceAvailable is null, using Balance
```

#### Root Causes
1. **Portfolio object null**
2. **Account not connected**
3. **Demo account with no balance reporting**

#### Diagnostic Steps
```bash
# Check portfolio status
grep -n "Portfolio.*null\|BalanceAvailable.*null" logs/current/ATAS_SESSION_LOG.txt

# Verify equity source progression
grep -n "equitySrc=" logs/current/ATAS_SESSION_LOG.txt | tail -3

# Check for manual overrides
grep -n "AccountEquityOverride" logs/current/ATAS_SESSION_LOG.txt
```

#### Solutions
```bash
# Set AccountEquityOverride manually
# Verify ATAS account connection
# Check demo account vs live account settings
```

## Diagnostic Procedures

### Complete System Health Check

#### 1. Initialization Diagnostics
```bash
# Check strategy loading
grep -n "INIT.*attached\|VERSION" logs/current/ATAS_SESSION_LOG.txt

# Verify indicator attachment
grep -n "GenialLine.*attached\|reflection" logs/current/ATAS_SESSION_LOG.txt

# Check configuration loading
grep -n "RISK SNAPSHOT" logs/current/ATAS_SESSION_LOG.txt | tail -1
```

#### 2. Real-Time Calculation Health
```bash
# Monitor calculation pipeline
grep -n "CALC IN\|CALC.*qtyFinal=\|CALC OUT" logs/current/ATAS_SESSION_LOG.txt | tail -10

# Check throttling behavior
grep -n "CALC PING.*bar=" logs/current/ATAS_SESSION_LOG.txt | tail -5

# Verify input consistency
grep -n "slTicks=.*tickCost=.*equity=" logs/current/ATAS_SESSION_LOG.txt | tail -3
```

#### 3. API Integration Status
```bash
# Portfolio API health
grep -n "Portfolio\|Balance" logs/current/ATAS_SESSION_LOG.txt | tail -5

# Security API responses
grep -n "Security\|TickCost\|TickSize" logs/current/ATAS_SESSION_LOG.txt | tail -5

# Fallback usage tracking
grep -n "RISK_FALLBACK\|fallback" logs/current/ATAS_SESSION_LOG.txt | wc -l
```

### Signal Flow Diagnostics

#### 1. Entry Signal Pipeline
```bash
# Trace complete signal flow for specific UID
powershell -ExecutionPolicy Bypass -File "tools\extract_uid.ps1" -Path "logs\current\ATAS_SESSION_LOG.txt" -Uid "abc123" -ShowTimeline

# Check confluence validation
grep -nE "Conf#[12].*(PASS|FAIL)" uid_abc123_*.log

# Verify guard status
grep -n "OnlyOne.*PASS\|Cooldown.*OFF" uid_abc123_*.log
```

#### 2. Quantity Calculation Flow
```bash
# Extract calculation progression
grep -n "qtyRaw=.*qtySnap=.*qtyClamp=.*qtyFinal=" logs/current/ATAS_SESSION_LOG.txt

# Check underfunded protection
grep -n "underfunded=.*action=" logs/current/ATAS_SESSION_LOG.txt

# Verify lot step processing
grep -n "qtySnap\|LotSize" logs/current/ATAS_SESSION_LOG.txt
```

## Error Categories

### Critical Errors (Trading Stopped)

#### ERROR: Calculation Engine Failure
```
ERROR 468/CALC FATAL uid=abc123 exception=DivideByZeroException slTicks=0
```
**Impact**: No position sizing calculations
**Action**: Emergency stop, manual override mode

#### ERROR: API Null Reference
```
ERROR Portfolio object is null, cannot determine account equity
```
**Impact**: All percent-of-account calculations fail
**Action**: Switch to FixedRiskUSD mode immediately

### Warning Errors (Degraded Function)

#### WARNING: Fallback Values Active
```
WARNING 468/RISK RISK_FALLBACK used=tickCost value=0.5
```
**Impact**: Potentially incorrect position sizing
**Action**: Verify tick values, update CSV overrides

#### WARNING: Underfunded Protection
```
WARNING 468/CALC UNDERFUNDED action=ABORT qty=0 rpc=25.00USD target=10.00USD
```
**Impact**: Entries skipped due to excessive risk
**Action**: Adjust risk parameters or verify market conditions

### Info Errors (Monitor Only)

#### INFO: Auto-Detection Active
```
INFO 468/RISK auto-detected tickCost=0.50USD/t via Security.TickCost
```
**Impact**: None, system working correctly
**Action**: Monitor for consistency

## Recovery Procedures

### Emergency Trading Stop
```bash
# 1. Set strategy to Manual mode, Quantity = 0
# 2. Verify no pending orders
grep -n "activeOrders=0\|live=0" logs/current/ATAS_SESSION_LOG.txt

# 3. Close any open positions manually
# 4. Monitor for confirmation
grep -n "net=0\|pnlOpen=0" logs/current/ATAS_SESSION_LOG.txt
```

### Calculation Engine Reset
```bash
# 1. Force diagnostic refresh
# Set RefreshDiagnostics = true in ATAS UI

# 2. Monitor reset confirmation
grep -n "DIAG.*manual-refresh" logs/current/ATAS_SESSION_LOG.txt

# 3. Verify new calculation cycle
grep -n "CALC IN.*mode=" logs/current/ATAS_SESSION_LOG.txt | tail -1

# 4. Check for error resolution
grep -n "ERROR\|FATAL" logs/current/ATAS_SESSION_LOG.txt | tail -5
```

### API Reconnection
```bash
# 1. Check connection status
grep -n "Portfolio.*connected\|Security.*connected" logs/current/ATAS_SESSION_LOG.txt

# 2. Force API refresh by changing instruments
# Switch to different instrument, then back

# 3. Monitor API responses
grep -n "auto-detected.*via" logs/current/ATAS_SESSION_LOG.txt | tail -3

# 4. Verify data consistency
grep -n "RISK SNAPSHOT" logs/current/ATAS_SESSION_LOG.txt | tail -1
```

## Known Issues

### Issue #1: Nullable Decimal API Changes
**Symptoms**: Compilation errors with TickCost properties
**Workaround**: Use null coalescence operators (`Portfolio?.BalanceAvailable ?? 0m`)
**Status**: Fixed in current version

### Issue #2: LotSize Property Inconsistency
**Symptoms**: Intermittent compilation errors with lot sizing
**Workaround**: Disabled advanced lot sizing temporarily
**Status**: Under investigation

### Issue #3: Demo Account Balance Reporting
**Symptoms**: BalanceAvailable always returns 0 in demo
**Workaround**: Use AccountEquityOverride for demo accounts
**Status**: ATAS platform limitation

### Issue #4: Signal Candle SL Buffer Double-Application
**Symptoms**: SL distance calculated as 2x expected
**Workaround**: Verify UseSignalCandleSL is correctly implemented
**Status**: Resolved in PASO 3

## FAQ

### Q: Why is my quantity always 0?
**A**: Check for underfunded protection events. Either increase RiskPerTradeUsd or verify tick values are correct.

### Q: Strategy shows wrong tick values for my instrument
**A**: Add your instrument to TickValueCsvOverrides: `"SYMBOL=value;SYMBOL2=value2"`

### Q: Account equity shows 0 but I have balance
**A**: Set AccountEquityOverride manually, or check if you're using a demo account.

### Q: Calculations are too slow/fast
**A**: Throttling runs every 10 bars by default. Only logs on significant changes.

### Q: How do I trace a specific trade decision?
**A**: Use extract_uid.ps1 with the UID from the CAPTURE log line.

### Q: Strategy worked yesterday but not today
**A**: Check for instrument symbol changes, API updates, or account connection issues.

### Q: Manual mode still shows auto calculations
**A**: PASO 3 calculates diagnostics in all modes. Integration happens in PASO 4.

### Q: Getting "Could not attach indicator" warnings
**A**: Redeploy using deploy_risk_management.ps1 and restart ATAS completely.

### Q: CSV overrides not working
**A**: Verify exact symbol name matching and CSV format: `"SYM1=val1;SYM2=val2"`

### Q: Position sizing seems wrong for my risk tolerance
**A**: Verify tick values are correct for your instrument and check SL distance calculation.

## Debug Commands Reference

### Quick Health Check
```bash
# Last 10 calculation results
grep -n "CALC OUT.*qtyFinal=" logs/current/ATAS_SESSION_LOG.txt | tail -10

# Recent errors
grep -n "ERROR\|WARNING.*CALC" logs/current/ATAS_SESSION_LOG.txt | tail -5

# Current configuration
grep -n "RISK SNAPSHOT" logs/current/ATAS_SESSION_LOG.txt | tail -1
```

### Detailed Analysis
```bash
# Complete UID timeline
powershell -ExecutionPolicy Bypass -File "tools\extract_uid.ps1" -Path "logs\current\ATAS_SESSION_LOG.txt" -Uid "TARGET_UID" -ShowTimeline

# Filtered tag view
powershell -ExecutionPolicy Bypass -File "tools\filter_risk.ps1" -Path "logs\current\ATAS_SESSION_LOG.txt" -Tags @('468/CALC')

# Real-time monitoring
powershell -ExecutionPolicy Bypass -File "tools\tail_risk.ps1" -Path "logs\current\ATAS_SESSION_LOG.txt" -TagFilter "CALC"
```