namespace PlayerDBSync
{
    /// <summary>The Windows window: fills in the settings, shows what <see cref="SyncEngine"/> is doing, and sits in the tray.</summary>
    public partial class Form1 : Form
    {
        private const int MaxLogLines = 500;

        // Steps in a schedule row's bar; enough that a 30 min backoff still moves every few seconds.
        private const int BarSteps = 1000;

        private readonly AppSettings _settings = AppSettings.Load();
        private readonly SyncEngine _engine;

        // Bumped on each version lookup, so a slow earlier one can't overwrite a newer result.
        private int _versionLookup;

        // The newer release btnUpdate offers, once one is found.
        private string? _updateTag;

        // The row each event has while syncing: its bar and its status.
        private readonly Dictionary<TimedEvent, (ScheduleBar Bar, Label Status)> _rows = new();

        // The header's tabs and the page each shows.
        private readonly (NavTab Tab, BackdropPanel Page)[] _pages;

        // The site's background, behind the pages.
        private readonly Backdrop _backdrop;

        public Form1()
        {
            InitializeComponent();
            // The site's emblem, at every size the taskbar and Alt+Tab ask for (the exe's own icon is the same file),
            // and at the tray's size.
            using (var stream = typeof(Form1).Assembly.GetManifestResourceStream("PlayerDBSync.app.ico"))
            {
                if (stream != null)
                {
                    var emblem = new Icon(stream);
                    Icon = emblem;
                    trayIcon.Icon = new Icon(emblem, SystemInformation.SmallIconSize);
                }
            }
            // Always in the tray while the app runs: click it to bring the window back, right-click for its menu.
            trayMenu.Renderer = new ThemedMenuRenderer();
            trayIcon.Visible = true;
            Theme.StyleWindow(this);
            Theme.DarkScrollBars(txtLog);
            _pages = [(navConfig, pnlConfig), (navRunning, pnlRunning), (navInfo, pnlInfo)];
            LayoutHeader();
            // No title bar: the header moves the window instead.
            BorderlessWindow.AddDragHandles(this, pnlHeader, lblBrand, lblStatus);

            _backdrop = new Backdrop(this);
            foreach (var (_, page) in _pages)
            {
                page.Backdrop = _backdrop;
            }
            // Every page, not just the one showing: while the window is opening or minimised none counts as visible.
            // Repainting a hidden page costs nothing.
            _backdrop.Changed += () =>
            {
                foreach (var (_, page) in _pages)
                {
                    page.Invalidate(true);
                }
            };
            // As tapping your name on the site: a different background now. A hidden extra, so nothing shows it can be clicked.
            lblBrand.Click += (_, _) => _backdrop.ChangeNow();
            Disposed += (_, _) => _backdrop.Dispose();

            _engine = new SyncEngine(_settings, Platform.CreateGameCapture(_settings));
            _engine.Logged += Log;
            _engine.StatusChanged += ShowStatus;
            _engine.AddonDataChanged += () => _ = RefreshVersionsAsync();
            Disposed += (_, _) => _engine.Dispose();

            ShowInfo();

            // A dev build always has the update orb, leading to the latest release; a release only once a newer one is out.
            if (!UpdateCheck.IsRelease(AppInfo.Version))
            {
                toolTip.SetToolTip(btnUpdateOrb, $"Dev build ({AppInfo.Version}): click for the latest release");
                btnUpdateOrb.Visible = true;
                LayoutHeader();
            }

            var updateTimer = new System.Windows.Forms.Timer { Interval = (int)Schedule.UpdateCheckInterval.TotalMilliseconds };
            updateTimer.Tick += async (_, _) => await CheckForUpdateAsync();
            updateTimer.Start();
            Disposed += (_, _) => updateTimer.Dispose();
        }

        private SyncInput CurrentInput() =>
            new(txtFolder.Text, txtRaidFolder.Text, txtUrl.Text, txtUserId.Text, txtApiKey.Text);

        private void Form1_Load(object? sender, EventArgs e)
        {
            txtFolder.Text = _settings.Folder;
            txtRaidFolder.Text = _settings.RaidFolder;
            txtUrl.Text = _settings.Url;
            txtUserId.Text = _settings.UserId;
            txtApiKey.Text = _settings.GetApiKey();
            chkRaidScreenshots.Checked = _settings.RaidScreenshots;
            chkDemoOcr.Checked = _settings.DemoOcrScreenshots;
        }

