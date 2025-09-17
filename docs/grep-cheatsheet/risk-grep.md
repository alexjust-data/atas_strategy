# Risk Management Grep Commands Cheatsheet

## Quick Reference

### Basic Commands
```bash
# View last 20 lines of current session
tail -n 20 logs/current/ATAS_SESSION_LOG.txt

# Search for specific UID timeline
grep -n "uid=abc123" logs/current/ATAS_SESSION_LOG.txt

# Monitor real-time (use tail -f in bash/WSL)
tail -f logs/current/ATAS_SESSION_LOG.txt | grep "468/"
```

## Tag-Based Filtering

### Risk Management Tags
```bash
# All risk management events
grep -n "468/" logs/current/ATAS_SESSION_LOG.txt

# Risk detection and overrides
grep -n "468/RISK" logs/current/ATAS_SESSION_LOG.txt

# Stop loss calculations
grep -n "468/SL" logs/current/ATAS_SESSION_LOG.txt

# Position sizing calculations
grep -n "468/CALC" logs/current/ATAS_SESSION_LOG.txt

# Strategy context and guards
grep -n "468/STR" logs/current/ATAS_SESSION_LOG.txt

# Order management
grep -n "468/ORD" logs/current/ATAS_SESSION_LOG.txt

# Position tracking
grep -n "468/POS" logs/current/ATAS_SESSION_LOG.txt
```

### Calculation Subtypes
```bash
# All calculation events
grep -n "468/CALC" logs/current/ATAS_SESSION_LOG.txt

# Calculation inputs only
grep -n "468/CALC IN" logs/current/ATAS_SESSION_LOG.txt

# Final calculation results
grep -n "468/CALC OUT" logs/current/ATAS_SESSION_LOG.txt

# Manual mode calculations
grep -n "468/CALC MANUAL" logs/current/ATAS_SESSION_LOG.txt

# Fixed USD mode calculations
grep -n "468/CALC FIXED" logs/current/ATAS_SESSION_LOG.txt

# Percent of account calculations
grep -n "468/CALC PCT" logs/current/ATAS_SESSION_LOG.txt

# Underfunded protection events
grep -n "468/CALC UNDERFUNDED" logs/current/ATAS_SESSION_LOG.txt

# Testing/monitoring pings
grep -n "468/CALC PING" logs/current/ATAS_SESSION_LOG.txt
```

## Error and Warning Patterns

### Critical Issues
```bash
# All errors and warnings
grep -nE "(ERROR|WARNING)" logs/current/ATAS_SESSION_LOG.txt

# Calculation errors
grep -n "ERROR.*CALC" logs/current/ATAS_SESSION_LOG.txt

# API failures
grep -nE "(Portfolio.*null|Security.*null)" logs/current/ATAS_SESSION_LOG.txt

# Initialization failures
grep -n "WARNING: Could not attach indicator" logs/current/ATAS_SESSION_LOG.txt

# Fatal calculation failures
grep -n "FATAL.*CALC" logs/current/ATAS_SESSION_LOG.txt
```

### Fallback Usage
```bash
# All fallback events
grep -n "fallback\|FALLBACK" logs/current/ATAS_SESSION_LOG.txt

# Tick value fallbacks
grep -n "RISK_FALLBACK.*tickCost" logs/current/ATAS_SESSION_LOG.txt

# Account equity fallbacks
grep -n "fallback.*equity" logs/current/ATAS_SESSION_LOG.txt
```

### Underfunded Protection
```bash
# All underfunded events
grep -n "UNDERFUNDED" logs/current/ATAS_SESSION_LOG.txt

# Aborted entries
grep -n "UNDERFUNDED.*action=ABORT" logs/current/ATAS_SESSION_LOG.txt

# Forced minimum quantities
grep -n "UNDERFUNDED.*action=MIN_QTY" logs/current/ATAS_SESSION_LOG.txt

# Risk/contract exceeds target
grep -n "rpc=.*target=" logs/current/ATAS_SESSION_LOG.txt
```

## Signal and Entry Analysis

### Signal Detection
```bash
# All signal captures
grep -n "CAPTURE.*uid=" logs/current/ATAS_SESSION_LOG.txt

# Signal confirmations
grep -n "SIGNAL_CHECK" logs/current/ATAS_SESSION_LOG.txt

# Entry aborts with reasons
grep -n "ABORT ENTRY" logs/current/ATAS_SESSION_LOG.txt

# Confluence validation
grep -nE "Conf#[12].*(PASS|FAIL)" logs/current/ATAS_SESSION_LOG.txt
```

