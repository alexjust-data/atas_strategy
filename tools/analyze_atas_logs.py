# python analyze_atas_logs.py --log "logs\current\ATAS_SESSION_LOG.txt" --outdir "risk_analysis"


#!/usr/bin/env python3
"""
analyze_atas_logs.py
--------------------
Exhaustive analysis of an ATAS session log focused on Risk Management (468/RISK),
Calculation engine (468/CALC), Strategy (468/STR), Orders (468/ORD) and Position (468/POS).

USAGE
-----
python analyze_atas_logs.py --log "<path to ATAS_SESSION_LOG.txt>" --outdir "./risk_analysis"

WHAT IT DOES
------------
- Parses the log file and extracts structured events with timestamps (when present),
  tags, and payloads.
- Computes KPIs:
    * Counts by tag and by subtype (SNAPSHOT, PULSE, INIT, ABORT, WARNING, etc.)
    * Signal → Entry → Fill chain integrity
    * Underfunded decisions (ABORT vs minQty) and frequency
    * AutoQty usage (when soft-engage is enabled) and reasons when ignored
    * Bracket attach success rate; TP/SL outcomes
    * SL drift diagnostics (planned vs actual) if present
- Emits CSVs for further analysis and a Markdown report with clear next steps.
"""

import argparse, os, re, json, csv
from datetime import datetime
from pathlib import Path
import pandas as pd

TAG_RE = re.compile(r'(?P<ts>\d{2}:\d{2}:\d{2})?.*?(?P<tag>468/(?:RISK|CALC|STR|ORD|POS))\s*[:,]\s*(?P<msg>.*)$')

SUBTYPE_PATTERNS = {
    'INIT': re.compile(r'\bINIT\b', re.I),
    'SNAPSHOT': re.compile(r'\bSNAPSHOT\b', re.I),
    'PULSE': re.compile(r'\bPULSE\b', re.I),
    'ABORT': re.compile(r'\bABORT\b', re.I),
    'WARNING': re.compile(r'\bWARNING\b', re.I),
    'ERROR': re.compile(r'\bERROR\b|\bEXCEPTION\b|\bEX:\b', re.I),
    'ENTRY_SENT': re.compile(r'\bENTRY sent\b|\bMARKET ORDER SENT\b|\bSubmitMarket CALLED\b', re.I),
    'BRACKETS': re.compile(r'\bBRACKETS\b|\bSTOP submitted\b|\bLIMIT submitted\b', re.I),
    'TP': re.compile(r'\bTP\b', re.I),
    'SL': re.compile(r'\bSL\b', re.I),
    'CONSISTENCY': re.compile(r'\bCONSISTENCY SL\b|\bSL DRIFT\b', re.I),
    'HEARTBEAT': re.compile(r'\bHEARTBEAT\b', re.I),
    'CAPTURE': re.compile(r'\bCAPTURE\b|\bPENDING\b', re.I),
}

NUM_RE = re.compile(r'(?P<key>\b[a-zA-Z_]+)=(?P<val>-?\d+(?:\.\d+)?)')

def parse_line(line):
    m = TAG_RE.search(line)
    if not m:
        return None
    ts = m.group('ts') or ''
    tag = m.group('tag')
    msg = m.group('msg').strip()
    subtypes = [name for name,pat in SUBTYPE_PATTERNS.items() if pat.search(msg)]
    nums = {k: float(v) for k,v in NUM_RE.findall(msg)}
    return {'ts': ts, 'tag': tag, 'msg': msg, 'subtypes': ';'.join(subtypes), **nums}

def load_events(path):
    events = []
    with open(path, 'r', encoding='utf-8', errors='ignore') as f:
        for i, line in enumerate(f, 1):
            rec = parse_line(line)
            if rec:
                rec['line'] = i
                events.append(rec)
    return pd.DataFrame(events)

def kpi_counts(df):
    by_tag = df.groupby('tag').size().reset_index(name='count')
    by_sub = df.explode('subtypes').groupby('subtypes').size().reset_index(name='count')
    return by_tag, by_sub

def extract_flags(df):
    mask = (df['tag']=='468/RISK') & df['msg'].str.contains('INIT flags', case=False, na=False)
    if mask.any():
        row = df[mask].iloc[0]
        flags = {}
        for key in ['EnableRiskManagement','RiskDryRun','effectiveDryRun']:
            m = re.search(rf'{key}\s*=\s*(\w+)', row['msg'])
            if m: flags[key] = m.group(1)
        return flags
    return {}

def soft_engage_usage(df):
    used = df[(df['tag']=='468/STR') & df['msg'].str.contains('ENTRY qty source=AUTO', na=False)]
    ignored = df[(df['tag']=='468/STR') & df['msg'].str.contains('ENTRY qty source=MANUAL', na=False)]
    aborted = df[(df['tag']=='468/STR') & df['msg'].str.contains('ENTRY ABORTED: autoQty<=0', na=False)]
    return {'used_auto': len(used), 'ignored_auto': len(ignored), 'aborted_auto': len(aborted)}

def underfunded_stats(df):
    aborts = df[(df['tag']=='468/CALC') & df['msg'].str.contains('ABORT: Underfunded', na=False)]
    warnings = df[(df['tag']=='468/CALC') & df['msg'].str.contains('WARNING: Underfunded', na=False)]
    return {'underfunded_aborts': len(aborts), 'underfunded_forced_min': len(warnings)}