        private async void Form1_Shown(object? sender, EventArgs e)
        {
            // Resume syncing automatically when a previous run left complete settings;
            // otherwise wait for the user to fill them in and press Start.
            if (AddonFolders.Check(CurrentInput()) == null)
            {
                btnStart_Click(this, EventArgs.Empty);
            }
            await CheckForUpdateAsync();
        }

        private async void txtAddonFolder_TextChanged(object? sender, EventArgs e)
        {
            await RefreshVersionsAsync();
        }

        /// <summary>Shows the addon_version from each addon's newest data file under its folder box.</summary>
        private async Task RefreshVersionsAsync()
        {
            int lookup = ++_versionLookup;
            var players = txtFolder.Text.Trim();
            var raid = txtRaidFolder.Text.Trim();
            var (playersVersion, raidVersion) = await Task.Run(() => (
                AddonVersion.Newest(players, AddonFolders.PlayerDbDataFolders),
                AddonVersion.Newest(raid, AddonFolders.RaidsDbDataFolders)));
            if (lookup == _versionLookup)
            {
                lblPlayerVersion.Text = playersVersion ?? "";
                lblRaidVersion.Text = raidVersion ?? "";
            }
        }

        private void btnBrowse_Click(object? sender, EventArgs e)
        {
            BrowseAddonFolder(txtFolder, "PlayerDB", AddonFolders.PlayerDbDataFolders, Platform.FolderPickerStart());
        }

        private void btnBrowseRaid_Click(object? sender, EventArgs e)
        {
            BrowseAddonFolder(txtRaidFolder, "RaidsDB", AddonFolders.RaidsDbDataFolders, "");
        }

        private void BrowseAddonFolder(TextBox box, string addonName, string[] dataFolders, string fallback)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = $"Select the {addonName} addon folder (the one containing {AppSettings.AddonMarkerFile})",
                UseDescriptionForTitle = true,
                SelectedPath = Directory.Exists(box.Text) ? box.Text : fallback,
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            var path = AddonFolders.FromPicked(dialog.SelectedPath, dataFolders);
            box.Text = path;
            if (!AddonFolders.IsAddonFolder(path, dataFolders))
            {
                ShowError(AddonFolders.NotAddonMessage(path, addonName, dataFolders), box);
            }
        }

        private async void btnStart_Click(object? sender, EventArgs e)
        {
            if (_engine.Syncing)
            {
                StopSync();
                return;
            }

            var input = CurrentInput();
            if (AddonFolders.Check(input) is { } problem)
            {
                ShowError(problem.Message, BoxFor(problem.Field));
                return;
            }
            txtUrl.Text = AppSettings.NormalizeBaseUrl(txtUrl.Text);
            input = CurrentInput();

            var (_, firstRuns) = _engine.Start(input, chkRaidScreenshots.Checked, chkDemoOcr.Checked);
            SetInputsEnabled(false);
            btnStart.Text = "Stop";
            btnStart.Kind = ButtonKind.Secondary;
            ShowPage(pnlRunning);
            ShowScheduleRows(true);
            scheduleTimer.Start();
            await firstRuns;
        }

        private Control BoxFor(InputField field) => field switch
        {
            InputField.AddonFolder => txtFolder,
            InputField.RaidFolder => txtRaidFolder,
            InputField.Url => txtUrl,
            InputField.UserId => txtUserId,
            _ => txtApiKey,
        };

        /// <summary>Does whatever is due, and moves each row's bar on.</summary>
        private void scheduleTimer_Tick(object? sender, EventArgs e)
        {
            _engine.Tick();
            var now = DateTime.Now;
            foreach (var ev in _engine.Scheduled.Where(ev => ev.Enabled))
            {
                ShowProgress(ev, now);
            }
        }

