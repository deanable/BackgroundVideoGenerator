namespace BackgroundVideoWinForms
{
    partial class Form1
    {
        private System.ComponentModel.IContainer components = null;
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            labelSearch = new System.Windows.Forms.Label();
            textBoxSearch = new System.Windows.Forms.TextBox();
            labelDuration = new System.Windows.Forms.Label();
            trackBarDuration = new System.Windows.Forms.TrackBar();
            groupBoxResolution = new System.Windows.Forms.GroupBox();
            radioButton1080p = new System.Windows.Forms.RadioButton();
            radioButton720p = new System.Windows.Forms.RadioButton();
            radioButton480p = new System.Windows.Forms.RadioButton();
            buttonStart = new System.Windows.Forms.Button();
            progressBar = new System.Windows.Forms.ProgressBar();
            labelStatus = new System.Windows.Forms.Label();
            labelApiKey = new System.Windows.Forms.Label();
            textBoxApiKey = new System.Windows.Forms.TextBox();
            ((System.ComponentModel.ISupportInitialize)(trackBarDuration)).BeginInit();
            groupBoxResolution.SuspendLayout();
            SuspendLayout();
            // 
            // labelApiKey
            // 
            labelApiKey.AutoSize = true;
            labelApiKey.Location = new System.Drawing.Point(12, 45);
            labelApiKey.Name = "labelApiKey";
            labelApiKey.Size = new System.Drawing.Size(87, 15);
            labelApiKey.TabIndex = 1;
            labelApiKey.Text = "Pexels API Key:";
            // 
            // textBoxApiKey
            // 
            textBoxApiKey.Location = new System.Drawing.Point(105, 42);
            textBoxApiKey.Name = "textBoxApiKey";
            textBoxApiKey.Size = new System.Drawing.Size(237, 23);
            textBoxApiKey.TabIndex = 2;
            // 
            // labelSearch
            // 
            labelSearch.AutoSize = true;
            labelSearch.Location = new System.Drawing.Point(12, 15);
            labelSearch.Name = "labelSearch";
            labelSearch.Size = new System.Drawing.Size(74, 15);
            labelSearch.TabIndex = 0;
            labelSearch.Text = "Search Term:";
            // 
            // textBoxSearch
            // 
            textBoxSearch.Location = new System.Drawing.Point(92, 12);
            textBoxSearch.Name = "textBoxSearch";
            textBoxSearch.Size = new System.Drawing.Size(250, 23);
            textBoxSearch.TabIndex = 3;
            // 
            // labelDuration
            // 
            labelDuration.AutoSize = true;
            labelDuration.Location = new System.Drawing.Point(12, 80);
            labelDuration.Name = "labelDuration";
            labelDuration.Size = new System.Drawing.Size(110, 15);
            labelDuration.TabIndex = 4;
            labelDuration.Text = "Duration: 1 minute";
            // 
            // trackBarDuration
            // 
            trackBarDuration.Location = new System.Drawing.Point(12, 100);
            trackBarDuration.Minimum = 1;
            trackBarDuration.Maximum = 15;
            trackBarDuration.TickFrequency = 1;
            trackBarDuration.Value = 1;
            trackBarDuration.Name = "trackBarDuration";
            trackBarDuration.Size = new System.Drawing.Size(330, 45);
            trackBarDuration.TabIndex = 5;
            trackBarDuration.Scroll += (sender, e) => {
                labelDuration.Text = $"Duration: {trackBarDuration.Value} minute{(trackBarDuration.Value == 1 ? "" : "s")}";
            };
            // 
            // groupBoxResolution
            // 
            groupBoxResolution.Controls.Add(radioButton1080p);
            groupBoxResolution.Controls.Add(radioButton720p);
            groupBoxResolution.Controls.Add(radioButton480p);
            groupBoxResolution.Location = new System.Drawing.Point(12, 120);
            groupBoxResolution.Name = "groupBoxResolution";
            groupBoxResolution.Size = new System.Drawing.Size(200, 83);
            groupBoxResolution.TabIndex = 4;
            groupBoxResolution.TabStop = false;
            groupBoxResolution.Text = "Output Resolution";
            // 
            // radioButton1080p
            // 
            radioButton1080p.AutoSize = true;
            radioButton1080p.Location = new System.Drawing.Point(6, 62);
            radioButton1080p.Name = "radioButton1080p";
            radioButton1080p.Size = new System.Drawing.Size(56, 19);
            radioButton1080p.TabIndex = 2;
            radioButton1080p.Text = "1080p";
            radioButton1080p.UseVisualStyleBackColor = true;
            // 
            // radioButton720p
            // 
            radioButton720p.AutoSize = true;
            radioButton720p.Checked = true;
            radioButton720p.Location = new System.Drawing.Point(6, 42);
            radioButton720p.Name = "radioButton720p";
            radioButton720p.Size = new System.Drawing.Size(50, 19);
            radioButton720p.TabIndex = 1;
            radioButton720p.TabStop = true;
            radioButton720p.Text = "720p";
            radioButton720p.UseVisualStyleBackColor = true;
            // 
            // radioButton480p
            // 
            radioButton480p.AutoSize = true;
            radioButton480p.Location = new System.Drawing.Point(6, 22);
            radioButton480p.Name = "radioButton480p";
            radioButton480p.Size = new System.Drawing.Size(50, 19);
            radioButton480p.TabIndex = 0;
            radioButton480p.Text = "480p";
            radioButton480p.UseVisualStyleBackColor = true;
            // 
            // buttonStart
            // 
            buttonStart.Location = new System.Drawing.Point(12, 220);
            buttonStart.Name = "buttonStart";
            buttonStart.Size = new System.Drawing.Size(330, 30);
            buttonStart.TabIndex = 5;
            buttonStart.Text = "Search and Generate";
            buttonStart.UseVisualStyleBackColor = true;
            buttonStart.Click += buttonStart_Click;
            // 
            // progressBar
            // 
            progressBar.Location = new System.Drawing.Point(12, 260);
            progressBar.Name = "progressBar";
            progressBar.Size = new System.Drawing.Size(330, 23);
            progressBar.TabIndex = 6;
            // 
            // labelStatus
            // 
            labelStatus.AutoSize = true;
            labelStatus.Location = new System.Drawing.Point(12, 290);
            labelStatus.Name = "labelStatus";
            labelStatus.Size = new System.Drawing.Size(0, 15);
            labelStatus.TabIndex = 7;
            // 
            // Form1
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(360, 320);
            Controls.Add(labelStatus);
            Controls.Add(progressBar);
            Controls.Add(buttonStart);
            Controls.Add(groupBoxResolution);
            Controls.Add(trackBarDuration);
            Controls.Add(labelDuration);
            Controls.Add(textBoxSearch);
            Controls.Add(labelSearch);
            Controls.Add(labelApiKey);
            Controls.Add(textBoxApiKey);
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            Name = "Form1";
            Text = "Background Video Creator (Pexels)";
            ((System.ComponentModel.ISupportInitialize)(trackBarDuration)).EndInit();
            groupBoxResolution.ResumeLayout(false);
            groupBoxResolution.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        private System.Windows.Forms.Label labelSearch;
        private System.Windows.Forms.TextBox textBoxSearch;
        private System.Windows.Forms.Label labelDuration;
        private System.Windows.Forms.TrackBar trackBarDuration;
        private System.Windows.Forms.GroupBox groupBoxResolution;
        private System.Windows.Forms.RadioButton radioButton1080p;
        private System.Windows.Forms.RadioButton radioButton720p;
        private System.Windows.Forms.RadioButton radioButton480p;
        private System.Windows.Forms.Button buttonStart;
        private System.Windows.Forms.ProgressBar progressBar;
        private System.Windows.Forms.Label labelStatus;
        private System.Windows.Forms.Label labelApiKey;
        private System.Windows.Forms.TextBox textBoxApiKey;
    }
} 