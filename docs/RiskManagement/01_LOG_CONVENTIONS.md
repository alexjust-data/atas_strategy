# 01. Risk Management Log Conventions

## Table of Contents
- [Tag Standards](#tag-standards)
- [Message Format](#message-format)
- [Log Event Types](#log-event-types)
- [Examples](#examples)
- [Best Practices](#best-practices)

## Tag Standards

### Official Tags (grep-friendly)
- `468/RISK` - Risk detection, overrides, diagnostics
- `468/SL` - Stop Loss calculations
- `468/CALC` - Position sizing calculations
- `468/STR` - Strategy context and guards
- `468/ORD` - Order management and brackets
- `468/POS` - Position tracking and snapshots

### Tag Subtypes
- `468/RISK SNAPSHOT` - Initial state capture
- `468/RISK OVERRIDE` - CSV override parsing and application
- `468/RISK DIAG` - Diagnostic updates
- `468/RISK RISK_FALLBACK` - Fallback value usage
- `468/SL CALC` - SL distance calculations
- `468/CALC IN|MANUAL|FIXED|PCT|UNDERFUNDED|OUT|PING` - Calculation states
- `468/STR CONTEXT` - N→N+1 context, guards, quantities
- `468/ORD BRK-PREFLIGHT|BRK-ATTACH` - Bracket operations
- `468/POS SNAP` - Position snapshots

## Message Format

### Core Rules
1. **One line = one event** - No multi-line entries
2. **Key=value format** - All parameters as `key=value` pairs
3. **UID mandatory** - Every risk-related event must include `uid={uid}`
4. **NA for non-applicable** - Use `NA` when value doesn't apply
5. **No spaces in values** - Use underscores or concatenation

### Standard Pattern
```
[timestamp] LEVEL tag uid={uid} key1=value1 key2=value2 ... note="optional free text"
```

## Log Event Types

### 1. RISK Events

#### SNAPSHOT (System State)
```
468/RISK SNAPSHOT uid={uid} ts={ts} instr={instr} code={code} exch={exch} qc={qc} tickSize={tickSize} tickCost={tickCost}{qc}/t source={tickSource} equityChosen={equity}{ac} equitySrc={equitySrc} ba={balAvail}{ac} bal={bal}{ac} mode={mode} riskUsd={riskUsd}{ac} riskPct={riskPct}% slOffsetTicks={slTicksBase} useSignalSL={useSigSL} overridesTickCost={ov}{qc}/t overridesApplied={ovApplied}
```

#### OVERRIDE (CSV Parsing)
```
468/RISK OVERRIDE uid={uid} raw="{csv}" parsedKeys={keys} symbolKey={symKey} hit={hit} tickCostOverride={ov}{qc}/t
```

#### DIAG (Diagnostic Updates)
```
468/RISK DIAG uid={uid} tickSize={tickSize} tickCost={tickCost}{qc}/t equity={equity}{ac} mode={mode} riskUsd={riskUsd}{ac} riskPct={riskPct}% slOffsetTicks={slTicksBase}
```

#### RISK_FALLBACK (Fallback Usage)
```
468/RISK RISK_FALLBACK uid={uid} used={what} value={val}
```

### 2. SL Events

#### CALC (Stop Loss Calculation)
```
468/SL CALC uid={uid} baseTicks={slBase} signalSL={useSigSL} bufferTicks={buf} limitsApplied={lim} totalSLTicks={slTicks} tickSize={tickSize} note={note}
```

### 3. CALC Events

#### IN (Calculation Input)
```
468/CALC IN uid={uid} mode={mode} slTicks={slTicks} tickCost={tickCost}{qc}/t tickSize={tickSize} equity={equity}{ac} note="equity in {ac}, tickCost in {qc}; no currency conversion at step3"
```

#### MANUAL (Manual Mode)
```
468/CALC MANUAL uid={uid} qty={qty} rpc={rpc}{qc} totalRisk={tot}{qc}
```

#### FIXED (Fixed USD Mode)
```
468/CALC FIXED uid={uid} targetRisk={target}{ac} rpc={rpc}{qc} underfunded={uf} qtyRaw={qtyRaw} qtySnap={qtySnap} qtyClamp={qtyClamp} qtyFinal={qty}
```

#### PCT (Percent of Account Mode)
```
468/CALC PCT uid={uid} equity={equity}{ac} pct={pct}% targetRisk={target}{ac} rpc={rpc}{qc} underfunded={uf} qtyRaw={qtyRaw} qtySnap={qtySnap} qtyClamp={qtyClamp} qtyFinal={qty}
```

#### UNDERFUNDED (Underfunded Protection)
```
468/CALC UNDERFUNDED uid={uid} action=ABORT qty=0 rpc={rpc}{qc} target={target}{ac} slTicks={slTicks} mode={mode}
468/CALC UNDERFUNDED uid={uid} action=MIN_QTY qty={minQty} actualRisk={act}{qc} rpc={rpc}{qc} target={target}{ac} slTicks={slTicks} mode={mode}
```

#### OUT (Calculation Output)
```
468/CALC OUT uid={uid} mode={mode} qtyFinal={qty} rpc={rpc}{qc} slTicks={slTicks} note="orders still use manual Quantity={manQty} until STEP4"
```

#### PING (Testing/Monitoring)
```
468/CALC PING uid={uid} bar={bar} mode={mode} slTicks={slTicks} rpc={rpc}{qc} lastQty={qty}
```

### 4. STR Events

#### CONTEXT (Strategy Context)
```
468/STR CONTEXT uid={uid} N={barN} N1={barN1} dir={dir} qtyBase={manQty} qtyAuto={autoQty} guards OnlyOne={onePos} Cooldown={cooldown} N1Window={n1win}
```

### 5. ORD Events

#### BRK-PREFLIGHT (Bracket Pre-check)
```
468/ORD BRK-PREFLIGHT uid={uid} dir={dir} entry={pxEntry} sl={pxSL} tp1={pxTP1} tp2={pxTP2} netBefore={net} okToAttach={ok} reason={reason}
```

#### BRK-ATTACH (Bracket Attachment)
```
468/ORD BRK-ATTACH uid={uid} result={ok|fail} liveOrders={live} reason={reason}
```

### 6. POS Events

#### SNAP (Position Snapshot)
```
468/POS SNAP uid={uid} net={net} source={src} activeOrders={live} pnlOpen={pnl}{ac}
```

## Examples

### Complete Flow Example
```
[13:45:01.123] WARNING 468/RISK SNAPSHOT uid=abc123 ts=13:45:01.123 instr=MNQ code=MNQ exch=CME qc=USD tickSize=0.25 tickCost=0.50USD/t source=Security equityChosen=25000USD equitySrc=BalanceAvailable ba=25000USD bal=25000USD mode=FixedRiskUSD riskUsd=100USD riskPct=NA slOffsetTicks=2 useSignalSL=true overridesTickCost=NASD/t overridesApplied=false

[13:45:01.124] WARNING 468/SL CALC uid=abc123 baseTicks=2 signalSL=true bufferTicks=5 limitsApplied=false totalSLTicks=7 tickSize=0.25 note="base SL + signal candle buffer"

[13:45:01.125] WARNING 468/CALC IN uid=abc123 mode=FixedRiskUSD slTicks=7 tickCost=0.50USD/t tickSize=0.25 equity=25000USD note="equity in USD, tickCost in USD; no currency conversion at step3"

[13:45:01.126] WARNING 468/CALC FIXED uid=abc123 targetRisk=100USD rpc=3.50USD underfunded=false qtyRaw=28.57 qtySnap=28 qtyClamp=28 qtyFinal=28

[13:45:01.127] WARNING 468/CALC OUT uid=abc123 mode=FixedRiskUSD qtyFinal=28 rpc=3.50USD slTicks=7 note="orders still use manual Quantity=2 until STEP4"
```

## Best Practices

### For Developers
1. **Always include UID** in risk-related logs
2. **Use consistent key names** as defined in 02_PLACEHOLDERS.md
3. **Include currency indicators** (`{qc}`, `{ac}`) for monetary values
4. **Use structured notes** for free-text explanations
5. **Log state changes** not just final values

### For Operations
1. **Filter by UID** to trace complete operation flows
2. **Use tags** for focused troubleshooting
3. **Check UNDERFUNDED** events for risk violations
4. **Monitor FALLBACK** usage for configuration issues
5. **Correlate timestamps** across different subsystems

### For Troubleshooting
1. **Start with SNAPSHOT** to understand initial state
2. **Follow CALC flow** to understand quantity decisions
3. **Check STR CONTEXT** for guard failures
4. **Review ORD events** for bracket issues
5. **Use PING events** for heartbeat monitoring