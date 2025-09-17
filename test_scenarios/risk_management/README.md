# 📊 Risk Management Log Formats

## Overview

This directory contains documentation for Risk Management log formats, analysis patterns, and monitoring procedures specific to the FourSixEight Confluences Strategy.

## Log Location

### Primary Log Files
- **Current Session**: `logs/current/ATAS_SESSION_LOG.txt`
- **Emergency Backup**: `logs/current/EMERGENCY_ATAS_LOG.txt`
- **Session ID**: `logs/current/ATAS_SESSION_ID.tmp`

### Generated Analysis Files
- **Filtered Views**: `*_468_CALC_*.log`, `*_468_RISK_*.log`, etc.
- **UID Timelines**: `uid_*_*.log`
- **Combined Views**: `*_combined_calc_*.log`

## Official Risk Management Tags

### Primary Tags
| Tag | Purpose | Frequency | Critical |
|-----|---------|-----------|----------|
| `468/RISK` | Risk detection, overrides, diagnostics | Startup + changes | Yes |
| `468/CALC` | Position sizing calculations | Every 10 bars | Yes |
| `468/SL` | Stop loss calculations | Per signal | Yes |
| `468/STR` | Strategy context and guards | Per entry | Yes |
| `468/ORD` | Order management and brackets | Per order | No |
| `468/POS` | Position tracking and snapshots | Per change | No |

### Calculation Subtypes
| Subtype | Purpose | Example |
|---------|---------|---------|
| `CALC IN` | Calculation inputs | `mode=FixedRiskUSD slTicks=7 tickCost=0.50USD/t` |
| `CALC MANUAL` | Manual mode results | `qty=2 rpc=3.50USD totalRisk=7.00USD` |
| `CALC FIXED` | Fixed USD mode results | `targetRisk=100USD qtyFinal=28` |
| `CALC PCT` | Percent mode results | `equity=25000USD pct=0.50% qtyFinal=35` |
| `CALC UNDERFUNDED` | Protection triggered | `action=ABORT rpc=25.00USD target=10.00USD` |
| `CALC OUT` | Final calculation result | `qtyFinal=28 rpc=3.50USD` |
| `CALC PING` | Throttled monitoring | `bar=17620 lastQty=28` |

## Message Format Standards

### Core Rules
1. **One line = one event** - No multi-line entries
2. **Key=value format** - All parameters as `key=value` pairs
3. **UID mandatory** - Every risk-related event must include `uid={uid}`
4. **Currency suffixes** - All monetary values include currency (`USD`, etc.)
5. **No spaces in values** - Use underscores or concatenation

### Standard Pattern
```
[timestamp] LEVEL tag uid={uid} key1=value1 key2=value2 ... note="optional free text"
```

### Example Messages
```
[13:45:01.125] WARNING 468/CALC IN uid=abc123 mode=FixedRiskUSD slTicks=7 tickCost=0.50USD/t equity=25000USD note="equity in USD, tickCost in USD; no currency conversion at step3"

[13:45:01.126] WARNING 468/CALC FIXED uid=abc123 targetRisk=100USD rpc=3.50USD underfunded=false qtyRaw=28.57 qtySnap=28 qtyClamp=28 qtyFinal=28

[13:45:01.127] WARNING 468/CALC OUT uid=abc123 mode=FixedRiskUSD qtyFinal=28 rpc=3.50USD slTicks=7 note="orders still use manual Quantity=2 until STEP4"
```

## Key Placeholders Reference

### Identifiers
- `{uid}` - Unique operation identifier (GUID format)
- `{ts}` - Local timestamp (HH:mm:ss.fff)
- `{bar}` - Bar index (integer)

### Risk Parameters
- `{mode}` - Position sizing mode (Manual/FixedRiskUSD/PercentOfAccount)
- `{riskUsd}` - Risk amount in USD (decimal)
- `{riskPct}` - Risk percentage (decimal with % suffix)
- `{slTicks}` - SL distance in ticks (decimal)