        /// <summary>
        ///  The bar is full when the event was just done and empties until it's due; a stretch of it sweeps along while
        ///  it's being done. It's red while the event is failing.
        /// </summary>
        private void ShowProgress(TimedEvent ev, DateTime now)
        {
            if (!_rows.TryGetValue(ev, out var row))
            {
                return;
            }
            row.Bar.Running = ev.Running;
            row.Bar.FillColor = ev.Failing ? Theme.Bad : Theme.Accent;
            row.Bar.Fraction = (float)ev.Fraction(now);
        }

        /// <summary>Shown beside the event's bar (red while failing); dropped once the sync has stopped and the rows have gone.</summary>
        private void ShowStatus(TimedEvent ev)
        {
            if (_rows.TryGetValue(ev, out var row))
            {
                row.Status.Text = ev.Status;
                row.Status.ForeColor = ev.Failing ? Theme.Bad : Theme.Muted;
            }
        }

        /// <summary>
        ///  While syncing, a row for each event done this sync: its name, its bar and its status. While stopped, none,
        ///  just a note saying so. The log takes the room left below.
        /// </summary>
        private void ShowScheduleRows(bool syncing)
        {
            tblSchedule.SuspendLayout();
            foreach (var control in tblSchedule.Controls.Cast<Control>().ToList())
            {
                control.Dispose();
            }
            tblSchedule.RowStyles.Clear();
            tblSchedule.RowCount = 0;
            _rows.Clear();

            foreach (var ev in syncing ? _engine.Scheduled.Where(ev => ev.Enabled) : [])
            {
                var bar = new ScheduleBar
                {
                    Anchor = AnchorStyles.Left | AnchorStyles.Right,
                    Height = 16,
                    Fraction = 1,
                    Margin = new Padding(0, 5, 16, 5),
                };
                var status = new Label { AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Theme.Muted };
                _rows[ev] = (bar, status);
                // What the event already said while its row wasn't there (its first run starts at once).
                ShowStatus(ev);
                AddRow(tblSchedule,
                    new Label { Text = ev.Label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 5, 16, 5) },
                    bar,
                    status);
            }
            tblSchedule.ResumeLayout();

            tblSchedule.Height = tblSchedule.RowCount == 0 ? 0 : tblSchedule.GetPreferredSize(new Size(tblSchedule.Width, 0)).Height;
            lblNotSyncing.Visible = !syncing;
            cardSchedule.Height = (syncing ? tblSchedule.Bottom : lblNotSyncing.Bottom) + 16;
            var logTop = cardSchedule.Bottom + 12;
            cardLog.SetBounds(cardLog.Left, logTop, cardLog.Width, cardLog.Bottom - logTop);

