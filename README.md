# ATAS 468 Strategy - Professional Trading System

## Overview
This is a professional quantitative trading strategy for the ATAS platform implementing the "468 strategy" with GenialLine crosses and confluence filters.

## Key Features
- **N+1 Execution Logic**: Professional timing with exact N+1 bar execution
- **Advanced Risk Management**: Dynamic position sizing with auto-detection and comprehensive diagnostics
- **Triple Guard System**: Robust position management preventing multiple entries
- **Confluence Filters**: GenialLine slope and EMA vs Wilder8 confirmations
- **Intelligent Position Sizing**: Manual, Fixed USD Risk, and % of Account modes
- **Real-time Diagnostics**: Live tick value and equity detection with UI transparency
- **Professional Logging**: Comprehensive debug and emergency logging with dual system

## Project Structure
```
src/
├── MyAtas.Shared/          # Common utilities and dual logging system
├── MyAtas.Indicators/      # FourSixEightIndicator (468 indicator)
└── MyAtas.Strategies/      # FourSixEightConfluencesStrategy_Simple
docs/                       # Strategy documentation and analysis
test_scenarios/             # Comprehensive testing framework (A-F scenarios)
tools/                      # Analysis scripts and deployment tools
logs/
├── current/                # Session-specific logs (ATAS_SESSION_LOG.txt)
└── emergency/              # Persistent emergency logs across sessions
```

## Strategy Logic
1. **Signal Capture (N)**: Detect GenialLine crosses with close confirmation
2. **N+1 Execution**: Execute exactly at N+1 with strict timing and price tolerance
3. **Confluence Validation**: Check GenialLine slope and EMA/Wilder8 relationship
4. **Risk Management**: Calculate SL from signal candle, TPs based on R-multiples

## Configuration

### Core Strategy Parameters
- `StrictN1Open`: Require first tick execution (default: true)
- `OpenToleranceTicks`: Price tolerance for late execution (default: 2)
- `RequireGenialSlope`: Require slope confirmation (default: true)
- `RequireEmaVsWilder`: Require EMA vs Wilder8 confluence (default: true)

### Risk Management & Position Sizing
- **Position Sizing Mode**: Manual / Fixed Risk USD / % of Account
- **Risk per Trade (USD)**: Fixed dollar risk amount (default: 50)
- **Risk % of Account**: Percentage of account equity to risk (default: 0.5%)
- **Tick Value Overrides**: CSV format for custom instruments (preset: `MNQ=0.5;NQ=5;MES=1.25;ES=12.5;MGC=1;GC=10`)
- **Skip if Underfunded**: Abort trades when risk/contract exceeds target (default: true)
- **Account Equity Override**: Manual equity setting when auto-detection fails

### Real-time Diagnostics (Read-only UI)
- **Effective Tick Value**: Currently used $/tick value (override/auto/fallback)
- **Effective Account Equity**: Current equity for % calculations
- **Last Auto Qty**: Last calculated position size
- **Last Risk/Contract**: Risk per contract in USD
- **Last Stop Distance**: SL distance in ticks
- **Refresh Diagnostics**: Button to log current values instantly

## Deployment
Run `tools/deploy_all.ps1` to build and deploy all components to ATAS.

## Testing Framework
Complete scenario testing system with forensic analysis:
- **Scenario A (Baseline)**: Both confluences + guard validation
- **Scenario B (CONF#1)**: GenialLine slope isolation testing
- **Scenario C (CONF#2)**: EMA vs Wilder8 window mode testing
- **Scenario D (CONF#2 Strict)**: EMA vs Wilder8 strict mode testing
- **Scenario E (N+1 Timing)**: Strict timing and expiration testing
- **Scenario F (Guard Test)**: OnlyOnePosition guard behavior validation

Use `tools/analizar_escenario.bat [A|B|C|D|E|F]` for detailed analysis.

## Recent Improvements

### Version 2.2 - Advanced Risk Management System
- **Dynamic Position Sizing**: 3 modes (Manual/Fixed USD/% Account) with real-time calculation
- **Auto-detection System**: Automatic tick value and account equity detection via ATAS API
- **Override System**: CSV-based tick value overrides with priority handling (override→auto→fallback)
- **Underfunded Protection**: Intelligent abort when risk exceeds targets (prevents forced entries)
- **Real-time Diagnostics**: Live UI display of effective values and calculation history
- **Enhanced Parser**: Accepts both `SYM=VAL` and `SYM,VAL` formats with InvariantCulture
- **MinStepPrice Detection**: Priority on ATAS standard $/tick property for robust auto-detection
- **Comprehensive Logging**: All risk calculations logged with `DIAG [init/manual-refresh]` echo

### Version 2.1 - Professional Trading Framework
- **Complete Testing Framework**: 6 comprehensive scenarios with forensic analysis
- **Trade Lock System**: 4-patch solution preventing false negatives (100% execution rate)
- **OnlyOnePosition Guard**: Robust overtrading prevention with cooldown system
- **Dual Logging System**: Session-specific and persistent emergency logging
- **Project Reorganization**: Clean structure with dedicated logs/ and tools/ directories
- **Confluence Validation**: Strict/Inclusive/Window modes for EMA vs Wilder8
- **Professional Timing**: N+1 execution with tolerance and expiration handling

## Test Results Summary
All scenarios validate 100% confluence accuracy:
- **Strategy executes 100% of valid signals** (post-patches)
- **OnlyOnePosition guard prevents overtrading** (working as designed)
- **Confluence filters working perfectly** (Strict/Window/Inclusive modes)
- **N+1 timing system robust** (no expiration issues)

## Risk Management Quick Start

### For Common Instruments (Auto-configured)
1. **Set Position Sizing Mode**: Choose between Manual, Fixed USD Risk, or % of Account
2. **Set Risk Amount**: Configure either "Risk per trade (USD)" or "Risk % of account"
3. **Verify Diagnostics**: Check "Effective tick value" and "Effective account equity" in UI
4. **Use Refresh Button**: Click "Refresh diagnostics" to log current values

### Supported Instruments (Pre-configured)
- **MNQ** (Micro NASDAQ): $0.50/tick
- **NQ** (E-mini NASDAQ): $5.00/tick
- **MES** (Micro S&P): $1.25/tick
- **ES** (E-mini S&P): $12.50/tick
- **MGC** (Micro Gold): $1.00/tick
- **GC** (Gold): $10.00/tick

### Log Analysis
```bash
# View all risk diagnostics
grep -n "DIAG \[" ATAS_SESSION_LOG.txt

# View tick value detection
grep -nE "TICK-VALUE|override|auto-detected" ATAS_SESSION_LOG.txt

# View auto-qty calculations
grep -n "AUTOQTY\|ABORT ENTRY" ATAS_SESSION_LOG.txt
```

## Version History
- **v2.2**: Advanced risk management system with dynamic position sizing and real-time diagnostics
- **v2.1**: Complete testing framework + trade lock fixes + project reorganization
- **v2.0**: Professional N+1 timing with tolerance and expiration
- **v1.0**: Initial 468 strategy implementation

## Author
Alex Just Rodriguez.
Developed for professional quantitative trading with ATAS platform.



----



