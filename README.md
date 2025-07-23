# BackgroundVideoWinForms

A C# WinForms application for concatenating and processing videos using ffmpeg, with real-time progress feedback.

## Features
- Select multiple video files to concatenate
- Choose output resolution (480p, 720p, 1080p)
- Save output to a chosen file
- Progress bar and status label update in real time by parsing ffmpeg output

## Requirements
- .NET 6.0 or later (WinForms)
- ffmpeg.exe available in your PATH or in the application directory

## Usage
1. Launch the application.
2. Click "Add Videos" to select video files to concatenate.
3. Choose the desired output resolution.
4. Click "Browse..." to select the output file location.
5. Click "Start" to begin processing.
6. Watch the progress bar and status label for real-time updates.

---

**Note:** This app uses ffmpeg via command line. Ensure ffmpeg is installed and accessible. 