### Entry Pipeline
```bash
# Complete entry flow (capture → context → execution)
grep -nE "(CAPTURE.*uid=|STR CONTEXT|CALC OUT)" logs/current/ATAS_SESSION_LOG.txt

# Guard status checks
grep -nE "(OnlyOne.*PASS|Cooldown.*OFF|guards.*PASS)" logs/current/ATAS_SESSION_LOG.txt

# Market order submissions
grep -n "SubmitMarket.*dir.*qty" logs/current/ATAS_SESSION_LOG.txt
```

## Configuration and Diagnostics

### System Configuration
```bash
# Initial system snapshots
grep -n "RISK SNAPSHOT" logs/current/ATAS_SESSION_LOG.txt

# Diagnostic updates
grep -n "RISK DIAG" logs/current/ATAS_SESSION_LOG.txt

# CSV override parsing
grep -n "OVERRIDE.*raw=.*hit=" logs/current/ATAS_SESSION_LOG.txt

# Auto-detection results
grep -n "auto-detected.*via" logs/current/ATAS_SESSION_LOG.txt
```

### Instrument Configuration
```bash
# Instrument identification
grep -n "instr=.*code=.*exch=" logs/current/ATAS_SESSION_LOG.txt

# Tick value sources
grep -n "tickCost.*source=" logs/current/ATAS_SESSION_LOG.txt

# Currency information
grep -n "qc=.*ac=" logs/current/ATAS_SESSION_LOG.txt

# Manual overrides applied
grep -n "override.*applied=true" logs/current/ATAS_SESSION_LOG.txt
```

## Advanced Analysis

### UID Timeline Extraction
```bash
# Extract all events for specific UID
grep -n "uid=abc123" logs/current/ATAS_SESSION_LOG.txt

# Timeline with context (before/after lines)
grep -B 2 -A 2 "uid=abc123" logs/current/ATAS_SESSION_LOG.txt

# Multiple UIDs pattern
grep -nE "uid=(abc123|def456|ghi789)" logs/current/ATAS_SESSION_LOG.txt
```

### Calculation Flow Analysis
```bash
# Complete calculation pipeline for UID
grep -n "uid=abc123" logs/current/ATAS_SESSION_LOG.txt | grep -E "(CALC IN|CALC.*qtyFinal=|CALC OUT)"

# Quantity progression (raw → snap → clamp → final)
grep -n "qtyRaw=.*qtySnap=.*qtyClamp=.*qtyFinal=" logs/current/ATAS_SESSION_LOG.txt

# Risk parameter correlation
grep -n "targetRisk=.*rpc=.*underfunded=" logs/current/ATAS_SESSION_LOG.txt
```

### Performance Monitoring
```bash
# Calculation frequency (should be ~every 10 bars)
grep -n "CALC PING.*bar=" logs/current/ATAS_SESSION_LOG.txt | tail -20

# Throttling effectiveness
grep -n "throttled\|skipped" logs/current/ATAS_SESSION_LOG.txt

# Memory/performance issues
grep -nE "(OutOfMemory|StackOverflow|took.*ms)" logs/current/ATAS_SESSION_LOG.txt
```

## Time-Based Filtering

### Session Analysis
```bash
# Last hour of activity
grep -n "$(date -d '1 hour ago' '+%H:%M')" logs/current/ATAS_SESSION_LOG.txt

# Specific time window (example: 13:45-13:50)
grep -n "13:4[5-9]:\|13:50:" logs/current/ATAS_SESSION_LOG.txt

# Last 100 risk management events
grep -n "468/" logs/current/ATAS_SESSION_LOG.txt | tail -100
```

### Today's Session Start
```bash
# Session initialization
grep -n "INIT\|VERSION\|attached" logs/current/ATAS_SESSION_LOG.txt | head -10

# First risk snapshot
grep -n "RISK SNAPSHOT" logs/current/ATAS_SESSION_LOG.txt | head -1

# Early calculation attempts
grep -n "CALC" logs/current/ATAS_SESSION_LOG.txt | head -5
```

## Multi-Pattern Searches

