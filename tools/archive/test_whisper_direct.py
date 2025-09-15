#!/usr/bin/env python3
"""
Test directo de Whisper con setup manual del PATH
"""
import os, sys, shutil, tempfile
from pathlib import Path

# 1) Setup FFmpeg PATH antes de importar Whisper
try:
    from imageio_ffmpeg import get_ffmpeg_exe
    ffmpeg_path = get_ffmpeg_exe()
    if ffmpeg_path and Path(ffmpeg_path).exists():
        ff_dir = str(Path(ffmpeg_path).parent)
        current_path = os.environ.get("PATH", "")
        if ff_dir not in current_path:
            os.environ["PATH"] = ff_dir + os.pathsep + current_path
            print(f"✅ Added FFmpeg to PATH: {ff_dir}")
        
        # Crear link/alias sin extensión si no existe
        ffmpeg_no_ext = Path(ff_dir) / "ffmpeg"
        if not ffmpeg_no_ext.exists():
            try:
                # En Windows, crear batch file que llame al ejecutable
                ffmpeg_bat = Path(ff_dir) / "ffmpeg.bat"
                ffmpeg_bat.write_text(f'@echo off\n"{ffmpeg_path}" %*\n')
                print(f"✅ Created ffmpeg.bat: {ffmpeg_bat}")
            except Exception as e:
                print(f"⚠️ Could not create ffmpeg.bat: {e}")
        
        # Verificar que ffmpeg es encontrable
        ffmpeg_found = shutil.which("ffmpeg")
        print(f"✅ FFmpeg findable via 'ffmpeg': {ffmpeg_found}")
    else:
        print("❌ imageio-ffmpeg not found")
        sys.exit(1)
except Exception as e:
    print(f"❌ Error setting up FFmpeg: {e}")
    sys.exit(1)

# 2) Ahora importar Whisper
try:
    import whisper
    print("✅ Whisper imported")
except Exception as e:
    print(f"❌ Whisper import failed: {e}")
    sys.exit(1)

# 3) Test con tu video real
video_path = r".\lessons\18-practice-08\sersans.mkv"
if not Path(video_path).exists():
    print(f"❌ Video not found: {video_path}")
    sys.exit(1)

print(f"🎬 Testing transcription: {Path(video_path).name}")

try:
    # Cargar modelo pequeño
    print("🔄 Loading Whisper small model...")
    model = whisper.load_model("small")
    
    # Transcribir
    print("🎤 Transcribing (this may take a few minutes)...")
    result = model.transcribe(str(video_path), language="es")
    
    # Resultados
    print("✅ TRANSCRIPTION SUCCESS!")
    print(f"Language detected: {result.get('language')}")
    text = result.get('text', '').strip()
    print(f"Text length: {len(text)} characters")
    print("\n📖 Preview (first 300 chars):")
    print("-" * 40)
    print(text[:300] + "..." if len(text) > 300 else text)
    print("-" * 40)
    
    # Guardar resultado
    output_file = Path("./lessons/18-practice-08/transcript_test.txt")
    output_file.write_text(text, encoding="utf-8")
    print(f"\n💾 Saved to: {output_file}")
    
except Exception as e:
    print(f"❌ Transcription failed: {e}")
    import traceback
    traceback.print_exc()