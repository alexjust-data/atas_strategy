# ATAS 468 Strategy - Professional Trading System

## Overview
This is a professional quantitative trading strategy for the ATAS platform implementing the "468 strategy" with GenialLine crosses and confluence filters.

## Key Features
- **N+1 Execution Logic**: Professional timing with exact N+1 bar execution
- **Triple Guard System**: Robust position management preventing multiple entries
- **Confluence Filters**: GenialLine slope and EMA vs Wilder8 confirmations
- **Risk Management**: Proper SL/TP calculation with R-multiple targets
- **Professional Logging**: Comprehensive debug and emergency logging

## Project Structure
```
src/
├── MyAtas.Shared/          # Common utilities and logging
├── MyAtas.Indicators/      # FourSixEightIndicator (468 indicator)
└── MyAtas.Strategies/      # Trading strategies
scripts/                    # Deployment scripts
EMERGENCY_ATAS_LOG.txt      # Real-time strategy logs
```

## Strategy Logic
1. **Signal Capture (N)**: Detect GenialLine crosses with close confirmation
2. **N+1 Execution**: Execute exactly at N+1 with strict timing and price tolerance
3. **Confluence Validation**: Check GenialLine slope and EMA/Wilder8 relationship
4. **Risk Management**: Calculate SL from signal candle, TPs based on R-multiples

## Configuration
Key parameters available in ATAS panel:
- `StrictN1Open`: Require first tick execution (default: true)
- `OpenToleranceTicks`: Price tolerance for late execution (default: 2)
- `RequireGenialSlope`: Require slope confirmation (default: true)
- `RequireEmaVsWilder`: Require EMA vs Wilder8 confluence (default: true)

## Deployment
Run `scripts/deploy_all.ps1` to build and deploy all components to ATAS.

## Recent Improvements
- Fixed N+1 execution timing with exact windowing
- Added price tolerance for real-world latency handling
- Implemented signal expiration to prevent stale executions
- Enhanced logging for complete execution traceability
- Professional risk management with coherent R calculations

## Version History
- v2.0: Professional N+1 timing with tolerance and expiration
- v1.0: Initial 468 strategy implementation

## Author
Alex Just Rodriguez.
Developed for professional quantitative trading with ATAS platform.
