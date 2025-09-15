#!/usr/bin/env python3
"""
Video Processing Test – parametric CLI
Runs the transcription pipeline on one video and writes outputs + a summary.json
"""

import sys, json, argparse
from pathlib import Path
from datetime import timedelta

# Import server functions (unit-test style; no MCP handshake)
sys.path.append(str(Path(__file__).parent / "mcp-servers"))
from transcription_server import (
    process_video_to_transcript,
    get_server_status,
)

def hhmmss(seconds: float) -> str:
    return str(timedelta(seconds=int(max(0, seconds))))

def main():
    ap = argparse.ArgumentParser(description="Test transcription on one video")
    ap.add_argument("--video", required=True, help="Path to input video (mp4/mkv/...)")
    ap.add_argument("--out", required=True, help="Output directory for files")
    ap.add_argument("--model", default="large-v3", help="Whisper model (tiny/base/small/medium/large[-v3])")
    ap.add_argument("--lang", default="es", help="Language code or 'auto'")
    ap.add_argument("--word-timestamps", dest="word_ts", action="store_true", help="Enable word-level timestamps")
    ap.add_argument("--no-word-timestamps", dest="word_ts", action="store_false", help="Disable word-level timestamps")
    ap.set_defaults(word_ts=True)
    ap.add_argument("--keep-audio", action="store_true", help="Keep extracted WAV in output dir")
    ap.add_argument("--formats", default="txt,vtt,srt,json", help="Comma-separated outputs to write")
    ap.add_argument("--fast", action="store_true",
                    help="Shortcut: model=small, no word timestamps (faster, cheaper)")
    args = ap.parse_args()

    video_path = Path(args.video)
    out_dir = Path(args.out)

    if args.fast:
        args.model = "small"
        args.word_ts = False

    if not video_path.exists():
        print(f"❌ Video not found: {video_path}", file=sys.stderr)
        return 1
    out_dir.mkdir(parents=True, exist_ok=True)

    print("🎯 Testing Video Processing")
    print(f"📹 Video: {video_path.name}")
    print(f"💾 Output: {out_dir}")
    print("—" * 60)

    # 1) Server status
    st = get_server_status()
    print(f"1️⃣ Server: {st.get('status')}, device={st.get('device')}, ffmpeg={st.get('ffmpeg_available')}")
    print()

    # 2) Run pipeline
    formats = [s.strip() for s in args.formats.split(",") if s.strip()]
    trading_prompt = (
        "Transcripción de un vídeo de trading algorítmico con terminología técnica: "
        "estrategias, indicadores (RSI, medias móviles, Bollinger), gestión del riesgo, "
        "futuros e índices. Corrige jerga y nombres propios de plataformas."
    )

    print("2️⃣ Processing…")
    print(f"   🧠 Model: {args.model} | 🗣️ Lang: {args.lang} | ⏱️ WordTS: {args.word_ts} | Formats: {formats}")
    res = process_video_to_transcript(
        video_path=str(video_path),
        model_size=args.model,
        language=args.lang,
        word_timestamps=args.word_ts,
        keep_audio=args.keep_audio,
        save_dir=str(out_dir),
        outputs=formats,
        initial_prompt=trading_prompt,
    )

    if not res.get("success"):
        print("❌ PROCESSING FAILED")
        print(f"Error: {res.get('error', 'unknown')}")
        return 1

    # 3) Report
    tr = res["transcription"]
    written = tr.get("written", {}) or {}
    print("✅ COMPLETED")
    print("—" * 60)
    print(f"📹 Source: {Path(res['source_video']).name}")
    print(f"🎵 Audio size: {res.get('audio_size_mb')} MB")
    print(f"🧠 Model: {res.get('model_used')} | 💻 Device: {res.get('device')}")
    print(f"🗣️ Detected: {tr.get('language')} | Segments: {tr.get('segment_count')}")
    print(f"⏱️ Duration: {tr.get('total_duration_hhmmss')} ({tr.get('total_duration_s'):.1f}s)")
    print()
    print("📁 Generated files:")
    for fmt, p in written.items():
        try:
            size_kb = Path(p).stat().st_size / 1024
            print(f"   • {fmt.upper():4s} {Path(p).name}  ({size_kb:.1f} KB)")
        except Exception:
            print(f"   • {fmt.upper():4s} {Path(p).name}  (size n/a)")
    if res.get("kept_audio_path"):
        wav = Path(res["kept_audio_path"])
        print(f"   • AUDIO  {wav.name} ({wav.stat().st_size/1024/1024:.1f} MB)")
    print()

    # Preview
    text = tr.get("text", "")
    preview = (text[:500] + "...") if len(text) > 500 else text
    print("📖 Transcript preview")
    print("-" * 40)
    print(preview)
    print("-" * 40)

    # Show first 3 segments with proper hh:mm:ss
    segs = tr.get("segments", []) or []
    print("\n⏱️ First segments:")
    for i, s in enumerate(segs[:3], start=1):
        print(f"   {i:02d}. [{hhmmss(s.get('start',0))} - {hhmmss(s.get('end',0))}] {s.get('text','')[:80]}...")

    # 4) Content Analysis (if JSON was generated)
    content_analysis_result = None
    json_file = written.get("json")
    if json_file and Path(json_file).exists():
        try:
            print("\n🧠 Running content analysis...")
            # Import content analysis
            sys.path.append(str(Path(__file__).parent / "mcp-servers"))
            from content_analysis_server import analyze_transcript_from_json, export_notes, export_concepts
            
            # Run analysis
            lesson_name = f"Practice {video_path.stem}"
            analysis_result = analyze_transcript_from_json(json_file, lesson_name)
            
            if analysis_result.get("success"):
                # Export notes and concepts automatically
                notes_result = export_notes(json_file, str(out_dir), lesson_name)
                concepts_result = export_concepts(json_file, str(out_dir), lesson_name)
                
                analysis_summary = analysis_result["analysis"]["summary"]
                print(f"   ✅ Content analysis completed!")
                print(f"   📊 Found: {analysis_summary['total_golden_points']} golden points, {analysis_summary['total_concepts_identified']} concepts")
                print(f"   🎓 Teaching style: {analysis_summary['teaching_style']}")
                print(f"   🔬 Academic research needed: {analysis_summary['academic_research_needed']} topics")
                
                if notes_result.get("success"):
                    print(f"   📝 Notes exported: {', '.join(notes_result['files_written'])}")
                if concepts_result.get("success"):
                    print(f"   📊 Concepts exported: {concepts_result['file_written']}")
                    
                content_analysis_result = analysis_summary
            else:
                print(f"   ⚠️ Content analysis failed: {analysis_result.get('error')}")
                
        except Exception as e:
            print(f"   ⚠️ Content analysis error: {e}")
    
    # 5) Save enhanced summary.json for auditing
    summary = {
        "video": str(video_path),
        "out_dir": str(out_dir),
        "model": args.model,
        "language": args.lang,
        "word_timestamps": args.word_ts,
        "keep_audio": args.keep_audio,
        "formats": formats,
        "detected_language": tr.get("language"),
        "segment_count": tr.get("segment_count"),
        "duration_s": tr.get("total_duration_s"),
        "duration_hhmmss": tr.get("total_duration_hhmmss"),
        "content_analysis": content_analysis_result  # Add analysis results
    }
    (out_dir / "lesson-summary.json").write_text(json.dumps(summary, indent=2, ensure_ascii=False), encoding="utf-8")
    print("\n📝 Saved: lesson-summary.json")
    print("🎉 SUCCESS")
    return 0

if __name__ == "__main__":
    sys.exit(main())