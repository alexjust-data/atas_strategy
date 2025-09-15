#!/usr/bin/env python3
"""
Test Helper Functions - shots and code extraction
Runs the new helpers on Practice-08 lesson with path autodiscovery.
"""

import sys, json
from pathlib import Path

# Repo root = two levels up from this file
ROOT = Path(__file__).resolve().parents[1]
LESSON = ROOT / "lessons" / "18-practice-08"

# Prefer package import if available; else fall back to local folder
PKG = ROOT / "tools" / "mcp-servers"
if str(PKG) not in sys.path:
    sys.path.append(str(PKG))

try:
    from content_analysis_server import generate_shots_todo_from_json, extract_code_references_from_json
except Exception as e:
    print(f"❌ Cannot import helpers from content_analysis_server: {e}")
    sys.exit(1)

def _find_json(lesson_dir: Path) -> Path | None:
    # Prefer transcription JSONs with actual transcript data
    candidates = [
        lesson_dir / "transcription" / "sersans.json",  # Specific name first
        *lesson_dir.glob("transcription/*.json"),       # Any JSON in transcription/
        lesson_dir / "audio.json",                      # Alternative name
    ]
    
    # Check each candidate and validate it has transcript content
    for c in candidates:
        if c.exists():
            try:
                import json
                with open(c, 'r', encoding='utf-8') as f:
                    data = json.load(f)
                # Validate it has transcript structure (text or segments)
                if data.get('text') or data.get('segments'):
                    return c
            except:
                continue
    return None

def _find_video(lesson_dir: Path) -> Path | None:
    for ext in ("*.mp4", "*.mkv", "*.mov"):
        for c in (lesson_dir.glob(ext) or []):
            return c
        for c in (lesson_dir / "source").glob(ext):
            return c
    return None

def main() -> int:
    print("🔧 Testing Helper Functions")
    print("=" * 50)

    # Resolve inputs
    json_file = _find_json(LESSON)
    video_path = _find_video(LESSON)
    out_dir = LESSON

    if not json_file or not json_file.exists():
        print(f"❌ Transcript JSON not found in {LESSON}")
        return 1
    if not video_path or not video_path.exists():
        print(f"❌ Video file not found in {LESSON}")
        return 1

    print(f"📄 JSON:  {json_file.relative_to(ROOT)}")
    print(f"🎬 VIDEO: {video_path.relative_to(ROOT)}")
    print(f"📂 OUT:   {out_dir.relative_to(ROOT)}")
    print()

    # 1) Shots exporter
    print("1️⃣ Testing shots exporter…")
    try:
        shots_result = generate_shots_todo_from_json(
            str(json_file),
            str(out_dir),
            str(video_path),
            "Practice 08"
        )
    except Exception as e:
        print(f"   ❌ Exception in generate_shots_todo: {e}")
        return 1

    if shots_result.get("success"):
        gp = shots_result.get("golden_points_count", 0)
        screenshots = shots_result.get("screenshots_count", 0)
        videos = shots_result.get("video_clips_count", 0)
        files = shots_result.get("files_written", [])
        print(f"   ✅ media.todo generated ({gp} golden points)")
        print(f"   📸 Screenshots: {screenshots} | 🎬 Video clips: {videos}")
        if files:
            print(f"   📁 Files: {files}")
        if shots_result.get("shots_dir"):
            print(f"   📂 Shots dir: {Path(shots_result['shots_dir']).name}")
        if shots_result.get("clips_dir"):
            print(f"   📂 Clips dir: {Path(shots_result['clips_dir']).name}")
    else:
        print(f"   ❌ Failed: {shots_result.get('error')}")
        return 1

    print()

    # 2) Code extraction
    print("2️⃣ Testing code extraction…")
    try:
        code_result = extract_code_references_from_json(
            str(json_file),
            str(out_dir),
            "Practice 08"
        )
    except Exception as e:
        print(f"   ❌ Exception in extract_code_references_with_context: {e}")
        return 1

    if code_result.get("success"):
        n = code_result.get("code_files_generated", 0)
        plats = code_result.get("platforms_detected", [])
        files = code_result.get("files_written", [])
        print(f"   ✅ {n} code files generated")
        print(f"   🔧 Platforms: {plats}")
        print(f"   📁 Files: {files}")
        if code_result.get("code_dir"):
            print(f"   📂 Code dir: {Path(code_result['code_dir']).resolve()}")
    else:
        print(f"   ❌ Failed: {code_result.get('error')}")
        return 1

    print("\n🎉 Helper testing completed!")
    return 0

if __name__ == "__main__":
    sys.exit(main())