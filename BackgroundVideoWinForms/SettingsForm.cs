using System;
using System.Windows.Forms;
using System.IO;

namespace BackgroundVideoWinForms
{
    public partial class SettingsForm : Form
    {
        public SettingsForm()
        {
            InitializeComponent();
            LoadSettings();
        }

        private void InitializeComponent()
        {
            this.Text = "Settings";
            this.Size = new System.Drawing.Size(500, 400);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            // Create controls
            var lblFFmpeg = new Label
            {
                Text = "FFmpeg Path:",
                Location = new System.Drawing.Point(20, 20),
                Size = new System.Drawing.Size(100, 20)
            };

            var txtFFmpeg = new TextBox
            {
                Name = "txtFFmpeg",
                Location = new System.Drawing.Point(130, 20),
                Size = new System.Drawing.Size(250, 20)
            };

            var btnBrowseFFmpeg = new Button
            {
                Text = "Browse...",
                Location = new System.Drawing.Point(390, 18),
                Size = new System.Drawing.Size(75, 25)
            };
            btnBrowseFFmpeg.Click += (s, e) => BrowseForFile(txtFFmpeg, "FFmpeg executable|ffmpeg.exe|All files|*.*");

            var lblFFprobe = new Label
            {
                Text = "FFprobe Path:",
                Location = new System.Drawing.Point(20, 50),
                Size = new System.Drawing.Size(100, 20)
            };

            var txtFFprobe = new TextBox
            {
                Name = "txtFFprobe",
                Location = new System.Drawing.Point(130, 50),
                Size = new System.Drawing.Size(250, 20)
            };

            var btnBrowseFFprobe = new Button
            {
                Text = "Browse...",
                Location = new System.Drawing.Point(390, 48),
                Size = new System.Drawing.Size(75, 25)
            };
            btnBrowseFFprobe.Click += (s, e) => BrowseForFile(txtFFprobe, "FFprobe executable|ffprobe.exe|All files|*.*");

            var btnAutoDetect = new Button
            {
                Text = "Auto-Detect FFmpeg",
                Location = new System.Drawing.Point(130, 80),
                Size = new System.Drawing.Size(120, 25)
            };
            btnAutoDetect.Click += BtnAutoDetect_Click;

            var btnTest = new Button
            {
                Text = "Test FFmpeg Installation",
                Location = new System.Drawing.Point(260, 80),
                Size = new System.Drawing.Size(140, 25)
            };
            btnTest.Click += BtnTest_Click;

            var btnOK = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new System.Drawing.Point(300, 320),
                Size = new System.Drawing.Size(75, 25)
            };
            btnOK.Click += BtnOK_Click;

            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new System.Drawing.Point(390, 320),
                Size = new System.Drawing.Size(75, 25)
            };

            var lblStatus = new Label
            {
                Name = "lblStatus",
                Location = new System.Drawing.Point(20, 120),
                Size = new System.Drawing.Size(450, 60),
                AutoSize = false
            };

            // Add controls to form
            this.Controls.AddRange(new Control[] {
                lblFFmpeg, txtFFmpeg, btnBrowseFFmpeg,
                lblFFprobe, txtFFprobe, btnBrowseFFprobe,
                btnAutoDetect, btnTest, btnOK, btnCancel, lblStatus
            });

            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;
        }

        private void LoadSettings()
        {
            var txtFFmpeg = (TextBox)Controls.Find("txtFFmpeg", true)[0];
            var txtFFprobe = (TextBox)Controls.Find("txtFFprobe", true)[0];

            txtFFmpeg.Text = RegistryHelper.LoadFFmpegPath();
            txtFFprobe.Text = RegistryHelper.LoadFFprobePath();

            // If no custom paths are set, show current detected paths
            if (string.IsNullOrEmpty(txtFFmpeg.Text))
            {
                txtFFmpeg.Text = FFmpegPathManager.FFmpegPath ?? "";
            }
            if (string.IsNullOrEmpty(txtFFprobe.Text))
            {
                txtFFprobe.Text = FFmpegPathManager.FFprobePath ?? "";
            }
        }

        private void BrowseForFile(TextBox textBox, string filter)
        {
            using (var openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = filter;
                openFileDialog.Title = "Select executable file";
                
                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    textBox.Text = openFileDialog.FileName;
                }
            }
        }

        private void BtnAutoDetect_Click(object sender, EventArgs e)
        {
            var txtFFmpeg = (TextBox)Controls.Find("txtFFmpeg", true)[0];
            var txtFFprobe = (TextBox)Controls.Find("txtFFprobe", true)[0];
            var lblStatus = (Label)Controls.Find("lblStatus", true)[0];

            // Reset FFmpegPathManager to force re-detection
            FFmpegPathManager.SetCustomFFmpegPath("", "");

            txtFFmpeg.Text = FFmpegPathManager.FFmpegPath ?? "";
            txtFFprobe.Text = FFmpegPathManager.FFprobePath ?? "";

            if (!string.IsNullOrEmpty(txtFFmpeg.Text))
            {
                lblStatus.Text = "FFmpeg auto-detection successful!";
                lblStatus.ForeColor = System.Drawing.Color.Green;
            }
            else
            {
                lblStatus.Text = "FFmpeg not found. Please install FFmpeg or specify the path manually.";
                lblStatus.ForeColor = System.Drawing.Color.Red;
            }
        }

        private void BtnTest_Click(object sender, EventArgs e)
        {
            var txtFFmpeg = (TextBox)Controls.Find("txtFFmpeg", true)[0];
            var txtFFprobe = (TextBox)Controls.Find("txtFFprobe", true)[0];
            var lblStatus = (Label)Controls.Find("lblStatus", true)[0];

            // Set custom paths for testing
            FFmpegPathManager.SetCustomFFmpegPath(txtFFmpeg.Text, txtFFprobe.Text);

            if (FFmpegPathManager.ValidateFFmpegInstallation())
            {
                lblStatus.Text = "FFmpeg installation test successful!";
                lblStatus.ForeColor = System.Drawing.Color.Green;
            }
            else
            {
                lblStatus.Text = "FFmpeg installation test failed. Please check the paths and ensure FFmpeg is properly installed.";
                lblStatus.ForeColor = System.Drawing.Color.Red;
            }
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            var txtFFmpeg = (TextBox)Controls.Find("txtFFmpeg", true)[0];
            var txtFFprobe = (TextBox)Controls.Find("txtFFprobe", true)[0];

            // Save settings
            RegistryHelper.SaveFFmpegPath(txtFFmpeg.Text);
            RegistryHelper.SaveFFprobePath(txtFFprobe.Text);

            // Update FFmpegPathManager with new paths
            FFmpegPathManager.SetCustomFFmpegPath(txtFFmpeg.Text, txtFFprobe.Text);

            Logger.LogInfo("Settings saved and FFmpeg paths updated");
        }
    }
} 