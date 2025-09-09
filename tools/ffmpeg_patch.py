#!/usr/bin/env python3
"""
FFmpeg Patch for Whisper on Windows
Fixes PATH issues by using full FFmpeg path
"""
import os
import shutil
import subprocess
import numpy as np
from pathlib import Path
from typing import Union
import whisper
from whisper.audio import SAMPLE_RATE

def _get_ffmpeg_path():
    """Get FFmpeg path with multiple fallback methods"""
    # 1) Environment variable override
    p = os.environ.get("FFMPEG_BIN")
    if p and Path(p).exists():
        return p
    
    # 2) imageio-ffmpeg (most common)
    try:
        from imageio_ffmpeg import get_ffmpeg_exe
        return get_ffmpeg_exe()
    except Exception:
        pass
    
    # 3) System PATH
    from shutil import which
    return which("ffmpeg") or "ffmpeg"

def _ensure_ffmpeg_on_path():
    """Ensure FFmpeg is accessible for subprocess calls"""
    ffmpeg_path = _get_ffmpeg_path()
    if not ffmpeg_path:
        return None
        
    ff = Path(ffmpeg_path)
    ff_dir = ff.parent
    
    # Add directory to PATH for child processes
    current_path = os.environ.get("PATH", "")
    if str(ff_dir) not in current_path:
        os.environ["PATH"] = str(ff_dir) + os.pathsep + current_path
    
    # On Windows, create ffmpeg.exe alias if needed
    if os.name == "nt":
        alias = ff_dir / "ffmpeg.exe"
        if ff.name.lower() != "ffmpeg.exe" and not alias.exists():
            try:
                shutil.copy2(ff, alias)
            except Exception:
                pass
    
    return str(ff)

def _load_audio_patched(file: Union[str, Path], sr: int = SAMPLE_RATE):
    """
    Patched version of whisper.audio.load_audio using full FFmpeg path
    """
    ffmpeg_path = _get_ffmpeg_path()
    
    cmd = [
        ffmpeg_path,  # Use full path instead of just "ffmpeg"
        "-nostdin",
        "-threads", "0",
        "-i", str(file),
        "-f", "s16le",
        "-ac", "1", 
        "-acodec", "pcm_s16le",
        "-ar", str(sr),
        "-"
    ]
    
    try:
        out = subprocess.run(cmd, capture_output=True, check=True).stdout
    except subprocess.CalledProcessError as e:
        raise RuntimeError(f"Failed to load audio: {e.stderr.decode()}") from e
    
    return np.frombuffer(out, np.int16).flatten().astype(np.float32) / 32768.0

def apply_patch():
    """Apply the FFmpeg patch to Whisper"""
    _ensure_ffmpeg_on_path()
    whisper.audio.load_audio = _load_audio_patched
    print(f"🔧 Applied FFmpeg patch: {_get_ffmpeg_path()}")

# Apply patch automatically on import
apply_patch()