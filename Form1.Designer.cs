namespace PlayerDBSync
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            lblFolder = new Label();
            txtFolder = new TextBox();
            btnBrowse = new Button();
            lblHomeFolder = new Label();
            txtHomeFolder = new TextBox();
            btnBrowseHome = new Button();
            lblRegradeFolder = new Label();
            txtRegradeFolder = new TextBox();
            btnBrowseRegrade = new Button();
            lblUrl = new Label();
            txtUrl = new TextBox();
            lblUserId = new Label();
            txtUserId = new TextBox();
            lblApiKey = new Label();
            txtApiKey = new TextBox();
            btnStart = new Button();
            lblStatus = new Label();
            txtLog = new TextBox();
            syncTimer = new System.Windows.Forms.Timer(components);
            homesTimer = new System.Windows.Forms.Timer(components);
            regradesTimer = new System.Windows.Forms.Timer(components);
            SuspendLayout();
            //
            // lblFolder
            //
            lblFolder.AutoSize = true;
            lblFolder.Location = new Point(12, 16);
            lblFolder.Name = "lblFolder";
            lblFolder.Size = new Size(91, 15);
            lblFolder.TabIndex = 0;
            lblFolder.Text = "PlayerDB addon:";
            //
            // txtFolder
            //
            txtFolder.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtFolder.Location = new Point(150, 12);
            txtFolder.Name = "txtFolder";
            txtFolder.Size = new Size(381, 23);
            txtFolder.TabIndex = 1;
            //
            // btnBrowse
            //
            btnBrowse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnBrowse.Location = new Point(537, 11);
            btnBrowse.Name = "btnBrowse";
            btnBrowse.Size = new Size(85, 25);
            btnBrowse.TabIndex = 2;
            btnBrowse.Text = "Browse…";
            btnBrowse.UseVisualStyleBackColor = true;
            btnBrowse.Click += btnBrowse_Click;
            //
            // lblHomeFolder
            //
            lblHomeFolder.AutoSize = true;
            lblHomeFolder.Location = new Point(12, 46);
            lblHomeFolder.Name = "lblHomeFolder";
            lblHomeFolder.Size = new Size(84, 15);
            lblHomeFolder.TabIndex = 3;
            lblHomeFolder.Text = "HomeDB addon:";
            //
            // txtHomeFolder
            //
            txtHomeFolder.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtHomeFolder.Location = new Point(150, 42);
            txtHomeFolder.Name = "txtHomeFolder";
            txtHomeFolder.Size = new Size(381, 23);
            txtHomeFolder.TabIndex = 4;
            //
            // btnBrowseHome
            //
            btnBrowseHome.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnBrowseHome.Location = new Point(537, 41);
            btnBrowseHome.Name = "btnBrowseHome";
            btnBrowseHome.Size = new Size(85, 25);
            btnBrowseHome.TabIndex = 5;
            btnBrowseHome.Text = "Browse…";
            btnBrowseHome.UseVisualStyleBackColor = true;
            btnBrowseHome.Click += btnBrowseHome_Click;
            //
            // lblRegradeFolder
            //
            lblRegradeFolder.AutoSize = true;
            lblRegradeFolder.Location = new Point(12, 76);
            lblRegradeFolder.Name = "lblRegradeFolder";
            lblRegradeFolder.Size = new Size(94, 15);
            lblRegradeFolder.TabIndex = 6;
            lblRegradeFolder.Text = "RegradeDB addon:";
            //
            // txtRegradeFolder
            //
            txtRegradeFolder.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtRegradeFolder.Location = new Point(150, 72);
            txtRegradeFolder.Name = "txtRegradeFolder";
            txtRegradeFolder.Size = new Size(381, 23);
            txtRegradeFolder.TabIndex = 7;
            //
            // btnBrowseRegrade
            //
            btnBrowseRegrade.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnBrowseRegrade.Location = new Point(537, 71);
            btnBrowseRegrade.Name = "btnBrowseRegrade";
            btnBrowseRegrade.Size = new Size(85, 25);
            btnBrowseRegrade.TabIndex = 8;
            btnBrowseRegrade.Text = "Browse…";
            btnBrowseRegrade.UseVisualStyleBackColor = true;
            btnBrowseRegrade.Click += btnBrowseRegrade_Click;
            //
            // lblUrl
            //
            lblUrl.AutoSize = true;
            lblUrl.Location = new Point(12, 106);
            lblUrl.Name = "lblUrl";
            lblUrl.Size = new Size(64, 15);
            lblUrl.TabIndex = 9;
            lblUrl.Text = "Server URL:";
            //
            // txtUrl
            //
            txtUrl.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtUrl.Location = new Point(150, 102);
            txtUrl.Name = "txtUrl";
            txtUrl.Size = new Size(472, 23);
            txtUrl.TabIndex = 10;
            //
            // lblUserId
            //
            lblUserId.AutoSize = true;
            lblUserId.Location = new Point(12, 136);
            lblUserId.Name = "lblUserId";
            lblUserId.Size = new Size(47, 15);
            lblUserId.TabIndex = 11;
            lblUserId.Text = "User ID:";
            //
            // txtUserId
            //
            txtUserId.Location = new Point(150, 132);
            txtUserId.Name = "txtUserId";
            txtUserId.Size = new Size(120, 23);
            txtUserId.TabIndex = 12;
            //
            // lblApiKey
            //
            lblApiKey.AutoSize = true;
            lblApiKey.Location = new Point(12, 166);
            lblApiKey.Name = "lblApiKey";
            lblApiKey.Size = new Size(50, 15);
            lblApiKey.TabIndex = 13;
            lblApiKey.Text = "API key:";
            //
            // txtApiKey
            //
            txtApiKey.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtApiKey.Location = new Point(150, 162);
            txtApiKey.Name = "txtApiKey";
            txtApiKey.Size = new Size(472, 23);
            txtApiKey.TabIndex = 14;
            txtApiKey.UseSystemPasswordChar = true;
            //
            // btnStart
            //
            btnStart.Location = new Point(150, 194);
            btnStart.Name = "btnStart";
            btnStart.Size = new Size(100, 28);
            btnStart.TabIndex = 15;
            btnStart.Text = "Start";
            btnStart.UseVisualStyleBackColor = true;
            btnStart.Click += btnStart_Click;
            //
            // lblStatus
            //
            lblStatus.AutoSize = true;
            lblStatus.Location = new Point(260, 194);
            lblStatus.Name = "lblStatus";
            lblStatus.Size = new Size(51, 15);
            lblStatus.TabIndex = 16;
            lblStatus.Text = "Stopped";
            //
            // txtLog
            //
            txtLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            txtLog.Location = new Point(12, 248);
            txtLog.Multiline = true;
            txtLog.Name = "txtLog";
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.Size = new Size(610, 277);
            txtLog.TabIndex = 17;
            //
            // syncTimer
            //
            syncTimer.Interval = 300000;
            syncTimer.Tick += syncTimer_Tick;
            //
            // homesTimer
            //
            homesTimer.Interval = 300000;
            homesTimer.Tick += homesTimer_Tick;
            //
            // regradesTimer
            //
            regradesTimer.Interval = 300000;
            regradesTimer.Tick += regradesTimer_Tick;
            //
            // Form1
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(634, 537);
            Controls.Add(txtLog);
            Controls.Add(lblStatus);
            Controls.Add(btnStart);
            Controls.Add(txtApiKey);
            Controls.Add(lblApiKey);
            Controls.Add(txtUserId);
            Controls.Add(lblUserId);
            Controls.Add(txtUrl);
            Controls.Add(lblUrl);
            Controls.Add(btnBrowseRegrade);
            Controls.Add(txtRegradeFolder);
            Controls.Add(lblRegradeFolder);
            Controls.Add(btnBrowseHome);
            Controls.Add(txtHomeFolder);
            Controls.Add(lblHomeFolder);
            Controls.Add(btnBrowse);
            Controls.Add(txtFolder);
            Controls.Add(lblFolder);
            MinimumSize = new Size(500, 400);
            Name = "Form1";
            Text = "PlayerDB Sync";
            FormClosing += Form1_FormClosing;
            Load += Form1_Load;
            Shown += Form1_Shown;
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label lblFolder;
        private TextBox txtFolder;
        private Button btnBrowse;
        private Label lblHomeFolder;
        private TextBox txtHomeFolder;
        private Button btnBrowseHome;
        private Label lblRegradeFolder;
        private TextBox txtRegradeFolder;
        private Button btnBrowseRegrade;
        private Label lblUrl;
        private TextBox txtUrl;
        private Label lblUserId;
        private TextBox txtUserId;
        private Label lblApiKey;
        private TextBox txtApiKey;
        private Button btnStart;
        private Label lblStatus;
        private TextBox txtLog;
        private System.Windows.Forms.Timer syncTimer;
        private System.Windows.Forms.Timer homesTimer;
        private System.Windows.Forms.Timer regradesTimer;
    }
}
