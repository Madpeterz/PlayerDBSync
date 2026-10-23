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
            lblPlayerVersion = new Label();
            lblRaidFolder = new Label();
            txtRaidFolder = new TextBox();
            btnBrowseRaid = new Button();
            lblRaidVersion = new Label();
            lblUrl = new Label();
            txtUrl = new TextBox();
            lblUserId = new Label();
            txtUserId = new TextBox();
            lblApiKey = new Label();
            txtApiKey = new TextBox();
            chkRaidScreenshots = new CheckBox();
            btnOpenScreenshots = new Button();
            chkDemoOcr = new CheckBox();
            btnStart = new Button();
            btnClearData = new Button();
            lblStatus = new Label();
            txtLog = new TextBox();
            syncTimer = new System.Windows.Forms.Timer(components);
            homesTimer = new System.Windows.Forms.Timer(components);
            regradesTimer = new System.Windows.Forms.Timer(components);
            inventoryTimer = new System.Windows.Forms.Timer(components);
            raidsTimer = new System.Windows.Forms.Timer(components);
            ignoreTimer = new System.Windows.Forms.Timer(components);
            imagesTimer = new System.Windows.Forms.Timer(components);
            demoOcrTimer = new System.Windows.Forms.Timer(components);
            SuspendLayout();
            // 
            // lblFolder
            // 
            lblFolder.AutoSize = true;
            lblFolder.Location = new Point(12, 16);
            lblFolder.Name = "lblFolder";
            lblFolder.Size = new Size(94, 15);
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
            txtFolder.TextChanged += txtAddonFolder_TextChanged;
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
            // lblPlayerVersion
            // 
            lblPlayerVersion.AutoSize = true;
            lblPlayerVersion.ForeColor = Color.ForestGreen;
            lblPlayerVersion.Location = new Point(150, 37);
            lblPlayerVersion.Name = "lblPlayerVersion";
            lblPlayerVersion.Size = new Size(0, 15);
            lblPlayerVersion.TabIndex = 17;
            // 
            // lblRaidFolder
            // 
            lblRaidFolder.AutoSize = true;
            lblRaidFolder.Location = new Point(12, 62);
            lblRaidFolder.Name = "lblRaidFolder";
            lblRaidFolder.Size = new Size(90, 15);
            lblRaidFolder.TabIndex = 3;
            lblRaidFolder.Text = "RaidsDB addon:";
            // 
            // txtRaidFolder
            // 
            txtRaidFolder.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtRaidFolder.Location = new Point(150, 58);
            txtRaidFolder.Name = "txtRaidFolder";
            txtRaidFolder.Size = new Size(381, 23);
            txtRaidFolder.TabIndex = 4;
            txtRaidFolder.TextChanged += txtAddonFolder_TextChanged;
            // 
            // btnBrowseRaid
            // 
            btnBrowseRaid.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnBrowseRaid.Location = new Point(537, 57);
            btnBrowseRaid.Name = "btnBrowseRaid";
            btnBrowseRaid.Size = new Size(85, 25);
            btnBrowseRaid.TabIndex = 5;
            btnBrowseRaid.Text = "Browse…";
            btnBrowseRaid.UseVisualStyleBackColor = true;
            btnBrowseRaid.Click += btnBrowseRaid_Click;
            // 
            // lblRaidVersion
            // 
            lblRaidVersion.AutoSize = true;
            lblRaidVersion.ForeColor = Color.ForestGreen;
            lblRaidVersion.Location = new Point(150, 83);
            lblRaidVersion.Name = "lblRaidVersion";
            lblRaidVersion.Size = new Size(0, 15);
            lblRaidVersion.TabIndex = 18;
            // 
            // lblUrl
            // 
            lblUrl.AutoSize = true;
            lblUrl.Location = new Point(12, 108);
            lblUrl.Name = "lblUrl";
            lblUrl.Size = new Size(66, 15);
            lblUrl.TabIndex = 6;
            lblUrl.Text = "Server URL:";
            // 
            // txtUrl
            // 
            txtUrl.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtUrl.Location = new Point(150, 104);
            txtUrl.Name = "txtUrl";
            txtUrl.Size = new Size(472, 23);
            txtUrl.TabIndex = 7;
            // 
            // lblUserId
            // 
            lblUserId.AutoSize = true;
            lblUserId.Location = new Point(12, 138);
            lblUserId.Name = "lblUserId";
            lblUserId.Size = new Size(47, 15);
            lblUserId.TabIndex = 8;
            lblUserId.Text = "User ID:";
            // 
            // txtUserId
            // 
            txtUserId.Location = new Point(150, 134);
            txtUserId.Name = "txtUserId";
            txtUserId.Size = new Size(120, 23);
            txtUserId.TabIndex = 9;
            // 
            // lblApiKey
            // 
            lblApiKey.AutoSize = true;
            lblApiKey.Location = new Point(12, 168);
            lblApiKey.Name = "lblApiKey";
            lblApiKey.Size = new Size(49, 15);
            lblApiKey.TabIndex = 10;
            lblApiKey.Text = "API key:";
            // 
            // txtApiKey
            // 
            txtApiKey.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtApiKey.Location = new Point(150, 164);
            txtApiKey.Name = "txtApiKey";
            txtApiKey.Size = new Size(472, 23);
            txtApiKey.TabIndex = 11;
            txtApiKey.UseSystemPasswordChar = true;
            // 
            // chkRaidScreenshots
            // 
            chkRaidScreenshots.AutoSize = true;
            chkRaidScreenshots.Location = new Point(19, 197);
            chkRaidScreenshots.Name = "chkRaidScreenshots";
            chkRaidScreenshots.Size = new Size(215, 19);
            chkRaidScreenshots.TabIndex = 12;
            chkRaidScreenshots.Text = "Save screenshot on RaidsDB request";
            chkRaidScreenshots.UseVisualStyleBackColor = true;
            chkRaidScreenshots.CheckedChanged += chkRaidScreenshots_CheckedChanged;
            // 
            // btnOpenScreenshots
            // 
            btnOpenScreenshots.Location = new Point(240, 193);
            btnOpenScreenshots.Name = "btnOpenScreenshots";
            btnOpenScreenshots.Size = new Size(85, 25);
            btnOpenScreenshots.TabIndex = 13;
            btnOpenScreenshots.Text = "Open folder";
            btnOpenScreenshots.UseVisualStyleBackColor = true;
            btnOpenScreenshots.Click += btnOpenScreenshots_Click;
            // 
            // chkDemoOcr
            // 
            chkDemoOcr.AutoSize = true;
            chkDemoOcr.Location = new Point(19, 221);
            chkDemoOcr.Name = "chkDemoOcr";
            chkDemoOcr.Size = new Size(292, 19);
            chkDemoOcr.TabIndex = 20;
            chkDemoOcr.Text = "Upload screenshot on PlayerDB DemoOCR request";
            chkDemoOcr.UseVisualStyleBackColor = true;
            chkDemoOcr.CheckedChanged += chkDemoOcr_CheckedChanged;
            // 
            // btnStart
            // 
            btnStart.Location = new Point(150, 250);
            btnStart.Name = "btnStart";
            btnStart.Size = new Size(100, 28);
            btnStart.TabIndex = 14;
            btnStart.Text = "Start";
            btnStart.UseVisualStyleBackColor = true;
            btnStart.Click += btnStart_Click;
            // 
            // btnClearData
            // 
            btnClearData.Location = new Point(12, 250);
            btnClearData.Name = "btnClearData";
            btnClearData.Size = new Size(130, 28);
            btnClearData.TabIndex = 19;
            btnClearData.Text = "Clear addon data…";
            btnClearData.UseVisualStyleBackColor = true;
            btnClearData.Click += btnClearData_Click;
            // 
            // lblStatus
            // 
            lblStatus.AutoSize = true;
            lblStatus.Location = new Point(340, 193);
            lblStatus.Name = "lblStatus";
            lblStatus.Size = new Size(51, 15);
            lblStatus.TabIndex = 15;
            lblStatus.Text = "Stopped";
            // 
            // txtLog
            // 
            txtLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            txtLog.Location = new Point(12, 304);
            txtLog.Multiline = true;
            txtLog.Name = "txtLog";
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.Size = new Size(610, 251);
            txtLog.TabIndex = 16;
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
            // inventoryTimer
            // 
            inventoryTimer.Interval = 300000;
            inventoryTimer.Tick += inventoryTimer_Tick;
            // 
            // raidsTimer
            // 
            raidsTimer.Interval = 300000;
            raidsTimer.Tick += raidsTimer_Tick;
            // 
            // ignoreTimer
            // 
            ignoreTimer.Interval = 900000;
            ignoreTimer.Tick += ignoreTimer_Tick;
            // 
            // imagesTimer
            // 
            imagesTimer.Interval = 30000;
            imagesTimer.Tick += imagesTimer_Tick;
            // 
            // demoOcrTimer
            // 
            demoOcrTimer.Interval = 30000;
            demoOcrTimer.Tick += demoOcrTimer_Tick;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(634, 567);
            Controls.Add(txtLog);
            Controls.Add(lblStatus);
            Controls.Add(btnStart);
            Controls.Add(btnClearData);
            Controls.Add(btnOpenScreenshots);
            Controls.Add(chkDemoOcr);
            Controls.Add(chkRaidScreenshots);
            Controls.Add(txtApiKey);
            Controls.Add(lblApiKey);
            Controls.Add(txtUserId);
            Controls.Add(lblUserId);
            Controls.Add(txtUrl);
            Controls.Add(lblUrl);
            Controls.Add(btnBrowseRaid);
            Controls.Add(txtRaidFolder);
            Controls.Add(lblRaidFolder);
            Controls.Add(lblRaidVersion);
            Controls.Add(lblPlayerVersion);
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
        private Label lblPlayerVersion;
        private Label lblRaidFolder;
        private Label lblRaidVersion;
        private TextBox txtRaidFolder;
        private Button btnBrowseRaid;
        private Label lblUrl;
        private TextBox txtUrl;
        private Label lblUserId;
        private TextBox txtUserId;
        private Label lblApiKey;
        private TextBox txtApiKey;
        private CheckBox chkRaidScreenshots;
        private Button btnOpenScreenshots;
        private CheckBox chkDemoOcr;
        private Button btnStart;
        private Button btnClearData;
        private Label lblStatus;
        private TextBox txtLog;
        private System.Windows.Forms.Timer syncTimer;
        private System.Windows.Forms.Timer homesTimer;
        private System.Windows.Forms.Timer regradesTimer;
        private System.Windows.Forms.Timer inventoryTimer;
        private System.Windows.Forms.Timer raidsTimer;
        private System.Windows.Forms.Timer ignoreTimer;
        private System.Windows.Forms.Timer imagesTimer;
        private System.Windows.Forms.Timer demoOcrTimer;
    }
}
