# 🛡️ Risk Management System - Comprehensive Index

## 📋 Overview

This is the complete Risk Management system for the ATAS FourSixEight Confluences Strategy. The system implements automated position sizing, underfunded protection, and comprehensive diagnostics through a 6-step incremental development approach.

## 🎯 Current Status

### ✅ COMPLETED
- **PASO 1**: Foundation - Enums, properties, and UI structure
- **PASO 2**: Auto-Detection - Tick values and account equity detection
- **PASO 3**: Calculation Engine - 3-mode position sizing with logging

### 🚧 PENDING
- **PASO 4**: Integration - Connect calculations to actual trading
- **PASO 5**: Breakeven System - TP1 trigger implementation
- **PASO 6**: Advanced Features - Diagnostics and refinements

## 📚 Documentation Structure

### Core Documentation (`docs/RiskManagement/`)

#### 01_LOG_CONVENTIONS.md
- **Purpose**: Log format standards and tag definitions
- **Contents**:
  - Official tags (`468/RISK`, `468/CALC`, `468/SL`, etc.)
  - Message format rules (key=value pairs, UID mandatory)
  - Event type specifications
  - Best practices for development and operations

#### 02_PLACEHOLDERS.md
- **Purpose**: Complete placeholder dictionary for structured logging
- **Contents**:
  - 80+ placeholder definitions with examples
  - Currency and value formatting rules
  - Risk parameter placeholders
  - Context and state indicators
  - Validation rules and constraints

#### 03_CALC_ENGINE.md
- **Purpose**: PASO 3 calculation engine detailed specification
- **Contents**:
  - 3 position sizing modes (Manual/FixedRiskUSD/PercentOfAccount)
  - Input flow and auto-detection chain
  - Currency assumptions and limitations
  - Underfunded protection logic
  - Decision matrix with examples
  - Expected log patterns

#### 04_RUNBOOK_OPERATIONS.md
- **Purpose**: Daily operations and monitoring procedures
- **Contents**:
  - Pre-market checklist
  - Session monitoring workflows
  - Deployment procedures
  - Performance monitoring
  - Emergency procedures
  - Operational alerts and best practices

#### 05_TROUBLESHOOTING.md
- **Purpose**: Comprehensive troubleshooting guide
- **Contents**:
  - Common issues and solutions
  - Diagnostic procedures
  - Error categories (Critical/Warning/Info)
  - Recovery procedures
  - Known issues and workarounds
  - FAQ with debug commands

### Command Reference (`docs/grep-cheatsheet/`)

#### risk-grep.md
- **Purpose**: Complete grep command cheatsheet for log analysis
- **Contents**:
  - Tag-based filtering patterns
  - Error and warning detection
  - Signal and entry analysis
  - Advanced analysis techniques
  - Time-based filtering
  - PowerShell tool integration

## 🔧 Tools and Scripts (`tools/`)

### Deployment and Build
- **deploy_risk_management.ps1**: Complete build/deploy/monitor solution
- **deploy_all.ps1**: Legacy deployment script (still functional)

### Log Analysis
- **tail_risk.ps1**: Real-time log monitoring with color coding
- **filter_risk.ps1**: Create filtered log views by tags
- **extract_uid.ps1**: Extract complete operation timelines by UID

### Usage Examples
```bash
# Deploy and monitor
powershell -ExecutionPolicy Bypass -File "tools\deploy_risk_management.ps1" -Monitor

# Real-time monitoring
powershell -ExecutionPolicy Bypass -File "tools\tail_risk.ps1" -Path "logs\current\ATAS_SESSION_LOG.txt"

# Trace specific operation
powershell -ExecutionPolicy Bypass -File "tools\extract_uid.ps1" -Path "logs\current\ATAS_SESSION_LOG.txt" -Uid "abc123" -ShowTimeline
```

## 📊 Log Analysis Quick Reference

### Critical Monitoring Points
```bash
# Health check
grep -n "468/CALC OUT.*qtyFinal=" logs/current/ATAS_SESSION_LOG.txt | tail -5

# Error detection
grep -nE "(ERROR|WARNING.*CALC)" logs/current/ATAS_SESSION_LOG.txt | tail -10

# Underfunded protection
grep -n "UNDERFUNDED" logs/current/ATAS_SESSION_LOG.txt

# Configuration status
grep -n "RISK SNAPSHOT" logs/current/ATAS_SESSION_LOG.txt | tail -1
```

### Common Analysis Workflows
1. **Entry Analysis**: Extract UID → Full timeline → Confluence validation
2. **Risk Audit**: Filter CALC events → Verify calculations → Check underfunded
3. **Performance**: Monitor PING frequency → Check throttling → Verify API health
4. **Debugging**: Error grep → Context extraction → Recovery procedure

## ⚙️ Configuration Reference

### Position Sizing Modes

#### Manual Mode
- **Input**: Fixed `Quantity` property
- **Output**: Same quantity, risk diagnostics calculated
- **Use Case**: Traditional fixed-quantity trading

