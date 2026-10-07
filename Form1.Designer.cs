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
            SuspendLayout();
            //
            // lblFolder
            //
            lblFolder.AutoSize = true;
            lblFolder.Location = new Point(12, 16);
            lblFolder.Name = "lblFolder";
            lblFolder.Size = new Size(88, 15);
            lblFolder.TabIndex = 0;
            lblFolder.Text = "Players folder:";
            //
            // txtFolder
            //
            txtFolder.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtFolder.Location = new Point(110, 12);
            txtFolder.Name = "txtFolder";
            txtFolder.Size = new Size(421, 23);
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
            // lblUrl
            //
            lblUrl.AutoSize = true;
            lblUrl.Location = new Point(12, 46);
            lblUrl.Name = "lblUrl";
            lblUrl.Size = new Size(64, 15);
            lblUrl.TabIndex = 3;
            lblUrl.Text = "Server URL:";
            //
            // txtUrl
            //
            txtUrl.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtUrl.Location = new Point(110, 42);
            txtUrl.Name = "txtUrl";
            txtUrl.Size = new Size(512, 23);
            txtUrl.TabIndex = 4;
            //
            // lblUserId
            //
            lblUserId.AutoSize = true;
            lblUserId.Location = new Point(12, 76);
            lblUserId.Name = "lblUserId";
            lblUserId.Size = new Size(47, 15);
            lblUserId.TabIndex = 5;
            lblUserId.Text = "User ID:";
            //
            // txtUserId
            //
            txtUserId.Location = new Point(110, 72);
            txtUserId.Name = "txtUserId";
            txtUserId.Size = new Size(120, 23);
            txtUserId.TabIndex = 6;
            //
            // lblApiKey
            //
            lblApiKey.AutoSize = true;
            lblApiKey.Location = new Point(12, 106);
            lblApiKey.Name = "lblApiKey";
            lblApiKey.Size = new Size(50, 15);
            lblApiKey.TabIndex = 7;
            lblApiKey.Text = "API key:";
            //
            // txtApiKey
            //
            txtApiKey.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtApiKey.Location = new Point(110, 102);
            txtApiKey.Name = "txtApiKey";
            txtApiKey.Size = new Size(512, 23);
            txtApiKey.TabIndex = 8;
            txtApiKey.UseSystemPasswordChar = true;
            //
            // btnStart
            //
            btnStart.Location = new Point(110, 134);
            btnStart.Name = "btnStart";
            btnStart.Size = new Size(100, 28);
            btnStart.TabIndex = 9;
            btnStart.Text = "Start";
            btnStart.UseVisualStyleBackColor = true;
            btnStart.Click += btnStart_Click;
            //
            // lblStatus
            //
            lblStatus.AutoSize = true;
            lblStatus.Location = new Point(220, 141);
            lblStatus.Name = "lblStatus";
            lblStatus.Size = new Size(51, 15);
            lblStatus.TabIndex = 10;
            lblStatus.Text = "Stopped";
            //
            // txtLog
            //
            txtLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            txtLog.Location = new Point(12, 172);
            txtLog.Multiline = true;
            txtLog.Name = "txtLog";
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.Size = new Size(610, 277);
            txtLog.TabIndex = 11;
            //
            // syncTimer
            //
            syncTimer.Interval = 300000;
            syncTimer.Tick += syncTimer_Tick;
            //
            // Form1
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(634, 461);
            Controls.Add(txtLog);
            Controls.Add(lblStatus);
            Controls.Add(btnStart);
            Controls.Add(txtApiKey);
            Controls.Add(lblApiKey);
            Controls.Add(txtUserId);
            Controls.Add(lblUserId);
            Controls.Add(txtUrl);
            Controls.Add(lblUrl);
            Controls.Add(btnBrowse);
            Controls.Add(txtFolder);
            Controls.Add(lblFolder);
            MinimumSize = new Size(500, 350);
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
    }
}
