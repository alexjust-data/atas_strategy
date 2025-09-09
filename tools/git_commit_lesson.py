#!/usr/bin/env python3
"""
Git Commit Lesson Script
Creates intelligent commits with lesson metrics and structured messages
"""

import json
import subprocess
import argparse
from pathlib import Path
from datetime import datetime

def run_command(cmd: str, cwd=None) -> tuple[bool, str]:
    """Run shell command and return (success, output)"""
    try:
        result = subprocess.run(
            cmd, shell=True, capture_output=True, text=True, cwd=cwd
        )
        return result.returncode == 0, result.stdout.strip()
    except Exception as e:
        return False, str(e)

def load_lesson_summary(lesson_dir: Path) -> dict:
    """Load lesson summary with metrics"""
    summary_file = lesson_dir / "lesson-summary.json"
    
    if not summary_file.exists():
        return {"error": "No lesson summary found"}
    
    try:
        with open(summary_file, 'r', encoding='utf-8') as f:
            return json.load(f)
    except Exception:
        return {"error": "Could not parse lesson summary"}

def count_generated_files(lesson_dir: Path) -> dict:
    """Count generated files in lesson directory"""
    counts = {
        "screenshots": 0,
        "video_clips": 0,
        "code_stubs": 0,
        "analysis_files": 0,
        "total_files": 0
    }
    
    # Count media files
    media_shots = lesson_dir / "media" / "shots"
    media_clips = lesson_dir / "media" / "clips"
    
    if media_shots.exists():
        counts["screenshots"] = len(list(media_shots.glob("*.png")))
    if media_clips.exists():
        counts["video_clips"] = len(list(media_clips.glob("*.mp4")))
    
    # Count code stubs
    code_dir = lesson_dir / "code"
    if code_dir.exists():
        counts["code_stubs"] = len(list(code_dir.rglob("*.md"))) - 1  # Exclude summary
    
    # Count analysis files
    analysis_dir = lesson_dir / "analysis"
    if analysis_dir.exists():
        counts["analysis_files"] = len(list(analysis_dir.glob("*.md"))) + len(list(analysis_dir.glob("*.json")))
    
    # Total files in lesson
    counts["total_files"] = len(list(lesson_dir.rglob("*")))
    
    return counts

def generate_commit_message(lesson_dir: Path) -> str:
    """Generate intelligent commit message with metrics"""
    lesson_name = lesson_dir.name
    summary = load_lesson_summary(lesson_dir)
    counts = count_generated_files(lesson_dir)
    
    # Extract key metrics
    duration = summary.get("duration_hhmmss", "unknown")
    golden_points = summary.get("content_analysis", {}).get("total_golden_points", 0) if summary.get("content_analysis") else 0
    teaching_style = summary.get("content_analysis", {}).get("teaching_style", "unknown") if summary.get("content_analysis") else "unknown"
    
    # Build compact stats
    stats_parts = []
    if golden_points > 0:
        stats_parts.append(f"{golden_points}gp")
    if counts["screenshots"] > 0:
        stats_parts.append(f"{counts['screenshots']}shots")
    if counts["video_clips"] > 0:
        stats_parts.append(f"{counts['video_clips']}clips")
    if counts["code_stubs"] > 0:
        stats_parts.append(f"{counts['code_stubs']}code")
    
    stats_summary = ", ".join(stats_parts) if stats_parts else "analysis"
    
    # Main commit message
    title = f"lesson-{lesson_name}: {stats_summary} 🎬"
    
    # Detailed body
    body_parts = [
        f"📹 Duration: {duration}",
        f"⭐ Golden points: {golden_points}",
        f"📸 Screenshots: {counts['screenshots']}",
        f"🎬 Video clips: {counts['video_clips']}",
        f"💻 Code stubs: {counts['code_stubs']}",
        f"📊 Analysis files: {counts['analysis_files']}",
        f"🎓 Teaching style: {teaching_style}",
        "",
        f"Generated: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}",
        "🤖 Auto-committed with Claude Code"
    ]
    
    full_message = title + "\n\n" + "\n".join(body_parts)
    return full_message

def commit_lesson(lesson_dir: Path, auto_push: bool = False, dry_run: bool = False) -> bool:
    """Commit lesson with intelligent message"""
    
    if not lesson_dir.exists():
        print(f"❌ Lesson directory not found: {lesson_dir}")
        return False
    
    # Check if we're in a git repo
    success, _ = run_command("git rev-parse --git-dir")
    if not success:
        print("❌ Not in a git repository")
        return False
    
    # Get repo root to ensure relative paths
    success, repo_root = run_command("git rev-parse --show-toplevel")
    if not success:
        print("❌ Could not determine repository root")
        return False
    
    repo_root = Path(repo_root)
    lesson_relative = lesson_dir.relative_to(repo_root)
    
    # Generate commit message
    commit_message = generate_commit_message(lesson_dir)
    
    print(f"🔧 Processing lesson: {lesson_dir.name}")
    print(f"📁 Path: {lesson_relative}")
    print("\n📝 Commit message:")
    print("-" * 60)
    print(commit_message)
    print("-" * 60)
    
    if dry_run:
        print("\n🔍 DRY RUN - No changes made")
        return True
    
    # Stage the lesson directory
    print(f"\n📦 Staging files...")
    success, output = run_command(f'git add "{lesson_relative}"', cwd=repo_root)
    if not success:
        print(f"❌ Failed to stage files: {output}")
        return False
    
    # Check if there are changes to commit
    success, output = run_command("git diff --cached --quiet")
    if success:  # No changes staged
        print("ℹ️  No changes to commit")
        return True
    
    # Commit with message
    print(f"💾 Committing changes...")
    commit_cmd = f'git commit -m "{commit_message.split(chr(10))[0]}" -m "{chr(10).join(commit_message.split(chr(10))[1:])}"'
    success, output = run_command(commit_cmd, cwd=repo_root)
    if not success:
        print(f"❌ Failed to commit: {output}")
        return False
    
    print("✅ Commit created successfully")
    
    # Push if requested
    if auto_push:
        print("🚀 Pushing to remote...")
        success, output = run_command("git push", cwd=repo_root)
        if success:
            print("✅ Pushed to remote successfully")
        else:
            print(f"⚠️  Push failed: {output}")
            return False
    
    return True

def main():
    parser = argparse.ArgumentParser(description="Commit lesson with intelligent metrics")
    parser.add_argument("--lesson", required=True, help="Lesson name (e.g., 18-practice-08)")
    parser.add_argument("--push", action="store_true", help="Push to remote after commit")
    parser.add_argument("--dry-run", action="store_true", help="Show commit message without committing")
    
    args = parser.parse_args()
    
    # Find lesson directory
    script_dir = Path(__file__).resolve().parent
    repo_root = script_dir.parent
    lesson_dir = repo_root / "lessons" / args.lesson
    
    print(f"🎯 Git Commit Lesson: {args.lesson}")
    print("=" * 50)
    
    success = commit_lesson(lesson_dir, auto_push=args.push, dry_run=args.dry_run)
    
    if success:
        print("\n🎉 Operation completed successfully!")
    else:
        print("\n💥 Operation failed!")
        return 1
    
    return 0

if __name__ == "__main__":
    exit(main())