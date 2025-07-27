# Background Video Generator

A Windows Forms application that creates background videos by downloading and concatenating clips from Pexels.

## Features

- Search and download video clips from Pexels API
- Automatic video normalization and concatenation
- Support for 1080p and 4K resolutions
- Horizontal and vertical aspect ratios
- Configurable video duration
- Advanced logging system for debugging

## Advanced Logging System

The application now includes a comprehensive logging system that creates detailed logs for debugging the video creation pipeline.

### Log File Location

Log files are stored in the `logs` folder within the application directory:
```
BackgroundVideoWinForms/logs/
```

### Log File Naming

Each application run creates a new log file with a timestamp:
```
log-YYYY-MM-DD_HH-mm-ss.log
```

Example: `log-2024-01-15_14-30-25.log`

### Log Levels

The logging system supports multiple log levels:

- **DEBUG**: Detailed debugging information
- **INFO**: General information about operations
- **WARNING**: Warning messages for non-critical issues
- **ERROR**: Error messages and exceptions

### Log Categories

The logging system tracks different aspects of the video creation pipeline:

#### Pipeline Steps
- Application initialization
- API search operations
- Download and normalization phases
- Video concatenation
- Cleanup operations

#### Performance Metrics
- API call response times
- Download speeds for individual clips
- Normalization processing times
- Overall pipeline completion time
- Memory usage tracking

#### File Operations
- File downloads with sizes
- File deletions during cleanup
- Final video creation with file size

#### API Calls
- Pexels API requests and responses
- Success/failure status
- Search parameters

#### FFmpeg Commands
- All FFmpeg and ffprobe commands executed
- Input and output file information
- Command parameters

#### System Information
- Operating system details
- .NET Framework version
- Processor count
- Available memory
- Application version

### Log Format

Each log entry includes:
```
[Timestamp] [Session Time] [Level] Message
```

Example:
```
[2024-01-15 14:30:25.123] [00:00:05.456] [INFO   ] PIPELINE STEP: API Search - Searching for 'nature' with duration 300s and resolution 1920x1080
[2024-01-15 14:30:26.789] [00:00:06.789] [PERF   ] PERFORMANCE: Pexels API Search completed in 1654ms - Found 25 clips
[2024-01-15 14:30:27.012] [00:00:07.012] [DEBUG  ] FILE OPERATION: Downloaded - clip_0.mp4 (15.2 MB)
```

### Debugging with Logs

When troubleshooting issues:

1. **Check the latest log file** in the `logs` folder
2. **Look for ERROR level messages** to identify problems
3. **Review PERFORMANCE entries** to identify bottlenecks
4. **Check FILE OPERATION entries** to verify file processing
5. **Examine FFMPEG entries** for command-line issues

### Log File Management

- Log files are created automatically for each application run
- Old log files are not automatically deleted
- Consider cleaning up old logs periodically to save disk space
- Log files contain sensitive information (API keys, file paths) - handle with care

## Requirements

- Windows 10 or later
- .NET 9.0 or later
- FFmpeg installed and accessible in PATH
- Pexels API key

## Installation

1. Clone or download the repository
2. Install FFmpeg and ensure it's in your system PATH
3. Build the solution using Visual Studio or `dotnet build`
4. Run the application and enter your Pexels API key

## Usage

1. Enter your Pexels API key
2. Enter a search term for video clips
3. Adjust duration, resolution, and aspect ratio settings
4. Click "Start" to begin video generation
5. Monitor progress in the status bar
6. Check logs in the `logs` folder for detailed information

## Configuration

Settings are automatically saved to the Windows Registry and restored on application startup:
- API key
- Search term
- Duration setting
- Resolution preference
- Aspect ratio preference
- Window position and size

## Troubleshooting

### Common Issues

1. **FFmpeg not found**: Ensure FFmpeg is installed and in PATH
2. **API key issues**: Verify your Pexels API key is valid
3. **No videos found**: Try different search terms
4. **Slow performance**: Check log files for performance bottlenecks

### Using Logs for Debugging

1. Run the application and reproduce the issue
2. Check the latest log file in the `logs` folder
3. Look for ERROR entries to identify the problem
4. Review the pipeline steps to understand where the issue occurred
5. Check system information for resource constraints

## License

This project is licensed under the MIT License. 