### Combined Conditions
```bash
# Underfunded AND specific mode
grep -n "UNDERFUNDED.*mode=FixedRiskUSD" logs/current/ATAS_SESSION_LOG.txt

# Errors AND specific UID
grep -n "ERROR.*uid=abc123" logs/current/ATAS_SESSION_LOG.txt

# Calculation results with specific quantities
grep -n "CALC OUT.*qtyFinal=[1-9]" logs/current/ATAS_SESSION_LOG.txt

# Overrides that were NOT hit
grep -n "OVERRIDE.*hit=false" logs/current/ATAS_SESSION_LOG.txt
```

### Exclusion Patterns
```bash
# All calc events EXCEPT PING
grep -n "468/CALC" logs/current/ATAS_SESSION_LOG.txt | grep -v "PING"

# All risk events EXCEPT diagnostics
grep -n "468/RISK" logs/current/ATAS_SESSION_LOG.txt | grep -v "DIAG"

# Non-zero quantities only
grep -n "qtyFinal=" logs/current/ATAS_SESSION_LOG.txt | grep -v "qtyFinal=0"
```

## Output Formatting

### Count Operations
```bash
# Count total risk management events
grep -c "468/" logs/current/ATAS_SESSION_LOG.txt

# Count underfunded events
grep -c "UNDERFUNDED" logs/current/ATAS_SESSION_LOG.txt

# Count by tag type
grep -o "468/[A-Z]*" logs/current/ATAS_SESSION_LOG.txt | sort | uniq -c
```

### Extract Values
```bash
# Extract all calculated quantities
grep -o "qtyFinal=[0-9]*" logs/current/ATAS_SESSION_LOG.txt | cut -d'=' -f2

# Extract all tick values
grep -o "tickCost=[0-9.]*" logs/current/ATAS_SESSION_LOG.txt | cut -d'=' -f2

# Extract unique UIDs
grep -o "uid=[a-z0-9]*" logs/current/ATAS_SESSION_LOG.txt | sort | uniq
```

## PowerShell Integration

### Using PowerShell Tools
```bash
# Real-time monitoring with color coding
powershell -ExecutionPolicy Bypass -File "tools\tail_risk.ps1" -Path "logs\current\ATAS_SESSION_LOG.txt"

# Create filtered views
powershell -ExecutionPolicy Bypass -File "tools\filter_risk.ps1" -Path "logs\current\ATAS_SESSION_LOG.txt"

# Extract complete UID timeline
powershell -ExecutionPolicy Bypass -File "tools\extract_uid.ps1" -Path "logs\current\ATAS_SESSION_LOG.txt" -Uid "abc123" -ShowTimeline
```

### Combining grep with PowerShell
```bash
# Pipe grep results to PowerShell for further processing
grep -n "CALC OUT" logs/current/ATAS_SESSION_LOG.txt | powershell -Command "ForEach-Object { $_ -replace 'qtyFinal=(\d+)', 'QUANTITY: $1' }"
```

## Common Workflows

### Debug Failed Entry
```bash
# 1. Find the failed entry
grep -n "ABORT ENTRY" logs/current/ATAS_SESSION_LOG.txt | tail -1

# 2. Extract the UID from that line
# 3. Get complete timeline
grep -n "uid=EXTRACTED_UID" logs/current/ATAS_SESSION_LOG.txt

# 4. Check confluence failures
grep -n "uid=EXTRACTED_UID.*Conf#.*FAIL" logs/current/ATAS_SESSION_LOG.txt
```

### Monitor Current Session Health
```bash
# Recent calculation results
grep -n "CALC OUT" logs/current/ATAS_SESSION_LOG.txt | tail -5

# Any recent errors
grep -nE "(ERROR|WARNING)" logs/current/ATAS_SESSION_LOG.txt | tail -10

# Current configuration
grep -n "RISK SNAPSHOT" logs/current/ATAS_SESSION_LOG.txt | tail -1

# Fallback usage count
grep -c "FALLBACK" logs/current/ATAS_SESSION_LOG.txt
```

### Audit Risk Calculations
```bash
# All calculation modes used today
grep -o "mode=[A-Za-z]*" logs/current/ATAS_SESSION_LOG.txt | sort | uniq -c

# Range of quantities calculated
grep -o "qtyFinal=[0-9]*" logs/current/ATAS_SESSION_LOG.txt | sort -t'=' -k2 -n | uniq

# Instruments processed
grep -o "instr=[A-Z]*" logs/current/ATAS_SESSION_LOG.txt | sort | uniq -c
```