# 02. Risk Management Log Placeholders

## Table of Contents
- [Overview](#overview)
- [Identifier Placeholders](#identifier-placeholders)
- [Instrument Placeholders](#instrument-placeholders)
- [Currency and Values](#currency-and-values)
- [Risk Parameters](#risk-parameters)
- [Calculation Results](#calculation-results)
- [Position and Orders](#position-and-orders)
- [Context and State](#context-and-state)
- [Usage Examples](#usage-examples)

## Overview

This document defines all placeholder keys used in Risk Management logs. Each placeholder follows the format `{key}` and represents a specific value type with defined units and format.

## Identifier Placeholders

| Placeholder | Description | Example | Notes |
|-------------|-------------|---------|-------|
| `{uid}` | Unique operation identifier | `abc123` | Persistent across N→N+1, GUID format |
| `{ts}` | Local timestamp | `13:45:01.123` | HH:mm:ss.fff format |
| `{bar}` | Bar index | `17620` | Integer, strategy bar counter |

## Instrument Placeholders

| Placeholder | Description | Example | Notes |
|-------------|-------------|---------|-------|
| `{instr}` | Instrument visible name | `MNQ` | Human-readable instrument name |
| `{code}` | Ticker/code | `MNQ` | Exchange ticker symbol |
| `{exch}` | Exchange | `CME` | Exchange identifier |
| `{qc}` | Quote currency | `USD` | Currency of tick cost |
| `{ac}` | Account currency | `USD` | Currency of account equity |

## Currency and Values

| Placeholder | Description | Example | Notes |
|-------------|-------------|---------|-------|
| `{tickSize}` | Points per tick | `0.25` | Decimal, minimum price increment |
| `{tickCost}` | Cost per tick | `0.50` | Decimal, always with `{qc}` suffix |
| `{equity}` | Account equity | `25000` | Decimal, always with `{ac}` suffix |
| `{equitySrc}` | Equity source | `BalanceAvailable` | Source of equity value |

### Equity Source Values
- `BalanceAvailable` - From Portfolio.BalanceAvailable
- `Balance` - From Portfolio.Balance fallback
- `ManualOverride` - From user configuration

## Risk Parameters

| Placeholder | Description | Example | Notes |
|-------------|-------------|---------|-------|
| `{mode}` | Position sizing mode | `FixedRiskUSD` | Manual/FixedRiskUSD/PercentOfAccount |
| `{riskUsd}` | Risk amount in USD | `100` | Decimal, with `{ac}` suffix |
| `{riskPct}` | Risk percentage | `0.5` | Decimal, always with % suffix |
| `{slTicksBase}` | Base SL offset ticks | `2` | Integer, configured SL offset |
| `{slTicks}` | Final SL distance | `7` | Decimal, calculated SL distance |
| `{buf}` | Buffer ticks | `5` | Integer, signal candle buffer |
| `{lim}` | Limits applied | `true` | Boolean, min/max limits applied |

## Calculation Results

| Placeholder | Description | Example | Notes |
|-------------|-------------|---------|-------|
| `{target}` | Target risk amount | `100` | Decimal, with `{ac}` suffix |
| `{rpc}` | Risk per contract | `3.50` | Decimal, with `{qc}` suffix |
| `{qty}` | Final quantity | `28` | Integer/decimal, contracts |
| `{qtyRaw}` | Raw calculation | `28.57` | Decimal, before rounding |
| `{qtySnap}` | Lot step snapped | `28` | Decimal, after lot step |
| `{qtyClamp}` | Min/max clamped | `28` | Decimal, after limits |
| `{uf}` | Underfunded flag | `false` | Boolean |
| `{minQty}` | Minimum quantity | `1` | Integer, when underfunded |
| `{act}` | Actual risk | `98` | Decimal, with `{qc}` suffix |
| `{manQty}` | Manual quantity | `2` | Integer, user-configured |

## Position and Orders

| Placeholder | Description | Example | Notes |
|-------------|-------------|---------|-------|
| `{dir}` | Direction | `BUY` | BUY/SELL |
| `{net}` | Net position | `-2` | Integer, positive=long, negative=short |
| `{live}` | Active orders count | `3` | Integer |
| `{src}` | Position source | `Cache` | Portfolio/Positions/Cache/StickyCache |
| `{pnl}` | Open PnL | `150` | Decimal, with `{ac}` suffix |

## Context and State

| Placeholder | Description | Example | Notes |
|-------------|-------------|---------|-------|
| `{barN}` | Signal bar | `17620` | Integer, N bar |
| `{barN1}` | Execution bar | `17621` | Integer, N+1 bar |
| `{n1win}` | N+1 window active | `true` | Boolean |
| `{onePos}` | OnlyOne guard | `PASS` | PASS/BLOCK |
| `{cooldown}` | Cooldown state | `OFF` | ON/OFF |
| `{reason}` | Text reason | `zombie_orders` | String, no spaces |
| `{note}` | Free text | `"equity in USD, no conversion"` | Quoted string |

## Override and Configuration

| Placeholder | Description | Example | Notes |
|-------------|-------------|---------|-------|
| `{ov}` | Override value | `0.5` | Decimal, override applied |
| `{ovApplied}` | Override applied | `true` | Boolean |
| `{csv}` | Raw CSV config | `"MNQ=0.5;NQ=5"` | Quoted string |
| `{keys}` | Parsed keys count | `2` | Integer |
| `{symKey}` | Symbol key used | `MNQ` | String, matched symbol |
| `{hit}` | Override hit | `true` | Boolean, found in CSV |

## Source and Detection

| Placeholder | Description | Example | Notes |
|-------------|-------------|---------|-------|
| `{tickSource}` | Tick value source | `Security` | Security/InstrumentInfo/Fallback |
| `{what}` | Fallback item | `tickSize` | String, what fell back |
| `{val}` | Fallback value | `0.25` | Decimal, fallback used |

## Usage Examples

### Complete Calculation Flow
```
468/CALC IN uid=abc123 mode=FixedRiskUSD slTicks=7 tickCost=0.50USD/t equity=25000USD
468/CALC FIXED uid=abc123 targetRisk=100USD rpc=3.50USD underfunded=false qtyFinal=28
468/CALC OUT uid=abc123 qtyFinal=28 note="orders use manual Quantity=2 until STEP4"
```

### Underfunded Scenarios
```
468/CALC UNDERFUNDED uid=def456 action=ABORT rpc=15.00USD target=10USD mode=FixedRiskUSD
468/CALC UNDERFUNDED uid=ghi789 action=MIN_QTY qty=1 actualRisk=25USD target=10USD
```

### Override Application
```
468/RISK OVERRIDE uid=jkl012 raw="MNQ=0.5;NQ=5" symKey=MNQ hit=true tickCostOverride=0.5USD/t
```

### Position Context
```
468/STR CONTEXT uid=mno345 N=17620 N1=17621 dir=BUY qtyBase=2 qtyAuto=28 guards=PASS
468/POS SNAP uid=mno345 net=0 source=Cache live=0 pnlOpen=0USD
```

## Validation Rules

### Required Placeholders
- `{uid}` - Must be present in ALL risk-related logs
- `{mode}` - Required in all CALC events
- Currency suffixes (`{qc}`, `{ac}`) - Required for monetary values

### Format Constraints
- Boolean: `true`/`false` (lowercase)
- Decimals: Up to 2 decimal places for currency, 4 for rates
- Strings: No spaces in values, use underscores
- Quoted strings: Use double quotes for free text

### Special Values
- `NA` - Use when placeholder doesn't apply
- `UNKNOWN` - Use when value cannot be determined
- `0` - Use for zero values, not empty string