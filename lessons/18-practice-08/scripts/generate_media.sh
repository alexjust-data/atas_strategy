#!/bin/bash
cd ..
echo "Generating media for Practice 08..."
echo "Creating 1 screenshots and 1 video clips"

echo "Generating screenshots..."
ffmpeg -ss 00:05:11 -i "C:\Users\AlexJ\Desktop\projects\curso-trading-algoritmico\lessons\18-practice-08\source\sersans.mkv" -vframes 1 -q:v 2 -y "C:\Users\AlexJ\Desktop\projects\curso-trading-algoritmico\lessons\18-practice-08\media\shots\shot_01_example_311s.png"

echo "Generating video clips..."
ffmpeg -ss 00:05:06 -i "C:\Users\AlexJ\Desktop\projects\curso-trading-algoritmico\lessons\18-practice-08\source\sersans.mkv" -t 15 -c:v libx264 -crf 18 -preset medium -c:a aac -b:a 192k -vf "fade=in:0:15,fade=out:435:15" -y "C:\Users\AlexJ\Desktop\projects\curso-trading-algoritmico\lessons\18-practice-08\media\clips\clip_01_example_311s.mp4"

echo "Media generation completed!"
echo "Check media/shots/ for screenshots and media/clips/ for video clips"
