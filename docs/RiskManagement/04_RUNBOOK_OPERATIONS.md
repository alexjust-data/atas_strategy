# 04. Risk Management Operations Runbook

## Table of Contents
- [Daily Operations](#daily-operations)
- [Session Monitoring](#session-monitoring)
- [Deployment Procedures](#deployment-procedures)
- [Log Analysis Workflows](#log-analysis-workflows)
- [Emergency Procedures](#emergency-procedures)
- [Performance Monitoring](#performance-monitoring)

## Daily Operations

### Pre-Market Checklist
```bash
# 1. Deploy latest version
cd "C:\Users\AlexJ\Desktop\projects\01_atas\06_ATAS_strategy - v2"
powershell -ExecutionPolicy Bypass -File "tools\deploy_risk_management.ps1"

# 2. Verify deployment
tail -n 20 logs/current/ATAS_SESSION_LOG.txt

# 3. Check for initialization errors
grep -n "WARNING: Could not attach indicator\|INIT OK" logs/current/ATAS_SESSION_LOG.txt
```

### Session Start Validation
```bash
# Expected initialization sequence
grep -A 5 "RISK SNAPSHOT" logs/current/ATAS_SESSION_LOG.txt | head -20

# Verify auto-detection
grep -n "auto-detected\|override\|fallback" logs/current/ATAS_SESSION_LOG.txt

# Check instrument configuration
grep -n "DIAG.*sym=" logs/current/ATAS_SESSION_LOG.txt
```

## Session Monitoring

### Real-Time Monitoring
```bash
# Start comprehensive real-time monitoring
powershell -ExecutionPolicy Bypass -File "tools\tail_risk.ps1" -Path "logs\current\ATAS_SESSION_LOG.txt"

# Monitor specific UID timeline
powershell -ExecutionPolicy Bypass -File "tools\extract_uid.ps1" -Path "logs\current\ATAS_SESSION_LOG.txt" -Uid "abc123" -ShowTimeline
```

### Key Metrics Dashboard
Monitor these critical indicators throughout the session:

#### Position Sizing Health
```bash
# Check calculation consistency
grep -n "CALC.*qtyFinal=" logs/current/ATAS_SESSION_LOG.txt | tail -10

# Monitor underfunded protection
grep -n "UNDERFUNDED" logs/current/ATAS_SESSION_LOG.txt
```

#### Risk Parameters Stability
```bash
# Track parameter changes
grep -n "RISK SNAPSHOT\|RISK DIAG" logs/current/ATAS_SESSION_LOG.txt | tail -5

# Check for fallback usage
grep -n "RISK_FALLBACK" logs/current/ATAS_SESSION_LOG.txt
```

#### Entry Decision Flow
```bash
# Monitor confluence validation
grep -n "SIGNAL_CHECK\|CONFLUENCE\|ABORT ENTRY" logs/current/ATAS_SESSION_LOG.txt | tail -20

# Track execution pipeline
grep -n "CAPTURE.*uid=\|STR CONTEXT\|CALC OUT" logs/current/ATAS_SESSION_LOG.txt | tail -15
```

## Deployment Procedures

### Standard Deployment
```bash
# Full deployment with monitoring
cd "C:\Users\AlexJ\Desktop\projects\01_atas\06_ATAS_strategy - v2"
powershell -ExecutionPolicy Bypass -File "tools\deploy_risk_management.ps1" -Monitor

# Expected output sequence:
# 1. "Building Indicators..."
# 2. "Building Strategies..."
# 3. "Deploying to ATAS..."
# 4. "All deployments completed successfully!"
```

### Emergency Rollback
```bash
# If new deployment fails, restore from backup
cp "C:\Users\AlexJ\AppData\Roaming\ATAS\Strategies\*.dll.backup" "C:\Users\AlexJ\AppData\Roaming\ATAS\Strategies\"

# Restart ATAS and verify previous version loads
grep -n "VERSION\|INIT" logs/current/ATAS_SESSION_LOG.txt | tail -5
```

### Hot-Fix Deployment
```bash
# For critical fixes during trading hours
# 1. Test in separate ATAS instance first
# 2. Deploy during low-volume periods
# 3. Monitor for 5 minutes before trusting
powershell -ExecutionPolicy Bypass -File "tools\deploy_risk_management.ps1" -QuickDeploy
```

## Log Analysis Workflows

### Signal Analysis Workflow
```bash
# 1. Extract signal events
grep -n "CAPTURE.*uid=" logs/current/ATAS_SESSION_LOG.txt > signal_events.log

# 2. For each UID found, extract complete timeline
# powershell extract_uid.ps1 -Path logs/current/ATAS_SESSION_LOG.txt -Uid "abc123"

# 3. Analyze confluence decisions
grep -nE "Conf#[12].*PASS|FAIL" uid_abc123_*.log
```

### Risk Calculation Audit
```bash
# 1. Create filtered calculation view
powershell -ExecutionPolicy Bypass -File "tools\filter_risk.ps1" -Path "logs\current\ATAS_SESSION_LOG.txt" -Tags @('468/CALC')

# 2. Verify calculation consistency
grep -n "CALC IN.*mode=.*slTicks=.*tickCost=" *_468_CALC_*.log

# 3. Check for underfunded events
grep -n "UNDERFUNDED" *_468_CALC_*.log

# 4. Validate quantity progression: Raw → Snap → Clamp → Final
grep -n "qtyRaw=.*qtySnap=.*qtyClamp=.*qtyFinal=" *_468_CALC_*.log
```

### Performance Analysis
```bash
# 1. Calculate operation timings
grep -n "CALC IN\|CALC OUT" logs/current/ATAS_SESSION_LOG.txt |
    awk -F'[\\[\\]]' '{print $2, $3}' |
    # Manual timing analysis

# 2. Monitor throttling effectiveness
grep -n "CALC PING" logs/current/ATAS_SESSION_LOG.txt | wc -l

# 3. Check memory usage patterns
grep -n "GC\|Memory" logs/current/ATAS_SESSION_LOG.txt
```

## Emergency Procedures

### Strategy Freeze Protocol
If strategy starts making erratic decisions:

```bash
# 1. IMMEDIATE: Set PositionSizingMode = Manual, Quantity = 0
# 2. Monitor for position closure
grep -n "filled.*qty=0\|position.*net=0" logs/current/ATAS_SESSION_LOG.txt

# 3. Extract problem UID for analysis
# Last 30 minutes of activity
grep -n "$(date -d '30 minutes ago' '+%H:%M')" logs/current/ATAS_SESSION_LOG.txt
```

### Underfunded Protection Triggered
```bash
# 1. Identify trigger event
grep -n "ABORT ENTRY.*Underfunded" logs/current/ATAS_SESSION_LOG.txt | tail -1

# 2. Extract calculation inputs
grep -B 5 -A 5 "UNDERFUNDED" logs/current/ATAS_SESSION_LOG.txt

# 3. Verify account equity detection
grep -n "equity.*auto\|override" logs/current/ATAS_SESSION_LOG.txt | tail -3

# 4. Adjust RiskPerTradeUsd or verify tick values
grep -n "tickCost.*override\|fallback" logs/current/ATAS_SESSION_LOG.txt
```

### Calculation Engine Malfunction
```bash
# 1. Check for calculation errors
grep -nE "ERROR.*CALC|NaN|Infinity|overflow" logs/current/ATAS_SESSION_LOG.txt

# 2. Verify input sanity
grep -n "slTicks=0\|tickCost=0\|equity=0" logs/current/ATAS_SESSION_LOG.txt

# 3. Force diagnostic refresh
# Set RefreshDiagnostics = true in ATAS UI

# 4. Monitor next calculation cycle
grep -n "DIAG.*manual-refresh" logs/current/ATAS_SESSION_LOG.txt
```

## Performance Monitoring

### Calculation Performance
```bash
# Monitor calculation frequency (should be ~every 10 bars)
grep -n "CALC PING.*bar=" logs/current/ATAS_SESSION_LOG.txt |
    tail -10 |
    awk -F'bar=' '{print $2}' |
    awk '{print $1-prev; prev=$1}'
```

### Memory and CPU Impact
```bash
# Check for memory leaks
grep -nE "OutOfMemory|StackOverflow|Heap" logs/current/ATAS_SESSION_LOG.txt

# Monitor calculation timing
grep -n "CALC.*took.*ms" logs/current/ATAS_SESSION_LOG.txt

# Verify throttling is working
grep -n "throttled\|skipped" logs/current/ATAS_SESSION_LOG.txt
```

### API Response Health
```bash
# Check Portfolio API responses
grep -n "Portfolio.*null\|BalanceAvailable.*null" logs/current/ATAS_SESSION_LOG.txt

# Monitor Security API stability
grep -n "Security.*null\|TickCost.*null" logs/current/ATAS_SESSION_LOG.txt

# Track fallback usage frequency
grep -n "RISK_FALLBACK" logs/current/ATAS_SESSION_LOG.txt | wc -l
```

## Operational Alerts

### Critical Alerts (Immediate Action Required)
- `ABORT ENTRY: Underfunded` - Review risk parameters
- `WARNING: Could not attach indicator` - Deployment failure
- `ERROR.*CALC` - Calculation engine failure
- Multiple `RISK_FALLBACK` - Configuration issue

### Warning Alerts (Monitor Closely)
- `override.*hit=false` - CSV configuration mismatch
- `MISMATCH.*equity` - Manual/auto equity disagreement
- Frequent `PING` without `IN`/`OUT` - Throttling misconfiguration

### Info Alerts (Track Trends)
- `auto-detected` frequency - API reliability
- `qtyFinal=0` frequency - Market conditions vs settings
- `Conf#1 failed` vs `Conf#2 failed` - Market behavior patterns

## Best Practices

### Log Management
1. **Rotate logs daily** - Archive previous day before market open
2. **Filter proactively** - Use tag-specific views during troubleshooting
3. **Backup critical UIDs** - Save timelines for significant events
4. **Monitor disk space** - Risk logs can grow large during active sessions

### Parameter Tuning
1. **Change one parameter at a time** - Isolate impact
2. **Test in paper trading first** - Validate calculations
3. **Monitor for 1 hour minimum** - Allow full cycle observation
4. **Document parameter changes** - Maintain audit trail

### Troubleshooting Approach
1. **Start with SNAPSHOT** - Understand initial state
2. **Follow UID timeline** - Complete operation context
3. **Check input sources** - Verify auto-detection
4. **Validate calculations** - Step through math manually
5. **Test edge cases** - Reproduce issue conditions