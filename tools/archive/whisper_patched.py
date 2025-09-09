#!/usr/bin/env python3
"""
Whisper patched para usar FFmpeg con ruta completa
Soluciona el problema de PATH en Windows
"""
import os
import numpy as np
import torch
import whisper
from whisper.audio import SAMPLE_RATE
from pathlib import Path
import subprocess
from typing import Union

# Detectar FFmpeg
_FFMPEG_PATH = None

def get_ffmpeg_path():
    """Obtener ruta completa de FFmpeg"""
    global _FFMPEG_PATH
    if _FFMPEG_PATH is None:
        try:
            from imageio_ffmpeg import get_ffmpeg_exe
            _FFMPEG_PATH = get_ffmpeg_exe()
        except ImportError:
            _FFMPEG_PATH = "ffmpeg"  # fallback
    return _FFMPEG_PATH

def load_audio_patched(file: Union[str, Path], sr: int = SAMPLE_RATE):
    """
    Version patcheada de whisper.audio.load_audio que usa ruta completa de FFmpeg
    """
    ffmpeg_path = get_ffmpeg_path()
    
    cmd = [
        ffmpeg_path,  # Usar ruta completa en lugar de solo "ffmpeg"
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

# Parchear la función original
whisper.audio.load_audio = load_audio_patched

def transcribe_with_patched_audio(model, audio_path: str, **kwargs):
    """Transcribir usando la función de audio patcheada"""
    return model.transcribe(str(audio_path), **kwargs)

if __name__ == "__main__":
    # Test del patch
    print(f"🔧 Using FFmpeg: {get_ffmpeg_path()}")
    
    video_path = r".\lessons\18-practice-08\sersans.mkv"
    
    if Path(video_path).exists():
        print("🔄 Loading Whisper small model...")
        model = whisper.load_model("small")
        
        print("🎤 Transcribing with patched audio loader...")
        try:
            result = transcribe_with_patched_audio(
                model, 
                video_path,
                language="es",
                verbose=False
            )
            
            print("✅ TRANSCRIPTION SUCCESS!")
            print(f"Language: {result['language']}")
            text = result['text'].strip()
            print(f"Length: {len(text)} characters")
            print("\n📖 Preview:")
            print("-" * 40)
            print(text[:400] + "..." if len(text) > 400 else text)
            print("-" * 40)
            
            # Guardar
            output_file = Path("./lessons/18-practice-08/transcript_patched.txt")
            output_file.write_text(text, encoding="utf-8")
            print(f"\n💾 Saved to: {output_file}")
            
        except Exception as e:
            print(f"❌ Transcription failed: {e}")
            import traceback
            traceback.print_exc()
    else:
        print(f"❌ Video not found: {video_path}")