            lblStatus.Text = syncing ? "● Syncing" : "Stopped";
            lblStatus.ForeColor = syncing ? Theme.Good : Theme.Muted;
            trayIcon.Text = syncing ? "PlayerDB Sync – syncing" : "PlayerDB Sync – stopped";
            LayoutHeader();
        }

        private static void AddRow(TableLayoutPanel table, params Control[] cells)
        {
            int row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            for (int column = 0; column < cells.Length; column++)
            {
                table.Controls.Add(cells[column], column, row);
            }
        }

        /// <summary>The page tabs one after another past the name, each as wide as its word; the status beside the Start button.</summary>
        private void LayoutHeader()
        {
            int x = lblBrand.Right + 16;
            foreach (var (tab, _) in _pages)
            {
                // Clear of the top edge, which resizes the window; the text and its underline stay centred.
                tab.SetBounds(x, 8, tab.GetPreferredSize(Size.Empty).Width, pnlHeader.Height - 16);
                x = tab.Right;
            }
            // Left of the update orb, while there is one.
            int right = btnUpdateOrb.Visible ? btnUpdateOrb.Left : btnStart.Left;
            lblStatus.Location = new Point(right - 12 - lblStatus.Width, (pnlHeader.Height - lblStatus.Height) / 2);
        }

        private void navTab_Click(object? sender, EventArgs e) => ShowPage(_pages.First(p => p.Tab == sender).Page);

        private void ShowPage(BackdropPanel page)
        {
            foreach (var (tab, p) in _pages)
            {
                tab.Active = p == page;
                p.Visible = p == page;
            }
        }

        /// <summary>The Info page: the version, what is synced where and how often, and the folders the app keeps things in.</summary>
        private void ShowInfo()
        {
            lblVersion.Text = AppInfo.Shown(AppInfo.Version);
            lblVersion.Left = lblAboutTitle.Right + 4;
            int x = lnkSite.Left;
            foreach (var link in new[] { lnkSite, lnkReleases, lnkReadme })
            {
                StyleLink(link);
                link.Left = x;
                x = link.Right + 20;
            }

            AddRow(tblUploads, InfoCell("From", Theme.Muted, Theme.Strong), InfoCell("Sent to", Theme.Muted, Theme.Strong), InfoCell("Every", Theme.Muted, Theme.Strong));
            foreach (var job in _engine.Jobs)
            {
                AddRow(tblUploads, InfoCell($"{job.AddonName} {job.DataFolderName} folder"), InfoCell(job.EndpointPath, Theme.Muted, Theme.Mono), InfoCell(job.Every));
            }
            AddRow(tblUploads, InfoCell("Raid screenshots"), InfoCell(RaidImageUploader.EndpointPath, Theme.Muted, Theme.Mono), InfoCell(Schedule.Every(Schedule.UploadInterval)));
            AddRow(tblUploads, InfoCell("Demo OCR screenshots"), InfoCell(DemoOcrUploader.EndpointPath, Theme.Muted, Theme.Mono), InfoCell(Schedule.Every(Schedule.UploadInterval)));
            AddRow(tblUploads, InfoCell("Raid item ignore list (downloaded)"), InfoCell(RaidIgnoreList.EndpointPath, Theme.Muted, Theme.Mono), InfoCell(Schedule.Every(Schedule.IgnoreListInterval)));

            foreach (var (name, folder) in new[]
            {
                ("Settings", AppSettings.SettingsFolder),
                ("Raid screenshots", RaidScreenshot.OutputFolder),
                ("Demo OCR screenshots", DemoOcr.OutputFolder),
            })
            {
                var link = new LinkLabel { Text = Platform.WithoutUserName(folder), AutoSize = true, Margin = new Padding(0, 3, 0, 3) };
                StyleLink(link);
                link.LinkClicked += (_, _) => OpenFolder(folder);
                AddRow(tblFiles, InfoCell(name, Theme.Muted), link);
            }

            // Each card as tall as its table, one under another.
            foreach (var (card, table) in new[] { (cardUploads, tblUploads), (cardFiles, tblFiles) })
            {
                table.Height = table.GetPreferredSize(new Size(table.Width, 0)).Height;
                card.Height = table.Bottom + 16;
            }
            cardFiles.Top = cardUploads.Bottom + 12;
        }

        private static Label InfoCell(string text, Color? color = null, Font? font = null)
        {
            var label = new Label { Text = text, AutoSize = true, ForeColor = color ?? Theme.Text, Margin = new Padding(0, 3, 24, 3) };
            if (font != null)
            {
                label.Font = font;
            }
            return label;
        }

        private static void StyleLink(LinkLabel link)
        {
            link.LinkColor = link.ActiveLinkColor = link.VisitedLinkColor = Theme.Accent;
            link.LinkBehavior = LinkBehavior.HoverUnderline;
        }

        private void lnkSite_LinkClicked(object? sender, LinkLabelLinkClickedEventArgs e)
        {
            // Only ever a web address: the box could hold anything, and opening it would run it.
            var url = AppSettings.NormalizeBaseUrl(txtUrl.Text);
            OpenUrl(AddonFolders.IsWebAddress(url) ? url : AppSettings.DefaultUrl);
        }

        private void lnkReleases_LinkClicked(object? sender, LinkLabelLinkClickedEventArgs e) => OpenUrl($"{AppInfo.RepositoryUrl}/releases/latest");

        /// <summary>
        ///  Shows btnUpdate, and the orb by Start/Stop, when GitHub's latest release is newer than this one (never for a dev build).
        /// </summary>
        private async Task CheckForUpdateAsync()
        {
            var tag = await UpdateCheck.NewerReleaseAsync(AppInfo.Version);
            if (tag == null || tag == _updateTag)
            {
                return;
            }
            _updateTag = tag;
            btnUpdate.Text = $"Update to {AppInfo.Shown(tag)}";
            btnUpdate.Width = TextRenderer.MeasureText(btnUpdate.Text, btnUpdate.Font).Width + 32;
            btnUpdate.Left = cardAbout.ClientSize.Width - 16 - btnUpdate.Width;
            btnUpdate.Visible = true;
            toolTip.SetToolTip(btnUpdateOrb, $"{AppInfo.Shown(tag)} is out: click to download it");
            btnUpdateOrb.Visible = true;
            LayoutHeader();
            Log($"{AppInfo.Shown(tag)} is out (this is {AppInfo.Shown(AppInfo.Version)}): Info > {btnUpdate.Text} to download it");
        }

        private void btnUpdate_Click(object? sender, EventArgs e) => OpenUrl(_updateTag == null
            ? $"{AppInfo.RepositoryUrl}/releases/latest"
            : $"{AppInfo.RepositoryUrl}/releases/tag/{Uri.EscapeDataString(_updateTag)}");

        private void lnkReadme_LinkClicked(object? sender, LinkLabelLinkClickedEventArgs e) => OpenUrl($"{AppInfo.RepositoryUrl}#readme");

        private void OpenUrl(string url)
        {
            try
            {
                Platform.Open(url);
            }
            catch (Exception ex)
            {
                Log($"Couldn't open {url}: {ex.Message}");
            }
        }

        /// <summary>Opens <paramref name="folder"/> in Explorer, creating it if nothing has been saved there yet.</summary>
        private void OpenFolder(string folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
                Platform.Open(folder);
            }
            catch (Exception ex)
            {
                Log($"Couldn't open {folder}: {ex.Message}");
            }
        }

        private void chkDemoOcr_CheckedChanged(object? sender, EventArgs e)
        {
            _engine.DemoOcrScreenshots = chkDemoOcr.Checked;
            if (_settings.DemoOcrScreenshots == chkDemoOcr.Checked)
            {
                return;
            }
            SaveSettings();
            if (chkDemoOcr.Checked)
            {
                _engine.NoteDemoOcrSwitchedOn();
            }
        }

        private void chkRaidScreenshots_CheckedChanged(object? sender, EventArgs e)
        {
            _engine.RaidScreenshots = chkRaidScreenshots.Checked;
            if (_settings.RaidScreenshots == chkRaidScreenshots.Checked)
            {
                return;
            }
            SaveSettings();
            if (chkRaidScreenshots.Checked)
            {
                _engine.NoteRaidScreenshotsSwitchedOn();
            }
        }

        /// <summary>
        ///  Moves the addons' *.dat files (Players, Houses, Regrades, Inventory, Raids) to the Recycle Bin, so files saved by an
        ///  addon version the server doesn't take stop failing every sync. Their sync records go too.
        /// </summary>
        private async void btnClearData_Click(object? sender, EventArgs e)
        {
            var folders = _engine.PlanClear(CurrentInput());
            int total = folders.Sum(f => f.Files.Length);
            if (total == 0)
            {
                MessageBox.Show(this, "There are no addon data files to clear.", "PlayerDB Sync", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // The addons would save their old data straight back if they're still running in the game.
            var clean = new TaskDialogButton("Clean");
            var nevermind = new TaskDialogButton("Nevermind");
            var confirm = new TaskDialogPage
            {
                Caption = "Clear addon data",
                Heading = "Please make sure you have disabled the addons and reloaded if you are ingame first",
                Text = $"{Words.Count(total, "addon data file")} will be moved to the Recycle Bin:\n\n"
                    + string.Join("\n", folders.Where(f => f.Files.Length > 0).Select(f => $"{f.Path}: {f.Files.Length}"))
                    + "\n\nThe addons start collecting again from scratch.",
                Icon = TaskDialogIcon.Warning,
                Buttons = { clean, nevermind },
                DefaultButton = nevermind,
            };
            if (TaskDialog.ShowDialog(this, confirm) != clean)
            {
                return;
            }

            SaveSettings();
            var result = _engine.Clear(folders);
            await RefreshVersionsAsync();

            TaskDialog.ShowDialog(this, new TaskDialogPage
            {
                Caption = "Clear addon data",
                Heading = "Please re-enable your addons",
                Text = result.Summary + "." + (result.Failed.Count > 0 ? $"\n\n{result.Failed.Count} couldn't be cleared; the log says why." : ""),
                Icon = result.Failed.Count > 0 ? TaskDialogIcon.Warning : TaskDialogIcon.Information,
                Buttons = { TaskDialogButton.OK },
            });
        }

        private void btnOpenScreenshots_Click(object? sender, EventArgs e) => OpenFolder(RaidScreenshot.OutputFolder);

        private void StopSync()
        {
            scheduleTimer.Stop();
            _engine.Stop();
            SetInputsEnabled(true);
            btnStart.Text = "Start";
            btnStart.Kind = ButtonKind.Primary;
            ShowScheduleRows(false);
        }

        private void ShowError(string message, Control focus)
        {
            // The box to fix may be on another page.
            if (_pages.FirstOrDefault(p => p.Page.Contains(focus)).Page is { } page)
            {
                ShowPage(page);
            }
            MessageBox.Show(this, message, "PlayerDB Sync", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            focus.Focus();
        }

        private void SetInputsEnabled(bool enabled)
        {
            // Read-only rather than disabled, so the boxes keep the theme's colours (Windows greys out disabled ones).
            foreach (var box in new[] { txtFolder, txtRaidFolder, txtUrl, txtUserId, txtApiKey })
            {
                box.ReadOnly = !enabled;
                box.ForeColor = enabled ? Theme.Text : Theme.Muted;
            }
            btnBrowse.Enabled = enabled;
            btnBrowseRaid.Enabled = enabled;
            btnClearData.Enabled = enabled;
        }

        private void SaveSettings()
        {
            _engine.Apply(CurrentInput(), chkRaidScreenshots.Checked, chkDemoOcr.Checked);
            _engine.SaveSettings();
        }

        private void Log(string message)
        {
            if (txtLog.Lines.Length >= MaxLogLines)
            {
                txtLog.Lines = txtLog.Lines[^(MaxLogLines / 2)..];
            }
            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        }

        private void Form1_FormClosing(object? sender, FormClosingEventArgs e)
        {
            scheduleTimer.Stop();
            _engine.Dispose();
            SaveSettings();
            // Gone at once, rather than lingering in the tray until the mouse passes over it.
            trayIcon.Visible = false;
        }

        private void btnMinimize_Click(object? sender, EventArgs e) => WindowState = FormWindowState.Minimized;

        private void trayIcon_MouseClick(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowFromTray();
            }
        }

        private void trayMenu_Opening(object? sender, System.ComponentModel.CancelEventArgs e) =>
            mnuStartStop.Text = _engine.Syncing ? "Stop syncing" : "Start syncing";

        private void mnuShow_Click(object? sender, EventArgs e) => ShowFromTray();

        private void mnuStartStop_Click(object? sender, EventArgs e)
        {
            // Starting with something to fix shows why, on the window.
            if (!_engine.Syncing && AddonFolders.Check(CurrentInput()) != null)
            {
                ShowFromTray();
            }
            btnStart_Click(sender, e);
        }

        /// <summary>The window back from the tray: shown, restored if it was minimised, and in front.</summary>
        private void ShowFromTray()
        {
            Show();
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }
            Activate();
        }

        // Minimised (the – button, Win+M, its menu), it's only in the tray: hidden, so it leaves the taskbar.
        // It keeps syncing; the tray icon brings it back.
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized && Visible)
            {
                Hide();
            }
        }

        private void btnExit_Click(object? sender, EventArgs e) => Close();

        private const int WsMinimizeBox = 0x20000;
        private const int WsSysMenu = 0x80000;
        private const int CsDropShadow = 0x20000;

        // Without Windows' frame: still minimised and restored by clicking it on the taskbar, with its menu there
        // (and Alt+Space), and a shadow to lift it off what's behind.
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style |= WsMinimizeBox | WsSysMenu;
                cp.ClassStyle |= CsDropShadow;
                return cp;
            }
        }

        // Its edges resize it, as the frame's would.
        protected override void WndProc(ref Message m)
        {
            if (!BorderlessWindow.HitTestEdge(this, ref m))
            {
                base.WndProc(ref m);
            }
        }
    }
}