### Calculation Results
- `{qty}` - Final quantity (integer/decimal)
- `{qtyRaw}` - Raw calculation before rounding (decimal)
- `{qtySnap}` - After lot step snapping (decimal)
- `{qtyClamp}` - After min/max limits (decimal)
- `{rpc}` - Risk per contract (decimal with currency)
- `{target}` - Target risk amount (decimal with currency)

### Instrument Data
- `{tickCost}` - Cost per tick (decimal with currency/t suffix)
- `{tickSize}` - Points per tick (decimal)
- `{equity}` - Account equity (decimal with currency)
- `{instr}` - Instrument name (string)

### Flags and States
- `{uf}` - Underfunded flag (true/false)
- `{ovApplied}` - Override applied (true/false)
- `{hit}` - CSV override hit (true/false)

## Analysis Patterns

### Health Check Commands
```bash
# Recent calculation results
grep -n "468/CALC OUT.*qtyFinal=" logs/current/ATAS_SESSION_LOG.txt | tail -5

# Error detection
grep -nE "(ERROR|WARNING.*CALC)" logs/current/ATAS_SESSION_LOG.txt | tail -10

# Configuration status
grep -n "468/RISK SNAPSHOT" logs/current/ATAS_SESSION_LOG.txt | tail -1

# Underfunded protection events
grep -n "468/CALC UNDERFUNDED" logs/current/ATAS_SESSION_LOG.txt
```

### UID Timeline Analysis
```bash
# Extract complete operation timeline
grep -n "uid=abc123" logs/current/ATAS_SESSION_LOG.txt

# With context lines
grep -B 2 -A 2 "uid=abc123" logs/current/ATAS_SESSION_LOG.txt

# Using PowerShell tool
powershell -ExecutionPolicy Bypass -File "tools\extract_uid.ps1" -Path "logs\current\ATAS_SESSION_LOG.txt" -Uid "abc123" -ShowTimeline
```

### Performance Monitoring
```bash
# Calculation frequency (should be ~every 10 bars)
grep -n "468/CALC PING.*bar=" logs/current/ATAS_SESSION_LOG.txt | tail -20

# API health check
grep -n "auto-detected.*via" logs/current/ATAS_SESSION_LOG.txt | tail -5

# Fallback usage frequency
grep -c "RISK_FALLBACK" logs/current/ATAS_SESSION_LOG.txt
```

## Expected Log Sequences

### Successful Entry Flow
1. **Signal Capture**: `CAPTURE.*uid=` (strategy detects signal)
2. **Risk Snapshot**: `RISK SNAPSHOT` (system state at entry)
3. **Calculation Input**: `CALC IN` (inputs to calculation engine)
4. **Mode Calculation**: `CALC FIXED|PCT|MANUAL` (position sizing)
5. **Calculation Output**: `CALC OUT` (final calculated quantity)
6. **Strategy Context**: `STR CONTEXT` (guards and context)
7. **Order Submission**: Market order execution

### Underfunded Protection Flow
1. **Calculation Input**: `CALC IN` (normal calculation start)
2. **Underfunded Detection**: `CALC UNDERFUNDED action=ABORT` (protection triggered)
3. **Zero Quantity Output**: `CALC OUT.*qtyFinal=0` (entry skipped)
4. **Entry Abort**: No order submission occurs

### Auto-Detection Flow
1. **System Startup**: Initial auto-detection attempts
2. **Tick Value Detection**: `auto-detected.*tickCost.*via Security.TickCost`
3. **Account Equity Detection**: `auto-detected.*equity.*via Portfolio.BalanceAvailable`
4. **Override Application**: `OVERRIDE.*hit=true` (if CSV overrides exist)
5. **Risk Snapshot**: Complete configuration logged

## Error Patterns

### Critical Errors
```
ERROR 468/CALC FATAL uid=abc123 exception=DivideByZeroException
ERROR Portfolio object is null, cannot determine account equity
```

### Warning Patterns
```
WARNING 468/RISK RISK_FALLBACK used=tickCost value=0.5
WARNING 468/CALC UNDERFUNDED action=ABORT qty=0
```