#### FixedRiskUSD Mode
- **Input**: `RiskPerTradeUsd` (e.g., $50)
- **Calculation**: `qty = floor(risk_target / risk_per_contract)`
- **Use Case**: Consistent dollar risk per trade

#### PercentOfAccount Mode
- **Input**: `RiskPercentOfAccount` (e.g., 0.5%)
- **Calculation**: `target_risk = equity * percent / 100`
- **Use Case**: Dynamic sizing based on account growth

### Auto-Detection System

#### Tick Value Priority
1. `Security.TickCost` (ATAS standard)
2. CSV overrides (`TickValueCsvOverrides`)
3. Fallback value (0.5 + warning)

#### Account Equity Priority
1. Manual override (`AccountEquityOverride`)
2. `Portfolio.BalanceAvailable`
3. `Portfolio.Balance` (fallback)
4. Default value (10000 + warning)

### Underfunded Protection
- **Trigger**: `risk_per_contract > target_risk`
- **Default**: Skip entry (`SkipIfUnderfunded = true`)
- **Alternative**: Force minimum quantity (`MinQtyIfUnderfunded`)

## 🎛️ Key Properties Reference

### Risk Management Properties
```csharp
// Mode Selection
PositionSizingMode = Manual|FixedRiskUSD|PercentOfAccount

// Risk Parameters
RiskPerTradeUsd = 50.0m           // Fixed USD risk
RiskPercentOfAccount = 0.5m       // Percent of account
Quantity = 2                      // Manual quantity

// Auto-Detection Overrides
TickValueCsvOverrides = "MNQ=0.5;NQ=5;ES=12.5"
AccountEquityOverride = 25000m

// Underfunded Protection
SkipIfUnderfunded = true
MinQtyIfUnderfunded = 1

// Diagnostics (Read-Only)
EffectiveTickValue              // Current tick value used
EffectiveAccountEquity          // Current equity used
LastAutoQty                     // Last calculated quantity
LastRiskPerContract             // Last risk/contract in USD
LastStopDistanceTicks           // Last SL distance in ticks
```

## 🚨 Emergency Procedures

### Strategy Freeze (Critical)
1. Set `PositionSizingMode = Manual`, `Quantity = 0`
2. Monitor position closure: `grep -n "net=0" logs/current/ATAS_SESSION_LOG.txt`
3. Investigate via UID timeline extraction

### Calculation Failure (Warning)
1. Force refresh: Set `RefreshDiagnostics = true`
2. Monitor: `grep -n "DIAG.*manual-refresh" logs/current/ATAS_SESSION_LOG.txt`
3. Check for resolution: `grep -n "CALC IN.*mode=" logs/current/ATAS_SESSION_LOG.txt | tail -1`

### API Disconnection (Info)
1. Switch instruments and back (force reconnection)
2. Monitor: `grep -n "auto-detected.*via" logs/current/ATAS_SESSION_LOG.txt`
3. Verify: `grep -n "RISK SNAPSHOT" logs/current/ATAS_SESSION_LOG.txt | tail -1`

## 📈 Expected Evolution (PASO 4-6)

### PASO 4: Integration
- Replace manual quantities with calculated quantities
- Real-time quantity updates for live orders
- Integration testing with paper trading

### PASO 5: Breakeven System
- Monitor TP1 fills via order tracking
- Automatic SL adjustment to breakeven + buffer
- Comprehensive breakeven logging

### PASO 6: Advanced Features
- Enhanced diagnostics and UI feedback
- Performance optimizations
- Advanced lot sizing with fractional contracts
- Multi-currency support enhancements

## 🔍 Validation Checklist

### Deployment Validation
- [ ] Compilation successful without warnings
- [ ] DLL deployment to ATAS directory confirmed
- [ ] Strategy loads without "Could not attach indicator" warnings
- [ ] Initial RISK SNAPSHOT logged with correct configuration

### Calculation Validation
- [ ] All 3 modes produce reasonable quantities
- [ ] Underfunded protection triggers correctly
- [ ] Auto-detection finds correct tick values and equity
- [ ] Log patterns match specification

### Performance Validation
- [ ] Calculations complete within milliseconds
- [ ] Throttling limits log volume appropriately
- [ ] No memory leaks during extended sessions
- [ ] API calls remain responsive

## 📞 Support Resources

### Log Analysis Tools
- Use PowerShell tools for advanced analysis
- Refer to grep-cheatsheet for quick commands
- Follow troubleshooting guide for systematic diagnosis

### Development Resources
- All documentation in `docs/RiskManagement/`
- Code structure follows ATAS ChartStrategy patterns
- Incremental development maintains backward compatibility

### Community and Documentation
- ATAS API Documentation: https://docs.atas.net/
- Project follows defensive coding principles
- Comprehensive logging enables community support

---

**Last Updated**: 2025-09-17
**Version**: PASO 3 Completed
**Next Milestone**: PASO 4 Integration