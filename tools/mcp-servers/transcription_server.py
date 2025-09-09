#!/usr/bin/env python3
"""
MCP Transcription Server (improved)
- Exposes tools to extract audio (ffmpeg) and transcribe (Whisper)
- Writes TXT/VTT/SRT/JSON if requested
- Supports auto language, optional word timestamps, CUDA if available
"""
import asyncio
import os
import json
import logging
import shutil
import tempfile
import subprocess
from pathlib import Path
from typing import Dict, Any, List, Optional, Tuple

import torch
import whisper
from whisper.utils import get_writer
from fastmcp import FastMCP

# Apply FFmpeg patch for Windows compatibility
import sys
sys.path.append(str(Path(__file__).parent.parent))
from ffmpeg_patch import apply_patch
# Apply patch explicitly
apply_patch()

# FFmpeg detection with multiple fallbacks
try:
    from imageio_ffmpeg import get_ffmpeg_exe
except Exception:
    get_ffmpeg_exe = None

# ──────────────────────────────────────────────────────────────────────────────
# Logging
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s | %(levelname)s | %(message)s"
)
log = logging.getLogger("mcp-transcription")

# MCP server
mcp = FastMCP("Transcription Server")

# Global Whisper model with size tracking
_WHISPER_MODEL = None
_WHISPER_MODEL_NAME = None
_WHISPER_DEVICE = "cuda" if torch.cuda.is_available() else "cpu"

