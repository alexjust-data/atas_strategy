#!/usr/bin/env python3
"""
Diagnóstico de Whisper - Debug las dependencias y PATH
"""
import os, sys, shutil, subprocess
from pathlib import Path

print("🔍 DIAGNÓSTICO WHISPER")
print("=" * 50)

# 1) Verificar FFmpeg
print("\n1️⃣ FFmpeg Detection:")
try:
    from imageio_ffmpeg import get_ffmpeg_exe
    ffmpeg_path = get_ffmpeg_exe()
    print(f"   imageio-ffmpeg path: {ffmpeg_path}")
    print(f"   File exists: {Path(ffmpeg_path).exists()}")
    
    # Test direct execution
    result = subprocess.run([ffmpeg_path, "-version"], 
                          capture_output=True, text=True, timeout=5)
    if result.returncode == 0:
        print("   ✅ FFmpeg executable works")
        print(f"   Version: {result.stdout.split()[2]}")
    else:
        print(f"   ❌ FFmpeg failed: {result.stderr[:200]}")
        
except Exception as e:
    print(f"   ❌ Error: {e}")

# 2) PATH analysis
print("\n2️⃣ PATH Analysis:")
path_dirs = os.environ.get("PATH", "").split(os.pathsep)
ffmpeg_dirs = [d for d in path_dirs if "ffmpeg" in d.lower()]
print(f"   FFmpeg dirs in PATH: {len(ffmpeg_dirs)}")
for d in ffmpeg_dirs:
    print(f"     • {d}")

# 3) Test basic Whisper import
print("\n3️⃣ Whisper Import:")
try:
    import whisper
    print("   ✅ Whisper imported successfully")
    print(f"   Whisper location: {whisper.__file__}")
except Exception as e:
    print(f"   ❌ Whisper import failed: {e}")

# 4) Test minimal transcription
print("\n4️⃣ Minimal Transcription Test:")
try:
    import whisper
    import tempfile
    
    # Create tiny test audio (silence)
    with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as f:
        # Write minimal WAV header + silence
        wav_header = b'RIFF$\x00\x00\x00WAVEfmt \x10\x00\x00\x00\x01\x00\x01\x00D\xac\x00\x00\x88X\x01\x00\x02\x00\x10\x00data\x00\x00\x00\x00'
        f.write(wav_header)
        test_audio = f.name
    
    print(f"   Test audio: {test_audio}")
    
    # Load tiny model and transcribe
    model = whisper.load_model("tiny")
    result = model.transcribe(test_audio)
    print("   ✅ Minimal transcription successful")
    print(f"   Result: '{result['text']}'")
    
    # Cleanup
    Path(test_audio).unlink(missing_ok=True)
    
except Exception as e:
    print(f"   ❌ Minimal transcription failed: {e}")
    import traceback
    traceback.print_exc()

# 5) Environment variables
print("\n5️⃣ Relevant Environment:")
env_vars = ['PATH', 'FFMPEG_BIN', 'TEMP', 'TMP']
for var in env_vars:
    value = os.environ.get(var, "Not set")
    if var == 'PATH':
        print(f"   {var}: {len(value)} characters")
    else:
        print(f"   {var}: {value}")

print("\n" + "=" * 50)
print("🎯 Run this script and share the output for diagnosis")