def calc_snapshots(df):
    snaps = df[(df['tag']=='468/CALC') & df['msg'].str.contains('SNAPSHOT', na=False)]
    cols = ['qty','slTicks','rpc','equity','tickValue']
    out = []
    for _, r in snaps.iterrows():
        row = {'line': r['line'], 'ts': r['ts']}
        for c in cols:
            m = re.search(rf'\b{c}\s*=\s*([0-9\.\-]+)', r['msg'])
            if m: row[c] = float(m.group(1))
        out.append(row)
    return pd.DataFrame(out)

def sl_drift(df):
    cons = df[(df['tag']=='468/CALC') & df['msg'].str.contains('CONSISTENCY SL', na=False)]
    deltas = []
    for _, r in cons.iterrows():
        m = re.search(r'delta\s*=\s*([-\d\.]+)', r['msg'], re.I)
        if m: deltas.append(float(m.group(1)))
    return {'sl_drift_events': len(cons), 'mean_abs_drift_ticks': (sum(abs(x) for x in deltas)/len(deltas) if deltas else 0.0)}

def entries_chain(df):
    entries = df[(df['tag']=='468/STR') & df['msg'].str.contains('ENTRY sent', na=False)]
    fills   = df[(df['tag']=='468/ORD') & df['msg'].str.contains('Filled', na=False)]
    brackets= df[(df['tag']=='468/STR') & df['msg'].str.contains('BRACKETS ATTACHED', na=False)]
    return {'entries': len(entries), 'fills_or_partials': len(fills), 'brackets_attached': len(brackets)}

def write_report(outdir, df, kpis):
    outdir = Path(outdir); outdir.mkdir(parents=True, exist_ok=True)
    df.to_csv(outdir/'events.csv', index=False)
    for tag in ['468/STR','468/RISK','468/CALC','468/ORD','468/POS']:
        df[df['tag']==tag].to_csv(outdir/(f'{tag.replace("/","_").lower()}.csv'), index=False)

    md = outdir/'risk_report.md'
    with open(md, 'w', encoding='utf-8') as f:
        f.write("# ATAS Risk Management Forensic Report\n\n")
        f.write(f"- Generated: {datetime.utcnow().isoformat()}Z\n")
        f.write(f"- Source log: `{kpis['meta']['log']}`\n\n")

        f.write("## Flags (from INIT)\n")
        f.write("```\n" + json.dumps(kpis['flags'], indent=2) + "\n```\n\n")

        f.write("## KPI Summary\n")
        f.write("```\n" + json.dumps({k:v for k,v in kpis.items() if k not in ['meta','flags','by_tag','by_sub','calc_snaps']}, indent=2) + "\n```\n\n")

        f.write("## Counts by Tag\n")
        f.write(kpis['by_tag'].to_markdown(index=False) + "\n\n")

        f.write("## Counts by Subtype\n")
        f.write(kpis['by_sub'].to_markdown(index=False) + "\n\n")

        if not kpis['calc_snaps'].empty:
            f.write("## Calculation Snapshots (sample)\n")
            f.write(kpis['calc_snaps'].head(20).to_markdown(index=False) + "\n\n")
        else:
            f.write("## Calculation Snapshots\n_No SNAPSHOT events found._\n\n")

        f.write("## Recommendations\n")
        recs = []
        if kpis['soft_engage']['used_auto']==0 and kpis['flags'].get('EnableRiskManagement','False')=='True':
            recs.append("- Risk is enabled but auto quantity was never used. Verify `RiskDryRun=OFF` and `UseAutoQuantityForLiveOrders=ON`.")
        if kpis['underfunded']['underfunded_aborts']>0:
            recs.append("- Underfunded aborts detected. Review `RiskPerTradeUsd` / `RiskPercentOfAccount` or reduce stop distance.")
        if kpis['sl_drift']['mean_abs_drift_ticks']>1.0:
            recs.append("- Significant SL drift observed (>= 1 tick average). Check bracket SL computation vs planned SL ticks.")
        if not recs:
            recs = ["- No critical issues detected in this session."]
        f.write("\n".join(recs) + "\n")

    print(f"[OK] Wrote: {md}")
    return md

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--log", required=True, help="Path to ATAS log file (e.g., logs/current/ATAS_SESSION_LOG.txt)")
    ap.add_argument("--outdir", default="risk_analysis", help="Output directory for report + CSVs")
    args = ap.parse_args()

    log_path = Path(args.log)
    if not log_path.exists():
        raise SystemExit(f"Log not found: {log_path}")

    df = load_events(log_path)
    if df.empty:
        raise SystemExit("No recognizable 468/* events found. Is this the correct log?")

    by_tag, by_sub = kpi_counts(df)
    flags = extract_flags(df)
    soft = soft_engage_usage(df)
    under = underfunded_stats(df)
    snaps = calc_snapshots(df)
    drift = sl_drift(df)
    chain = entries_chain(df)

    kpis = {
        'meta': {'log': str(log_path)},
        'flags': flags,
        'by_tag': by_tag,
        'by_sub': by_sub,
        'soft_engage': soft,
        'underfunded': under,
        'sl_drift': drift,
        'entries_chain': chain,
        'calc_snaps': snaps,
    }

    outdir = Path(args.outdir)
    outdir.mkdir(parents=True, exist_ok=True)
    by_tag.to_csv(outdir/'counts_by_tag.csv', index=False)
    by_sub.to_csv(outdir/'counts_by_subtype.csv', index=False)
    snaps.to_csv(outdir/'calc_snapshots.csv', index=False)
    with open(outdir/'kpis.json', 'w', encoding='utf-8') as jf:
        json.dump({k:(v if isinstance(v,(dict,list)) else None) for k,v in kpis.items()}, jf, indent=2)

    write_report(outdir, df, kpis)

if __name__ == "__main__":
    main()
