using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Win32;
using System.Linq; // Added for Average()
using System.Globalization; // Added for NumberStyles

namespace BackgroundVideoWinForms
{
    public partial class Form1 : Form
    {
        private PexelsService pexelsService = new PexelsService();
        private VideoDownloader videoDownloader = new VideoDownloader();
        private VideoNormalizer videoNormalizer = new VideoNormalizer();
        private VideoConcatenator videoConcatenator = new VideoConcatenator();
        
        // Cancellation support
        private CancellationTokenSource cancellationTokenSource;
        private bool isProcessing = false;
        
        // Progress tracking
        private double totalDuration = 0;

        public Form1()
        {
            InitializeComponent();
            labelStatus.Text = "";
            
            // Log system information at startup
            Logger.LogSystemInfo();
            Logger.LogMemoryUsage();
            
            // Initialize FFmpeg paths
            InitializeFFmpegPaths();
            
            // Load all settings from registry
            LoadSettingsFromRegistry();
            
            // Set up event handlers for saving settings
            textBoxApiKey.Leave += textBoxApiKey_Leave;
            textBoxSearch.Leave += textBoxSearch_Leave;
            trackBarDuration.ValueChanged += trackBarDuration_ValueChanged;
            radioButton1080p.CheckedChanged += radioButtonResolution_CheckedChanged;
            radioButton4k.CheckedChanged += radioButtonResolution_CheckedChanged;
            radioButtonHorizontal.CheckedChanged += radioButtonAspectRatio_CheckedChanged;
            radioButtonVertical.CheckedChanged += radioButtonAspectRatio_CheckedChanged;
            
            // Set up form closing event to save window position
            this.FormClosing += Form1_FormClosing;
            
            Logger.LogPipelineStep("Application Initialization", "Form loaded and settings loaded from registry");
        }

        private void InitializeFFmpegPaths()
        {
            // Load custom FFmpeg paths from registry if set
            string customFFmpegPath = RegistryHelper.LoadFFmpegPath();
            string customFFprobePath = RegistryHelper.LoadFFprobePath();
            
            if (!string.IsNullOrEmpty(customFFmpegPath))
            {
                FFmpegPathManager.SetCustomFFmpegPath(customFFmpegPath, customFFprobePath);
            }
            
            // Validate FFmpeg installation
            if (!FFmpegPathManager.ValidateFFmpegInstallation())
            {
                Logger.LogWarning("FFmpeg installation validation failed. Users may need to configure paths in settings.");
            }
        }

        private const string PEXELS_API_URL = "https://api.pexels.com/videos/search";
        private const string REGISTRY_PATH = @"Software\\BackgroundVideoWinForms";
        private const string REGISTRY_APIKEY = "PexelsApiKey";

        private async void buttonStart_Click(object sender, EventArgs e)
        {
            if (isProcessing)
            {
                MessageBox.Show("Processing is already in progress. Please wait or cancel the current operation.");
                return;
            }

            // Validate FFmpeg installation before starting
            if (!FFmpegPathManager.ValidateFFmpegInstallation())
            {
                var result = MessageBox.Show(
                    "FFmpeg is not properly configured. Would you like to open settings to configure FFmpeg paths?",
                    "FFmpeg Configuration Required",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                
                if (result == DialogResult.Yes)
                {
                    using (var settingsForm = new SettingsForm())
                    {
                        settingsForm.ShowDialog();
                    }
                }
                return;
            }

            var pipelineStopwatch = Stopwatch.StartNew();
            Logger.LogPipelineStep("Pipeline Start", "User initiated video generation");
            Logger.LogMemoryUsage();
            
            // Initialize cancellation token
            cancellationTokenSource = new CancellationTokenSource();
            isProcessing = true;
            
            // Reset progress tracking variables
            totalDuration = 0;
            Logger.LogDebug("Reset progress tracking variables for new encoding session");
            
            // Update UI
            buttonStart.Enabled = false;
            buttonCancel.Enabled = true;
            
            // Save current settings before processing
            SaveSettingsToRegistry();
            
            string searchTerm = textBoxSearch.Text.Trim();
            int duration = trackBarDuration.Value * 60; // minutes to seconds
            string resolution = GetResolutionString();
            string apiKey = textBoxApiKey.Text.Trim();

            Logger.LogInfo($"Pipeline Parameters - Search: '{searchTerm}', Duration: {duration}s, Resolution: {resolution}");

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Logger.LogError("No API key entered");
                MessageBox.Show("Please enter your Pexels API key.");
                ResetUI();
                return;
            }
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                Logger.LogError("No search term entered");
                MessageBox.Show("Please enter a search term.");
                ResetUI();
                return;
            }

            progressBar.Style = ProgressBarStyle.Marquee;
            labelStatus.Text = "Searching Pexels...";
            buttonStart.Enabled = false;

            List<string> downloadedFiles = null;
            string outputFilePath = string.Empty;

            try
            {
                // 1. Query Pexels API for video clips matching searchTerm
                var searchStopwatch = Stopwatch.StartNew();
                Logger.LogPipelineStep("API Search", $"Searching for '{searchTerm}' with duration {duration}s and resolution {resolution}");
                Logger.LogApiCall("Pexels Search", $