def _hhmmss(seconds: float) -> str:
    seconds = max(0, float(seconds))
    h = int(seconds // 3600)
    m = int((seconds % 3600) // 60)
    s = seconds - h*3600 - m*60
    return f"{h:02d}:{m:02d}:{int(s):02d}"

def _ffmpeg_bin() -> Optional[str]:
    """Find FFmpeg binary with multiple fallback methods."""
    # 1) Environment variable override
    p = os.environ.get("FFMPEG_BIN")
    if p and Path(p).exists():
        log.info(f"Using FFmpeg from FFMPEG_BIN: {p}")
        return p
    
    # 2) System PATH
    p = shutil.which("ffmpeg")
    if p:
        log.info(f"Using FFmpeg from PATH: {p}")
        return p
    
    # 3) imageio-ffmpeg (common in Python envs)
    if get_ffmpeg_exe:
        try:
            p = get_ffmpeg_exe()
            if p and Path(p).exists():
                log.info(f"Using FFmpeg from imageio-ffmpeg: {p}")
                return p
        except Exception:
            pass
    
    # 4) Common Windows locations
    common_paths = [
        "C:/ffmpeg/bin/ffmpeg.exe",
        "C:/Program Files/ffmpeg/bin/ffmpeg.exe",
        "C:/ProgramData/chocolatey/bin/ffmpeg.exe"
    ]
    for p in common_paths:
        if Path(p).exists():
            log.info(f"Using FFmpeg from common location: {p}")
            return p
    
    log.warning("FFmpeg not found in any location")
    return None

def _ensure_ffmpeg() -> bool:
    return _ffmpeg_bin() is not None

def load_whisper_model(model_size: str = "base") -> whisper.Whisper:
    """Smart-load Whisper with model size change detection."""
    global _WHISPER_MODEL, _WHISPER_MODEL_NAME
    if _WHISPER_MODEL is None or _WHISPER_MODEL_NAME != model_size:
        log.info(f"Loading Whisper model: {model_size} on {_WHISPER_DEVICE}")
        _WHISPER_MODEL = whisper.load_model(model_size, device=_WHISPER_DEVICE)
        _WHISPER_MODEL_NAME = model_size
        log.info("Whisper model loaded.")
    return _WHISPER_MODEL

# Setup FFmpeg PATH globally for Whisper compatibility
def _setup_ffmpeg_path():
    """Ensure FFmpeg is in PATH for Whisper to find it"""
    ffmpeg_bin = _ffmpeg_bin()
    if ffmpeg_bin:
        ff_dir = str(Path(ffmpeg_bin).parent)
        current_path = os.environ.get("PATH", "")
        if ff_dir not in current_path:
            os.environ["PATH"] = ff_dir + os.pathsep + current_path
            log.info(f"🔧 Added FFmpeg directory to PATH: {ff_dir}")
            # Also set FFMPEG_BIN for Whisper to find it directly
            os.environ["FFMPEG_BIN"] = ffmpeg_bin
            return True
    else:
        log.warning("⚠️ FFmpeg not found - may cause Whisper issues")
        return False

# Call setup on module import
_setup_ffmpeg_path()

# ──────────────────────────────────────────────────────────────────────────────
# Core functions (no decorators - for direct import)
def extract_audio_from_video(
    video_path: str,
    output_path: Optional[str] = None,
    sample_rate: int = 16000
) -> Dict[str, Any]:
    """Extract mono WAV audio using ffmpeg."""
    try:
        ffmpeg_bin = _ffmpeg_bin()
        if not ffmpeg_bin:
            return {"success": False, "error": "ffmpeg not found (try: pip install imageio-ffmpeg or set FFMPEG_BIN env var)"}
        
        vf = Path(video_path)
        if not vf.exists():
            return {"success": False, "error": f"Video not found: {video_path}"}

        of = Path(output_path) if output_path else vf.with_suffix(".wav")
        cmd = [
            ffmpeg_bin,  # Use detected binary
            "-hide_banner", "-loglevel", "error",
            "-i", str(vf),
            "-vn", "-sn", "-dn",
            "-acodec", "pcm_s16le",
            "-ar", str(sample_rate),
            "-ac", "1",
            "-y",
            str(of),
        ]
        log.info(f"Extracting audio: {vf.name} → {of.name}")
        res = subprocess.run(cmd, capture_output=True, text=True)
        if res.returncode != 0:
            return {"success": False, "error": f"ffmpeg failed: {res.stderr[:400]}"}
        return {
            "success": True,
            "audio_path": str(of),
            "size_mb": round(of.stat().st_size / 1024 / 1024, 2),
            "sample_rate": sample_rate,
        }
    except Exception as e:
        return {"success": False, "error": f"Audio extraction failed: {e}"}

# ──────────────────────────────────────────────────────────────────────────────
def transcribe_audio(
    audio_path: str,
    model_size: str = "base",
    language: str = "auto",
    word_timestamps: bool = False,
    save_dir: Optional[str] = None,
    outputs: Optional[List[str]] = None,   # e.g., ["txt","vtt","srt","json"]
    initial_prompt: Optional[str] = None   # bias: trading terms, etc.
) -> Dict[str, Any]:
    """Transcribe WAV/MP3/etc with Whisper. Optionally write TXT/VTT/SRT/JSON."""
    try:
        af = Path(audio_path)
        if not af.exists():
            return {"success": False, "error": f"Audio not found: {audio_path}"}

        model = load_whisper_model(model_size)

        lang = None if language in ("auto", "", None) else language
        log.info(f"Transcribing {af.name} | lang={lang or 'auto'} | words={word_timestamps}")

        result = model.transcribe(
            str(af),
            language=lang,
            word_timestamps=word_timestamps,
            verbose=False,
            condition_on_previous_text=True,
            initial_prompt=initial_prompt or None,
        )

        segs = []
        for seg in result.get("segments", []):
            segs.append({
                "id": seg.get("id"),
                "start": seg.get("start"),
                "end": seg.get("end"),
                "text": seg.get("text", "").strip()
            })

        text = result.get("text", "").strip()
        detected_lang = result.get("language") or lang or "unknown"

        # Write requested outputs to organized structure
        written = {}
        if save_dir:
            # Create organized subdirectories
            base_dir = Path(save_dir)
            transcription_dir = base_dir / "transcription"
            transcription_dir.mkdir(parents=True, exist_ok=True)
            stem = af.stem

            if outputs:
                if "txt" in outputs:
                    p = transcription_dir / f"{stem}.txt"
                    p.write_text(text, encoding="utf-8")
                    written["txt"] = str(p)

                if "json" in outputs:
                    p = transcription_dir / f"{stem}.json"
                    p.write_text(json.dumps({
                        "text": text,
                        "language": detected_lang,
                        "segments": segs
                    }, ensure_ascii=False, indent=2), encoding="utf-8")
                    written["json"] = str(p)

                # Use Whisper writers for vtt/srt (output to transcription dir)
                if "vtt" in outputs:
                    writer = get_writer("vtt", str(transcription_dir))
                    writer(result, audio_path, {"highlight_words": word_timestamps})
                    written["vtt"] = str(transcription_dir / f"{stem}.vtt")
                if "srt" in outputs:
                    writer = get_writer("srt", str(transcription_dir))
                    writer(result, audio_path, {"highlight_words": word_timestamps})
                    written["srt"] = str(transcription_dir / f"{stem}.srt")

        total_dur = max((s["end"] for s in segs), default=0.0)
        return {
            "success": True,
            "language": detected_lang,
            "text": text,
            "segments": segs,
            "segment_count": len(segs),
            "total_duration_s": total_dur,
            "total_duration_hhmmss": _hhmmss(total_dur),
            "written": written
        }
    except Exception as e:
        return {"success": False, "error": f"Transcription failed: {e}"}

# ──────────────────────────────────────────────────────────────────────────────
def process_video_to_transcript(
    video_path: str,
    model_size: str = "base",
    language: str = "auto",
    word_timestamps: bool = False,
    keep_audio: bool = False,
    save_dir: Optional[str] = None,
    outputs: Optional[List[str]] = None,
    initial_prompt: Optional[str] = None
) -> Dict[str, Any]:
    """Pipeline: video → audio → transcript (and files)."""
    try:
        with tempfile.TemporaryDirectory() as td:
            audio_path = Path(td) / "audio.wav"
            a = extract_audio_from_video(video_path, str(audio_path))
            if not a.get("success"):
                return a

            t = transcribe_audio(
                str(audio_path),
                model_size=model_size,
                language=language,
                word_timestamps=word_timestamps,
                save_dir=save_dir,
                outputs=outputs,
                initial_prompt=initial_prompt
            )
            if not t.get("success"):
                return t

            resp = {
                "success": True,
                "source_video": str(video_path),
                "audio_size_mb": a.get("size_mb"),
                "model_used": model_size,
                "device": _WHISPER_DEVICE,
                "transcription": t
            }

            if keep_audio and save_dir:
                # Save audio in transcription subdirectory
                base_dir = Path(save_dir)
                transcription_dir = base_dir / "transcription" 
                final_audio = transcription_dir / (Path(video_path).stem + ".wav")
                final_audio.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(audio_path, final_audio)
                resp["kept_audio_path"] = str(final_audio)

            return resp
    except Exception as e:
        return {"success": False, "error": f"Video processing failed: {e}"}

# ──────────────────────────────────────────────────────────────────────────────
def get_server_status() -> Dict[str, Any]:
    return {
        "server": "Transcription Server",
        "status": "running",
        "whisper_loaded": _WHISPER_MODEL is not None,
        "device": _WHISPER_DEVICE,
        "available_models": ["tiny","base","small","medium","large","large-v3"],
        "supported_languages": ["auto","es","en"],
        "ffmpeg_available": _ensure_ffmpeg()
    }

# ──────────────────────────────────────────────────────────────────────────────
# Register functions as MCP tools (without overwriting function names)
mcp.tool()(extract_audio_from_video)
mcp.tool()(transcribe_audio) 
mcp.tool()(process_video_to_transcript)
mcp.tool()(get_server_status)

# ──────────────────────────────────────────────────────────────────────────────
def main():
    log.info("🚀 Starting Transcription MCP Server")
    log.info("📋 Registered tools: extract_audio_from_video, transcribe_audio, process_video_to_transcript, get_server_status")
    # Important: actually start the MCP server!
    mcp.run()

if __name__ == "__main__":
    main()