### Common Issues
- **Zero tick values**: `tickCost=0` or `RISK_FALLBACK.*tickCost`
- **Missing equity**: `equity=0` or `Portfolio.*null`
- **Calculation failures**: `qtyFinal=0` with normal inputs
- **API disconnections**: Frequent fallback usage

## Monitoring Thresholds

### Normal Operation
- **Calculation frequency**: Every 10 bars (±2)
- **Fallback usage**: <5% of operations
- **Underfunded events**: <10% of signals
- **API response time**: <100ms

### Warning Conditions
- **High fallback usage**: >20% of operations
- **Frequent underfunded**: >50% of signals
- **Calculation errors**: Any ERROR level events
- **API failures**: >5% null responses

### Critical Conditions
- **No calculations**: No CALC events for >100 bars
- **Fatal errors**: Any FATAL level events
- **Complete API failure**: 100% fallback usage
- **Memory issues**: OutOfMemory exceptions

## File Management

### Log Rotation
- **Daily rotation**: Archive previous day's logs
- **Size limits**: Rotate when files exceed 50MB
- **Retention**: Keep 30 days of archived logs

### Backup Procedures
- **Emergency backup**: Automatic copy to EMERGENCY_ATAS_LOG.txt
- **Pre-deployment**: Save current logs before new deployments
- **Critical events**: Immediate backup after fatal errors

### Cleanup Commands
```bash
# Archive old logs
mkdir logs/archive/$(date +%Y%m%d)
cp logs/current/*.txt logs/archive/$(date +%Y%m%d)/

# Clean filtered views older than 7 days
find logs/current -name "*_468_*_*.log" -mtime +7 -delete

# Compress archived logs
gzip logs/archive/*/*.txt
```

## Integration with Tools

### PowerShell Tools
```bash
# Real-time monitoring
powershell -ExecutionPolicy Bypass -File "tools\tail_risk.ps1" -Path "logs\current\ATAS_SESSION_LOG.txt"

# Create filtered views
powershell -ExecutionPolicy Bypass -File "tools\filter_risk.ps1" -Path "logs\current\ATAS_SESSION_LOG.txt"

# Extract UID timeline
powershell -ExecutionPolicy Bypass -File "tools\extract_uid.ps1" -Path "logs\current\ATAS_SESSION_LOG.txt" -Uid "abc123"
```

### Grep Integration
- Use `docs/grep-cheatsheet/risk-grep.md` for command reference
- Combine grep with PowerShell for advanced filtering
- Pipe results to analysis scripts for automation

## Troubleshooting Guide

### No Risk Events
1. Check strategy loading: `grep -n "INIT.*attached" logs/current/ATAS_SESSION_LOG.txt`
2. Verify deployment: Check DLL timestamps in ATAS directory
3. Check for exceptions: `grep -n "ERROR\|EXCEPTION" logs/current/ATAS_SESSION_LOG.txt`

### Incorrect Calculations
1. Extract calculation inputs: `grep -n "CALC IN" logs/current/ATAS_SESSION_LOG.txt | tail -5`
2. Check auto-detection: `grep -n "auto-detected\|fallback" logs/current/ATAS_SESSION_LOG.txt`
3. Verify configuration: `grep -n "RISK SNAPSHOT" logs/current/ATAS_SESSION_LOG.txt | tail -1`

### Performance Issues
1. Check calculation frequency: `grep -n "CALC PING" logs/current/ATAS_SESSION_LOG.txt | tail -20`
2. Monitor API responses: `grep -n "Portfolio\|Security" logs/current/ATAS_SESSION_LOG.txt`
3. Look for memory issues: `grep -n "OutOfMemory\|StackOverflow" logs/current/ATAS_SESSION_LOG.txt`

---

**Related Documentation**:
- `docs/RiskManagement/01_LOG_CONVENTIONS.md` - Complete logging standards
- `docs/RiskManagement/02_PLACEHOLDERS.md` - Placeholder dictionary
- `docs/RiskManagement/05_TROUBLESHOOTING.md` - Troubleshooting procedures
- `docs/grep-cheatsheet/risk-grep.md` - Grep command reference