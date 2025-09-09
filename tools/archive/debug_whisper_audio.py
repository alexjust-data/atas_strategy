#!/usr/bin/env python3
"""
Debug específico del comando que falla en whisper.audio.load_audio
"""
import os, sys, subprocess, shutil
from pathlib import Path

# Setup FFmpeg PATH
try:
    from imageio_ffmpeg import get_ffmpeg_exe
    ffmpeg_path = get_ffmpeg_exe()
    ff_dir = str(Path(ffmpeg_path).parent)
    os.environ["PATH"] = ff_dir + os.pathsep + os.environ.get("PATH", "")
    
    # Crear ffmpeg.bat si no existe
    ffmpeg_bat = Path(ff_dir) / "ffmpeg.bat"
    if not ffmpeg_bat.exists():
        ffmpeg_bat.write_text(f'@echo off\n"{ffmpeg_path}" %*\n')
    
    print(f"✅ FFmpeg setup: {ffmpeg_path}")
    print(f"✅ Directory in PATH: {ff_dir}")
    print(f"✅ ffmpeg.bat created: {ffmpeg_bat}")
except Exception as e:
    print(f"❌ FFmpeg setup failed: {e}")
    sys.exit(1)

# Test que comando usa Whisper exactamente
print("\n🔍 Testing Whisper's exact command...")

# Recrear el comando que usa Whisper (desde whisper/audio.py)
video_path = r".\lessons\18-practice-08\sersans.mkv"

# Comando que usa Whisper internamente
cmd = [
    "ffmpeg",
    "-nostdin",
    "-threads", "0",
    "-i", str(video_path),
    "-f", "s16le",
    "-ac", "1",
    "-acodec", "pcm_s16le",
    "-ar", "16000",
    "-"
]

print("Command that Whisper tries to run:")
print(" ".join(cmd))
print()

# Test 1: Intentar ejecutar el comando tal como lo hace Whisper
print("1️⃣ Testing exact Whisper command...")
try:
    result = subprocess.run(cmd, capture_output=True, check=True, timeout=10)
    print(f"✅ Command succeeded! Output size: {len(result.stdout)} bytes")
except subprocess.TimeoutExpired:
    print("⏱️ Command timed out (normal for this test)")
except FileNotFoundError as e:
    print(f"❌ FileNotFoundError: {e}")
    print("   This is the exact error Whisper gets!")
except Exception as e:
    print(f"❌ Other error: {e}")

# Test 2: Usar ruta completa
print("\n2️⃣ Testing with full FFmpeg path...")
cmd_full = cmd.copy()
cmd_full[0] = ffmpeg_path
try:
    result = subprocess.run(cmd_full, capture_output=True, check=True, timeout=10)
    print(f"✅ Full path command succeeded! Output size: {len(result.stdout)} bytes")
except subprocess.TimeoutExpired:
    print("⏱️ Command timed out (normal - means it's working)")
except Exception as e:
    print(f"❌ Full path failed: {e}")

# Test 3: Usar ffmpeg.bat
print("\n3️⃣ Testing with ffmpeg.bat...")
cmd_bat = cmd.copy() 
cmd_bat[0] = str(ffmpeg_bat)
try:
    result = subprocess.run(cmd_bat, capture_output=True, check=True, timeout=10)
    print(f"✅ Batch file succeeded! Output size: {len(result.stdout)} bytes")
except subprocess.TimeoutExpired:
    print("⏱️ Batch command timed out (normal - means it's working)")
except Exception as e:
    print(f"❌ Batch file failed: {e}")

# Test 4: Verificación de PATH
print("\n4️⃣ PATH verification...")
ffmpeg_which = shutil.which("ffmpeg")
print(f"which('ffmpeg'): {ffmpeg_which}")

# Verificar si podemos ejecutar ffmpeg directamente
try:
    result = subprocess.run(["ffmpeg", "-version"], capture_output=True, check=True, timeout=5)
    print("✅ 'ffmpeg -version' works from PATH")
except Exception as e:
    print(f"❌ 'ffmpeg -version' failed: {e}")

print("\n" + "="*50)
print("🎯 If Test 2 or 3 work but Test 1 fails, we need to patch Whisper")