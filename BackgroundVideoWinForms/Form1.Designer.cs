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
            radioButton4k = new System.Windows.Forms.RadioButton();
            groupBoxAspectRatio = new System.Windows.Forms.GroupBox();
            radioButtonHorizontal = new System.Windows.Forms.RadioButton();
            radioButtonVertical = new System.Windows.Forms.RadioButton();
            buttonStart = new System.Windows.Forms.Button();
            progressBar = new System.Windows.Forms.ProgressBar();
            labelStatus = new System.Windows.Forms.Label();
            labelApiKey = new System.Windows.Forms.Label();
            textBoxApiKey = new System.Windows.Forms.TextBox();
            ((System.ComponentModel.ISupportInitialize)trackBarDuration).BeginInit();
            groupBoxResolution.SuspendLayout();
            groupBoxAspectRatio.SuspendLayout();
            SuspendLayout();
            // 
            // labelSearch
            // 
            labelSearch.AutoSize = true;
            labelSearch.Location = new System.Drawing.Point(12, 15);
            labelSearch.Name = "labelSearch";
            labelSearch.Size = new System.Drawing.Size(75, 15);
            labelSearch.TabIndex = 0;
            labelSearch.Text = "Search Term:";
            // 
            // textBoxSearch
            // 
            textBoxSearch.Location = new System.Drawing.Point(92, 12);
            textBoxSearch.Name = "textBoxSearch";
            textBoxSearch.Size = new System.Drawing.Size(324, 23);
            textBoxSearch.TabIndex = 3;
            // 
            // labelDuration
            // 
            labelDuration.AutoSize = true;
            labelDuration.Location = new System.Drawing.Point(12, 80);
            labelDuration.Name = "labelDuration";
            labelDuration.Size = new System.Drawing.Size(106, 15);
            labelDuration.TabIndex = 4;
            labelDuration.Text = "Duration: 1 minute";
            // 
            // trackBarDuration
            // 
            trackBarDuration.Location = new System.Drawing.Point(12, 100);
            trackBarDuration.Maximum = 15;
            trackBarDuration.Minimum = 1;
            trackBarDuration.Name = "trackBarDuration";
            trackBarDuration.Size = new System.Drawing.Size(404, 45);
            trackBarDuration.TabIndex = 5;
            trackBarDuration.Value = 1;
            trackBarDuration.Scroll += trackBarDuration_Scroll;
            // 
            // groupBoxResolution
            // 
            groupBoxResolution.Controls.Add(radioButton1080p);
            groupBoxResolution.Controls.Add(radioButton4k);
            groupBoxResolution.Location = new System.Drawing.Point(12, 160);
            groupBoxResolution.Name = "groupBoxResolution";
            groupBoxResolution.Size = new System.Drawing.Size(200, 83);
            groupBoxResolution.TabIndex = 4;
            groupBoxResolution.TabStop = false;
            groupBoxResolution.Text = "Output Resolution";
            // 
            // radioButton1080p
            // 
            radioButton1080p.AutoSize = true;
            radioButton1080p.Checked = true;
            radioButton1080p.Location = new System.Drawing.Point(6, 22);
            radioButton1080p.Name = "radioButton1080p";
            radioButton1080p.Size = new System.Drawing.Size(56, 19);
            radioButton1080p.TabIndex = 0;
            radioButton1080p.TabStop = true;
            radioButton1080p.Text = "1080p";
            radioButton1080p.UseVisualStyleBackColor = true;
            // 
            // radioButton4k
            // 
            radioButton4k.AutoSize = true;
            radioButton4k.Location = new System.Drawing.Point(6, 47);
            radioButton4k.Name = "radioButton4k";
            radioButton4k.Size = new System.Drawing.Size(38, 19);
            radioButton4k.TabIndex = 1;
            radioButton4k.Text = "4K";
            radioButton4k.UseVisualStyleBackColor = true;
            // 
            // groupBoxAspectRatio
            // 
            groupBoxAspectRatio.Controls.Add(radioButtonHorizontal);
            groupBoxAspectRatio.Controls.Add(radioButtonVertical);
            groupBoxAspectRatio.Location = new System.Drawing.Point(218, 160);
            groupBoxAspectRatio.Name = "groupBoxAspectRatio";
            groupBoxAspectRatio.Size = new System.Drawing.Size(200, 83);
            groupBoxAspectRatio.TabIndex = 5;
            groupBoxAspectRatio.TabStop = false;
            groupBoxAspectRatio.Text = "Aspect Ratio";
            // 
            // radioButtonHorizontal
            // 
            radioButtonHorizontal.AutoSize = true;
            radioButtonHorizontal.Checked = true;
            radioButtonHorizontal.Location = new System.Drawing.Point(6, 22);
            radioButtonHorizontal.Name = "radioButtonHorizontal";
            radioButtonHorizontal.Size = new System.Drawing.Size(80, 19);
            radioButtonHorizontal.TabIndex = 0;
            radioButtonHorizontal.TabStop = true;
            radioButtonHorizontal.Text = "Horizontal";
            radioButtonHorizontal.UseVisualStyleBackColor = true;
            // 
            // radioButtonVertical
            // 
            radioButtonVertical.AutoSize = true;
            radioButtonVertical.Location = new System.Drawing.Point(6, 47);
            radioButtonVertical.Name = "radioButtonVertical";
            radioButtonVertical.Size = new System.Drawing.Size(63, 19);
            radioButtonVertical.TabIndex = 1;
            radioButtonVertical.Text = "Vertical";
            radioButtonVertical.UseVisualStyleBackColor = true;
            // 
            // buttonStart
            // 
            buttonStart.Location = new System.Drawing.Point(12, 249);
            buttonStart.Name = "buttonStart";
            buttonStart.Size = new System.Drawing.Size(200, 30);
            buttonStart.TabIndex = 5;
            buttonStart.Text = "Search and Generate";
            buttonStart.UseVisualStyleBackColor = true;
            buttonStart.Click += buttonStart_Click;
            // 
            // progressBar
            // 
            progressBar.Location = new System.Drawing.Point(218, 249);
            progressBar.Name = "progressBar";
            progressBar.Size = new System.Drawing.Size(200, 30);
            progressBar.TabIndex = 6;
            // 
            // labelStatus
            // 
            labelStatus.AutoSize = true;
            labelStatus.Location = new System.Drawing.Point(12, 285);
            labelStatus.Name = "labelStatus";
            labelStatus.Size = new System.Drawing.Size(0, 15);
            labelStatus.TabIndex = 7;
            // 
            // labelApiKey
            // 
            labelApiKey.AutoSize = true;
            labelApiKey.Location = new System.Drawing.Point(12, 45);
            labelApiKey.Name = "labelApiKey";
            labelApiKey.Size = new System.Drawing.Size(85, 15);
            labelApiKey.TabIndex = 1;
            labelApiKey.Text = "Pexels API Key:";
            // 
            // textBoxApiKey
            // 
            textBoxApiKey.Location = new System.Drawing.Point(105, 42);
            textBoxApiKey.Name = "textBoxApiKey";
            textBoxApiKey.Size = new System.Drawing.Size(311, 23);
            textBoxApiKey.TabIndex = 2;
            // 
            // Form1
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(428, 290);
            Controls.Add(labelStatus);
            Controls.Add(progressBar);
            Controls.Add(buttonStart);
            Controls.Add(groupBoxResolution);
            Controls.Add(groupBoxAspectRatio);
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
            ((System.ComponentModel.ISupportInitialize)trackBarDuration).EndInit();
            groupBoxResolution.ResumeLayout(false);
            groupBoxResolution.PerformLayout();
            groupBoxAspectRatio.ResumeLayout(false);
            groupBoxAspectRatio.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        private System.Windows.Forms.Label labelSearch;
        private System.Windows.Forms.TextBox textBoxSearch;
        private System.Windows.Forms.Label labelDuration;
        private System.Windows.Forms.TrackBar trackBarDuration;
        private System.Windows.Forms.GroupBox groupBoxResolution;
        private System.Windows.Forms.RadioButton radioButton1080p;
        private System.Windows.Forms.RadioButton radioButton4k;
        private System.Windows.Forms.GroupBox groupBoxAspectRatio;
        private System.Windows.Forms.RadioButton radioButtonHorizontal;
        private System.Windows.Forms.RadioButton radioButtonVertical;
        private System.Windows.Forms.Button buttonStart;
        private System.Windows.Forms.ProgressBar progressBar;
        private System.Windows.Forms.Label labelStatus;
        private System.Windows.Forms.Label labelApiKey;
        private System.Windows.Forms.TextBox textBoxApiKey;
    }
} 