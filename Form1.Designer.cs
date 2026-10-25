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
            pnlHeader = new HeaderBar();
            lblBrand = new Label();
            navConfig = new NavTab();
            navRunning = new NavTab();
            navInfo = new NavTab();
            lblStatus = new Label();
            btnStart = new ThemedButton();
            btnMinimize = new WindowButton();
            btnExit = new WindowButton();
            pnlConfig = new BackdropPanel();
            cardAddons = new CardPanel();
            lblAddonsTitle = new Label();
            lblFolder = new Label();
            frmFolder = new InputFrame();
            txtFolder = new TextBox();
            btnBrowse = new ThemedButton();
            lblPlayerVersion = new Label();
            lblRaidFolder = new Label();
            frmRaidFolder = new InputFrame();
            txtRaidFolder = new TextBox();
            btnBrowseRaid = new ThemedButton();
            lblRaidVersion = new Label();
            cardServer = new CardPanel();
            lblServerTitle = new Label();
            lblUrl = new Label();
            frmUrl = new InputFrame();
            txtUrl = new TextBox();
            lblUserId = new Label();
            frmUserId = new InputFrame();
            txtUserId = new TextBox();
            lblApiKey = new Label();
            frmApiKey = new InputFrame();
            txtApiKey = new TextBox();
            cardScreenshots = new CardPanel();
            lblScreenshotsTitle = new Label();
            chkRaidScreenshots = new ThemedCheckBox();
            btnOpenScreenshots = new ThemedButton();
            chkDemoOcr = new ThemedCheckBox();
            btnClearData = new ThemedButton();
            pnlRunning = new BackdropPanel();
            cardSchedule = new CardPanel();
            lblScheduleTitle = new Label();
            lblNotSyncing = new Label();
            tblSchedule = new TableLayoutPanel();
            cardLog = new CardPanel();
            lblLogTitle = new Label();
            txtLog = new TextBox();
            pnlInfo = new BackdropPanel();
            cardAbout = new CardPanel();
            lblAboutTitle = new Label();
            lblVersion = new Label();
            lblAbout = new Label();
            lnkSite = new LinkLabel();
            lnkReleases = new LinkLabel();
            lnkReadme = new LinkLabel();
            cardUploads = new CardPanel();
            lblUploadsTitle = new Label();
            tblUploads = new TableLayoutPanel();
            cardFiles = new CardPanel();
            lblFilesTitle = new Label();
            tblFiles = new TableLayoutPanel();
            scheduleTimer = new System.Windows.Forms.Timer(components);
            toolTip = new ToolTip(components);
            trayIcon = new NotifyIcon(components);
            trayMenu = new ContextMenuStrip(components);
            mnuShow = new ToolStripMenuItem();
            mnuStartStop = new ToolStripMenuItem();
            mnuSeparator = new ToolStripSeparator();
            mnuExit = new ToolStripMenuItem();
            trayMenu.SuspendLayout();
            pnlHeader.SuspendLayout();
            pnlConfig.SuspendLayout();
            cardAddons.SuspendLayout();
            frmFolder.SuspendLayout();
            frmRaidFolder.SuspendLayout();
            cardServer.SuspendLayout();
            frmUrl.SuspendLayout();
            frmUserId.SuspendLayout();
            frmApiKey.SuspendLayout();
            cardScreenshots.SuspendLayout();
            pnlRunning.SuspendLayout();
            cardSchedule.SuspendLayout();
            cardLog.SuspendLayout();
            pnlInfo.SuspendLayout();
            cardAbout.SuspendLayout();
            cardUploads.SuspendLayout();
            cardFiles.SuspendLayout();
            SuspendLayout();
            //
            // pnlHeader
            //
            pnlHeader.Controls.Add(lblBrand);
            pnlHeader.Controls.Add(navConfig);
            pnlHeader.Controls.Add(navRunning);
            pnlHeader.Controls.Add(navInfo);
            pnlHeader.Controls.Add(lblStatus);
            pnlHeader.Controls.Add(btnStart);
            pnlHeader.Controls.Add(btnMinimize);
            pnlHeader.Controls.Add(btnExit);
            pnlHeader.Dock = DockStyle.Top;
            pnlHeader.Location = new Point(0, 0);
            pnlHeader.Name = "pnlHeader";
            pnlHeader.Size = new Size(680, 56);
            pnlHeader.TabIndex = 0;
            //
            // lblBrand
            //
            lblBrand.AutoSize = true;
            lblBrand.Font = new Font("Segoe UI", 13F, FontStyle.Bold);
            lblBrand.Location = new Point(16, 15);
            lblBrand.Name = "lblBrand";
            lblBrand.Size = new Size(126, 25);
            lblBrand.TabIndex = 0;
            lblBrand.Text = "PlayerDB Sync";
            //
            // navConfig
            //
            navConfig.Active = true;
            navConfig.Location = new Point(166, 0);
            navConfig.Name = "navConfig";
            navConfig.Size = new Size(56, 56);
            navConfig.TabIndex = 1;
            navConfig.Text = "Config";
            navConfig.Click += navTab_Click;
            //
            // navRunning
            //
            navRunning.Location = new Point(222, 0);
            navRunning.Name = "navRunning";
            navRunning.Size = new Size(66, 56);
            navRunning.TabIndex = 2;
            navRunning.Text = "Running";
            navRunning.Click += navTab_Click;
            //
            // navInfo
            //
            navInfo.Location = new Point(288, 0);
            navInfo.Name = "navInfo";
            navInfo.Size = new Size(42, 56);
            navInfo.TabIndex = 3;
            navInfo.Text = "Info";
            navInfo.Click += navTab_Click;
            //
            // lblStatus
            //
            lblStatus.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            lblStatus.AutoSize = true;
            lblStatus.ForeColor = Color.FromArgb(154, 161, 173);
            lblStatus.Location = new Point(497, 21);
            lblStatus.Name = "lblStatus";
            lblStatus.Size = new Size(51, 15);
            lblStatus.TabIndex = 4;
            lblStatus.Text = "Stopped";
            //
            // btnStart
            //
            btnStart.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnStart.Kind = ButtonKind.Primary;
            btnStart.Location = new Point(484, 13);
            btnStart.Name = "btnStart";
            btnStart.Size = new Size(100, 30);
            btnStart.TabIndex = 5;
            btnStart.Text = "Start";
            btnStart.Click += btnStart_Click;
            //
            // btnMinimize
            //
            btnMinimize.AccessibleName = "Minimise";
            btnMinimize.AccessibleRole = AccessibleRole.PushButton;
            btnMinimize.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnMinimize.Location = new Point(596, 13);
            btnMinimize.Name = "btnMinimize";
            btnMinimize.Size = new Size(34, 30);
            btnMinimize.TabIndex = 6;
            toolTip.SetToolTip(btnMinimize, "Minimise");
            btnMinimize.Click += btnMinimize_Click;
            //
            // btnExit
            //
            btnExit.AccessibleName = "Exit";
            btnExit.AccessibleRole = AccessibleRole.PushButton;
            btnExit.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnExit.Glyph = WindowGlyph.Close;
            btnExit.Location = new Point(634, 13);
            btnExit.Name = "btnExit";
            btnExit.Size = new Size(34, 30);
            btnExit.TabIndex = 7;
            toolTip.SetToolTip(btnExit, "Exit");
            btnExit.Click += btnExit_Click;
            //
            // pnlConfig
            //
            pnlConfig.AutoScroll = true;
            pnlConfig.Controls.Add(cardAddons);
            pnlConfig.Controls.Add(cardServer);
            pnlConfig.Controls.Add(cardScreenshots);
            pnlConfig.Controls.Add(btnClearData);
            pnlConfig.Dock = DockStyle.Fill;
            pnlConfig.Location = new Point(0, 56);
            pnlConfig.Name = "pnlConfig";
            pnlConfig.Size = new Size(680, 544);
            pnlConfig.TabIndex = 1;
            //
            // cardAddons
            //
            cardAddons.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            cardAddons.Controls.Add(lblAddonsTitle);
            cardAddons.Controls.Add(lblFolder);
            cardAddons.Controls.Add(frmFolder);
            cardAddons.Controls.Add(btnBrowse);
            cardAddons.Controls.Add(lblPlayerVersion);
            cardAddons.Controls.Add(lblRaidFolder);
            cardAddons.Controls.Add(frmRaidFolder);
            cardAddons.Controls.Add(btnBrowseRaid);
            cardAddons.Controls.Add(lblRaidVersion);
            cardAddons.Location = new Point(16, 16);
            cardAddons.Name = "cardAddons";
            cardAddons.Size = new Size(648, 166);
            cardAddons.TabIndex = 0;
            //
            // lblAddonsTitle
            //
            lblAddonsTitle.AutoSize = true;
            lblAddonsTitle.Font = new Font("Segoe UI Semibold", 10.5F);
            lblAddonsTitle.Location = new Point(16, 12);
            lblAddonsTitle.Name = "lblAddonsTitle";
            lblAddonsTitle.Size = new Size(55, 19);
            lblAddonsTitle.TabIndex = 0;
            lblAddonsTitle.Text = "Addons";
            //
            // lblFolder
            //
            lblFolder.AutoSize = true;
            lblFolder.ForeColor = Color.FromArgb(154, 161, 173);
            lblFolder.Location = new Point(16, 51);
            lblFolder.Name = "lblFolder";
            lblFolder.Size = new Size(91, 15);
            lblFolder.TabIndex = 1;
            lblFolder.Text = "PlayerDB addon";
            //
            // frmFolder
            //
            frmFolder.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            frmFolder.Controls.Add(txtFolder);
            frmFolder.Location = new Point(140, 44);
            frmFolder.Name = "frmFolder";
            frmFolder.Size = new Size(394, 30);
            frmFolder.TabIndex = 2;
            //
            // txtFolder
            //
            txtFolder.Name = "txtFolder";
            txtFolder.Size = new Size(378, 16);
            txtFolder.TabIndex = 0;
            txtFolder.TextChanged += txtAddonFolder_TextChanged;
            //
            // btnBrowse
            //
            btnBrowse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnBrowse.Location = new Point(542, 44);
            btnBrowse.Name = "btnBrowse";
            btnBrowse.Size = new Size(90, 30);
            btnBrowse.TabIndex = 3;
            btnBrowse.Text = "Browse…";
            btnBrowse.Click += btnBrowse_Click;
            //
            // lblPlayerVersion
            //
            lblPlayerVersion.AutoSize = true;
            lblPlayerVersion.ForeColor = Color.FromArgb(92, 201, 138);
            lblPlayerVersion.Location = new Point(140, 78);
            lblPlayerVersion.Name = "lblPlayerVersion";
            lblPlayerVersion.Size = new Size(0, 15);
            lblPlayerVersion.TabIndex = 4;
            //
            // lblRaidFolder
            //
            lblRaidFolder.AutoSize = true;
            lblRaidFolder.ForeColor = Color.FromArgb(154, 161, 173);
            lblRaidFolder.Location = new Point(16, 111);
            lblRaidFolder.Name = "lblRaidFolder";
            lblRaidFolder.Size = new Size(87, 15);
            lblRaidFolder.TabIndex = 5;
            lblRaidFolder.Text = "RaidsDB addon";
            //
            // frmRaidFolder
            //
            frmRaidFolder.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            frmRaidFolder.Controls.Add(txtRaidFolder);
            frmRaidFolder.Location = new Point(140, 104);
            frmRaidFolder.Name = "frmRaidFolder";
            frmRaidFolder.Size = new Size(394, 30);
            frmRaidFolder.TabIndex = 6;
            //
            // txtRaidFolder
            //
            txtRaidFolder.Name = "txtRaidFolder";
            txtRaidFolder.Size = new Size(378, 16);
            txtRaidFolder.TabIndex = 0;
            txtRaidFolder.TextChanged += txtAddonFolder_TextChanged;
            //
            // btnBrowseRaid
            //
            btnBrowseRaid.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnBrowseRaid.Location = new Point(542, 104);
            btnBrowseRaid.Name = "btnBrowseRaid";
            btnBrowseRaid.Size = new Size(90, 30);
            btnBrowseRaid.TabIndex = 7;
            btnBrowseRaid.Text = "Browse…";
            btnBrowseRaid.Click += btnBrowseRaid_Click;
            //
            // lblRaidVersion
            //
            lblRaidVersion.AutoSize = true;
            lblRaidVersion.ForeColor = Color.FromArgb(92, 201, 138);
            lblRaidVersion.Location = new Point(140, 138);
            lblRaidVersion.Name = "lblRaidVersion";
            lblRaidVersion.Size = new Size(0, 15);
            lblRaidVersion.TabIndex = 8;
            //
            // cardServer
            //
            cardServer.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            cardServer.Controls.Add(lblServerTitle);
            cardServer.Controls.Add(lblUrl);
            cardServer.Controls.Add(frmUrl);
            cardServer.Controls.Add(lblUserId);
            cardServer.Controls.Add(frmUserId);
            cardServer.Controls.Add(lblApiKey);
            cardServer.Controls.Add(frmApiKey);
            cardServer.Location = new Point(16, 194);
            cardServer.Name = "cardServer";
            cardServer.Size = new Size(648, 162);
            cardServer.TabIndex = 1;
            //
            // lblServerTitle
            //
            lblServerTitle.AutoSize = true;
            lblServerTitle.Font = new Font("Segoe UI Semibold", 10.5F);
            lblServerTitle.Location = new Point(16, 12);
            lblServerTitle.Name = "lblServerTitle";
            lblServerTitle.Size = new Size(49, 19);
            lblServerTitle.TabIndex = 0;
            lblServerTitle.Text = "Server";
            //
            // lblUrl
            //
            lblUrl.AutoSize = true;
            lblUrl.ForeColor = Color.FromArgb(154, 161, 173);
            lblUrl.Location = new Point(16, 51);
            lblUrl.Name = "lblUrl";
            lblUrl.Size = new Size(63, 15);
            lblUrl.TabIndex = 1;
            lblUrl.Text = "Server URL";
            //
            // frmUrl
            //
            frmUrl.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            frmUrl.Controls.Add(txtUrl);
            frmUrl.Location = new Point(140, 44);
            frmUrl.Name = "frmUrl";
            frmUrl.Size = new Size(492, 30);
            frmUrl.TabIndex = 2;
            //
            // txtUrl
            //
            txtUrl.Name = "txtUrl";
            txtUrl.Size = new Size(476, 16);
            txtUrl.TabIndex = 0;
            //
            // lblUserId
            //
            lblUserId.AutoSize = true;
            lblUserId.ForeColor = Color.FromArgb(154, 161, 173);
            lblUserId.Location = new Point(16, 87);
            lblUserId.Name = "lblUserId";
            lblUserId.Size = new Size(44, 15);
            lblUserId.TabIndex = 3;
            lblUserId.Text = "User ID";
            //
            // frmUserId
            //
            frmUserId.Controls.Add(txtUserId);
            frmUserId.Location = new Point(140, 80);
            frmUserId.Name = "frmUserId";
            frmUserId.Size = new Size(140, 30);
            frmUserId.TabIndex = 4;
            //
            // txtUserId
            //
            txtUserId.Name = "txtUserId";
            txtUserId.Size = new Size(124, 16);
            txtUserId.TabIndex = 0;
            //
            // lblApiKey
            //
            lblApiKey.AutoSize = true;
            lblApiKey.ForeColor = Color.FromArgb(154, 161, 173);
            lblApiKey.Location = new Point(16, 123);
            lblApiKey.Name = "lblApiKey";
            lblApiKey.Size = new Size(46, 15);
            lblApiKey.TabIndex = 5;
            lblApiKey.Text = "API key";
            //
            // frmApiKey
            //
            frmApiKey.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            frmApiKey.Controls.Add(txtApiKey);
            frmApiKey.Location = new Point(140, 116);
            frmApiKey.Name = "frmApiKey";
            frmApiKey.Size = new Size(492, 30);
            frmApiKey.TabIndex = 6;
            //
            // txtApiKey
            //
            txtApiKey.Name = "txtApiKey";
            txtApiKey.Size = new Size(476, 16);
            txtApiKey.TabIndex = 0;
            txtApiKey.UseSystemPasswordChar = true;
            //
            // cardScreenshots
            //
            cardScreenshots.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            cardScreenshots.Controls.Add(lblScreenshotsTitle);
            cardScreenshots.Controls.Add(chkRaidScreenshots);
            cardScreenshots.Controls.Add(btnOpenScreenshots);
            cardScreenshots.Controls.Add(chkDemoOcr);
            cardScreenshots.Location = new Point(16, 368);
            cardScreenshots.Name = "cardScreenshots";
            cardScreenshots.Size = new Size(648, 108);
            cardScreenshots.TabIndex = 2;
            //
            // lblScreenshotsTitle
            //
            lblScreenshotsTitle.AutoSize = true;
            lblScreenshotsTitle.Font = new Font("Segoe UI Semibold", 10.5F);
            lblScreenshotsTitle.Location = new Point(16, 12);
            lblScreenshotsTitle.Name = "lblScreenshotsTitle";
            lblScreenshotsTitle.Size = new Size(85, 19);
            lblScreenshotsTitle.TabIndex = 0;
            lblScreenshotsTitle.Text = "Screenshots";
            //
            // chkRaidScreenshots
            //
            chkRaidScreenshots.AutoSize = true;
            chkRaidScreenshots.Location = new Point(16, 44);
            chkRaidScreenshots.Name = "chkRaidScreenshots";
            chkRaidScreenshots.Size = new Size(225, 21);
            chkRaidScreenshots.TabIndex = 1;
            chkRaidScreenshots.Text = "Save screenshot on RaidsDB request";
            chkRaidScreenshots.CheckedChanged += chkRaidScreenshots_CheckedChanged;
            //
            // btnOpenScreenshots
            //
            btnOpenScreenshots.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnOpenScreenshots.Location = new Point(522, 40);
            btnOpenScreenshots.Name = "btnOpenScreenshots";
            btnOpenScreenshots.Size = new Size(110, 30);
            btnOpenScreenshots.TabIndex = 2;
            btnOpenScreenshots.Text = "Open folder";
            btnOpenScreenshots.Click += btnOpenScreenshots_Click;
            //
            // chkDemoOcr
            //
            chkDemoOcr.AutoSize = true;
            chkDemoOcr.Location = new Point(16, 74);
            chkDemoOcr.Name = "chkDemoOcr";
            chkDemoOcr.Size = new Size(302, 21);
            chkDemoOcr.TabIndex = 3;
            chkDemoOcr.Text = "Upload screenshot on PlayerDB DemoOCR request";
            chkDemoOcr.CheckedChanged += chkDemoOcr_CheckedChanged;
            //
            // btnClearData
            //
            btnClearData.Kind = ButtonKind.Danger;
            btnClearData.Location = new Point(16, 490);
            btnClearData.Name = "btnClearData";
            btnClearData.Size = new Size(150, 30);
            btnClearData.TabIndex = 3;
            btnClearData.Text = "Clear addon data…";
            btnClearData.Click += btnClearData_Click;
            //
            // pnlRunning
            //
            pnlRunning.Controls.Add(cardSchedule);
            pnlRunning.Controls.Add(cardLog);
            pnlRunning.Dock = DockStyle.Fill;
            pnlRunning.Location = new Point(0, 56);
            pnlRunning.Name = "pnlRunning";
            pnlRunning.Size = new Size(680, 544);
            pnlRunning.TabIndex = 2;
            pnlRunning.Visible = false;
            //
            // cardSchedule
            //
            cardSchedule.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            cardSchedule.Controls.Add(lblScheduleTitle);
            cardSchedule.Controls.Add(lblNotSyncing);
            cardSchedule.Controls.Add(tblSchedule);
            cardSchedule.Location = new Point(16, 16);
            cardSchedule.Name = "cardSchedule";
            cardSchedule.Size = new Size(648, 75);
            cardSchedule.TabIndex = 0;
            //
            // lblScheduleTitle
            //
            lblScheduleTitle.AutoSize = true;
            lblScheduleTitle.Font = new Font("Segoe UI Semibold", 10.5F);
            lblScheduleTitle.Location = new Point(16, 12);
            lblScheduleTitle.Name = "lblScheduleTitle";
            lblScheduleTitle.Size = new Size(65, 19);
            lblScheduleTitle.TabIndex = 0;
            lblScheduleTitle.Text = "Schedule";
            //
            // lblNotSyncing
            //
            lblNotSyncing.AutoSize = true;
            lblNotSyncing.ForeColor = Color.FromArgb(154, 161, 173);
            lblNotSyncing.Location = new Point(16, 44);
            lblNotSyncing.Name = "lblNotSyncing";
            lblNotSyncing.Size = new Size(246, 15);
            lblNotSyncing.TabIndex = 1;
            lblNotSyncing.Text = "Not syncing. Fill in Config, then press Start.";
            //
            // tblSchedule
            //
            tblSchedule.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            tblSchedule.ColumnCount = 3;
            tblSchedule.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tblSchedule.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tblSchedule.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210F));
            tblSchedule.Location = new Point(16, 44);
            tblSchedule.Name = "tblSchedule";
            tblSchedule.RowCount = 0;
            tblSchedule.Size = new Size(616, 0);
            tblSchedule.TabIndex = 2;
            //
            // cardLog
            //
            cardLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            cardLog.Controls.Add(lblLogTitle);
            cardLog.Controls.Add(txtLog);
            cardLog.Location = new Point(16, 103);
            cardLog.Name = "cardLog";
            cardLog.Size = new Size(648, 425);
            cardLog.TabIndex = 1;
            //
            // lblLogTitle
            //
            lblLogTitle.AutoSize = true;
            lblLogTitle.Font = new Font("Segoe UI Semibold", 10.5F);
            lblLogTitle.Location = new Point(16, 12);
            lblLogTitle.Name = "lblLogTitle";
            lblLogTitle.Size = new Size(31, 19);
            lblLogTitle.TabIndex = 0;
            lblLogTitle.Text = "Log";
            //
            // txtLog
            //
            txtLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            txtLog.BackColor = Color.FromArgb(28, 32, 39);
            txtLog.BorderStyle = BorderStyle.None;
            txtLog.Font = new Font("Consolas", 9F);
            txtLog.ForeColor = Color.FromArgb(230, 232, 236);
            txtLog.Location = new Point(16, 44);
            txtLog.Multiline = true;
            txtLog.Name = "txtLog";
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.Size = new Size(620, 369);
            txtLog.TabIndex = 1;
            //
            // pnlInfo
            //
            pnlInfo.AutoScroll = true;
            pnlInfo.Controls.Add(cardAbout);
            pnlInfo.Controls.Add(cardUploads);
            pnlInfo.Controls.Add(cardFiles);
            pnlInfo.Dock = DockStyle.Fill;
            pnlInfo.Location = new Point(0, 56);
            pnlInfo.Name = "pnlInfo";
            pnlInfo.Size = new Size(680, 544);
            pnlInfo.TabIndex = 3;
            pnlInfo.Visible = false;
            //
            // cardAbout
            //
            cardAbout.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            cardAbout.Controls.Add(lblAboutTitle);
            cardAbout.Controls.Add(lblVersion);
            cardAbout.Controls.Add(lblAbout);
            cardAbout.Controls.Add(lnkSite);
            cardAbout.Controls.Add(lnkReleases);
            cardAbout.Controls.Add(lnkReadme);
            cardAbout.Location = new Point(16, 16);
            cardAbout.Name = "cardAbout";
            cardAbout.Size = new Size(648, 112);
            cardAbout.TabIndex = 0;
            //
            // lblAboutTitle
            //
            lblAboutTitle.AutoSize = true;
            lblAboutTitle.Font = new Font("Segoe UI Semibold", 10.5F);
            lblAboutTitle.Location = new Point(16, 12);
            lblAboutTitle.Name = "lblAboutTitle";
            lblAboutTitle.Size = new Size(99, 19);
            lblAboutTitle.TabIndex = 0;
            lblAboutTitle.Text = "PlayerDB Sync";
            //
            // lblVersion
            //
            lblVersion.AutoSize = true;
            lblVersion.ForeColor = Color.FromArgb(154, 161, 173);
            lblVersion.Location = new Point(121, 15);
            lblVersion.Name = "lblVersion";
            lblVersion.Size = new Size(0, 15);
            lblVersion.TabIndex = 1;
            //
            // lblAbout
            //
            lblAbout.AutoSize = true;
            lblAbout.Location = new Point(16, 44);
            lblAbout.MaximumSize = new Size(616, 0);
            lblAbout.Name = "lblAbout";
            lblAbout.Size = new Size(440, 15);
            lblAbout.TabIndex = 2;
            lblAbout.Text = "Uploads what the PlayerDB and RaidsDB addons collect to the PlayerDB site.";
            //
            // lnkSite
            //
            lnkSite.AutoSize = true;
            lnkSite.Location = new Point(16, 76);
            lnkSite.Name = "lnkSite";
            lnkSite.Size = new Size(84, 15);
            lnkSite.TabIndex = 3;
            lnkSite.TabStop = true;
            lnkSite.Text = "Open the site";
            lnkSite.LinkClicked += lnkSite_LinkClicked;
            //
            // lnkReleases
            //
            lnkReleases.AutoSize = true;
            lnkReleases.Location = new Point(124, 76);
            lnkReleases.Name = "lnkReleases";
            lnkReleases.Size = new Size(108, 15);
            lnkReleases.TabIndex = 4;
            lnkReleases.TabStop = true;
            lnkReleases.Text = "Check for updates";
            lnkReleases.LinkClicked += lnkReleases_LinkClicked;
            //
            // lnkReadme
            //
            lnkReadme.AutoSize = true;
            lnkReadme.Location = new Point(256, 76);
            lnkReadme.Name = "lnkReadme";
            lnkReadme.Size = new Size(68, 15);
            lnkReadme.TabIndex = 5;
            lnkReadme.TabStop = true;
            lnkReadme.Text = "How to use";
            lnkReadme.LinkClicked += lnkReadme_LinkClicked;
            //
            // cardUploads
            //
            cardUploads.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            cardUploads.Controls.Add(lblUploadsTitle);
            cardUploads.Controls.Add(tblUploads);
            cardUploads.Location = new Point(16, 140);
            cardUploads.Name = "cardUploads";
            cardUploads.Size = new Size(648, 100);
            cardUploads.TabIndex = 1;
            //
            // lblUploadsTitle
            //
            lblUploadsTitle.AutoSize = true;
            lblUploadsTitle.Font = new Font("Segoe UI Semibold", 10.5F);
            lblUploadsTitle.Location = new Point(16, 12);
            lblUploadsTitle.Name = "lblUploadsTitle";
            lblUploadsTitle.Size = new Size(94, 19);
            lblUploadsTitle.TabIndex = 0;
            lblUploadsTitle.Text = "What it syncs";
            //
            // tblUploads
            //
            tblUploads.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            tblUploads.ColumnCount = 3;
            tblUploads.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tblUploads.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tblUploads.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tblUploads.Location = new Point(16, 44);
            tblUploads.Name = "tblUploads";
            tblUploads.RowCount = 0;
            tblUploads.Size = new Size(616, 40);
            tblUploads.TabIndex = 1;
            //
            // cardFiles
            //
            cardFiles.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            cardFiles.Controls.Add(lblFilesTitle);
            cardFiles.Controls.Add(tblFiles);
            cardFiles.Location = new Point(16, 252);
            cardFiles.Name = "cardFiles";
            cardFiles.Size = new Size(648, 100);
            cardFiles.TabIndex = 2;
            //
            // lblFilesTitle
            //
            lblFilesTitle.AutoSize = true;
            lblFilesTitle.Font = new Font("Segoe UI Semibold", 10.5F);
            lblFilesTitle.Location = new Point(16, 12);
            lblFilesTitle.Name = "lblFilesTitle";
            lblFilesTitle.Size = new Size(146, 19);
            lblFilesTitle.TabIndex = 0;
            lblFilesTitle.Text = "Where it keeps things";
            //
            // tblFiles
            //
            tblFiles.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            tblFiles.ColumnCount = 2;
            tblFiles.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tblFiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tblFiles.Location = new Point(16, 44);
            tblFiles.Name = "tblFiles";
            tblFiles.RowCount = 0;
            tblFiles.Size = new Size(616, 40);
            tblFiles.TabIndex = 1;
            //
            // scheduleTimer
            //
            scheduleTimer.Interval = 250;
            scheduleTimer.Tick += scheduleTimer_Tick;
            //
            // trayIcon
            //
            trayIcon.ContextMenuStrip = trayMenu;
            trayIcon.Text = "PlayerDB Sync – stopped";
            trayIcon.MouseClick += trayIcon_MouseClick;
            //
            // trayMenu
            //
            trayMenu.Items.AddRange(new ToolStripItem[] { mnuShow, mnuStartStop, mnuSeparator, mnuExit });
            trayMenu.Name = "trayMenu";
            trayMenu.ShowImageMargin = false;
            trayMenu.Size = new Size(126, 76);
            trayMenu.Opening += trayMenu_Opening;
            //
            // mnuShow
            //
            mnuShow.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            mnuShow.Name = "mnuShow";
            mnuShow.Size = new Size(125, 22);
            mnuShow.Text = "Show";
            mnuShow.Click += mnuShow_Click;
            //
            // mnuStartStop
            //
            mnuStartStop.Name = "mnuStartStop";
            mnuStartStop.Size = new Size(125, 22);
            mnuStartStop.Text = "Start syncing";
            mnuStartStop.Click += mnuStartStop_Click;
            //
            // mnuSeparator
            //
            mnuSeparator.Name = "mnuSeparator";
            mnuSeparator.Size = new Size(122, 6);
            //
            // mnuExit
            //
            mnuExit.Name = "mnuExit";
            mnuExit.Size = new Size(125, 22);
            mnuExit.Text = "Exit";
            mnuExit.Click += btnExit_Click;
            //
            // Form1
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.FromArgb(20, 23, 28);
            ClientSize = new Size(680, 600);
            Controls.Add(pnlConfig);
            Controls.Add(pnlRunning);
            Controls.Add(pnlInfo);
            Controls.Add(pnlHeader);
            ForeColor = Color.FromArgb(230, 232, 236);
            MinimumSize = new Size(600, 480);
            Name = "Form1";
            Text = "PlayerDB Sync";
            FormClosing += Form1_FormClosing;
            Load += Form1_Load;
            Shown += Form1_Shown;
            trayMenu.ResumeLayout(false);
            pnlHeader.ResumeLayout(false);
            pnlHeader.PerformLayout();
            frmFolder.ResumeLayout(false);
            frmFolder.PerformLayout();
            frmRaidFolder.ResumeLayout(false);
            frmRaidFolder.PerformLayout();
            cardAddons.ResumeLayout(false);
            cardAddons.PerformLayout();
            frmUrl.ResumeLayout(false);
            frmUrl.PerformLayout();
            frmUserId.ResumeLayout(false);
            frmUserId.PerformLayout();
            frmApiKey.ResumeLayout(false);
            frmApiKey.PerformLayout();
            cardServer.ResumeLayout(false);
            cardServer.PerformLayout();
            cardScreenshots.ResumeLayout(false);
            cardScreenshots.PerformLayout();
            pnlConfig.ResumeLayout(false);
            cardSchedule.ResumeLayout(false);
            cardSchedule.PerformLayout();
            cardLog.ResumeLayout(false);
            cardLog.PerformLayout();
            pnlRunning.ResumeLayout(false);
            cardAbout.ResumeLayout(false);
            cardAbout.PerformLayout();
            cardUploads.ResumeLayout(false);
            cardUploads.PerformLayout();
            cardFiles.ResumeLayout(false);
            cardFiles.PerformLayout();
            pnlInfo.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private HeaderBar pnlHeader;
        private Label lblBrand;
        private NavTab navConfig;
        private NavTab navRunning;
        private NavTab navInfo;
        private Label lblStatus;
        private ThemedButton btnStart;
        private WindowButton btnMinimize;
        private WindowButton btnExit;
        private BackdropPanel pnlConfig;
        private CardPanel cardAddons;
        private Label lblAddonsTitle;
        private Label lblFolder;
        private InputFrame frmFolder;
        private TextBox txtFolder;
        private ThemedButton btnBrowse;
        private Label lblPlayerVersion;
        private Label lblRaidFolder;
        private InputFrame frmRaidFolder;
        private TextBox txtRaidFolder;
        private ThemedButton btnBrowseRaid;
        private Label lblRaidVersion;
        private CardPanel cardServer;
        private Label lblServerTitle;
        private Label lblUrl;
        private InputFrame frmUrl;
        private TextBox txtUrl;
        private Label lblUserId;
        private InputFrame frmUserId;
        private TextBox txtUserId;
        private Label lblApiKey;
        private InputFrame frmApiKey;
        private TextBox txtApiKey;
        private CardPanel cardScreenshots;
        private Label lblScreenshotsTitle;
        private ThemedCheckBox chkRaidScreenshots;
        private ThemedButton btnOpenScreenshots;
        private ThemedCheckBox chkDemoOcr;
        private ThemedButton btnClearData;
        private BackdropPanel pnlRunning;
        private CardPanel cardSchedule;
        private Label lblScheduleTitle;
        private Label lblNotSyncing;
        private TableLayoutPanel tblSchedule;
        private CardPanel cardLog;
        private Label lblLogTitle;
        private TextBox txtLog;
        private BackdropPanel pnlInfo;
        private CardPanel cardAbout;
        private Label lblAboutTitle;
        private Label lblVersion;
        private Label lblAbout;
        private LinkLabel lnkSite;
        private LinkLabel lnkReleases;
        private LinkLabel lnkReadme;
        private CardPanel cardUploads;
        private Label lblUploadsTitle;
        private TableLayoutPanel tblUploads;
        private CardPanel cardFiles;
        private Label lblFilesTitle;
        private TableLayoutPanel tblFiles;
        private System.Windows.Forms.Timer scheduleTimer;
        private ToolTip toolTip;
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;
        private ToolStripMenuItem mnuShow;
        private ToolStripMenuItem mnuStartStop;
        private ToolStripSeparator mnuSeparator;
        private ToolStripMenuItem mnuExit;
    }
}
