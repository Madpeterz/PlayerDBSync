namespace PlayerDBSync
{
    public partial class Form1 : Form
    {
        private const int MaxLogLines = 500;
        private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan ScreenshotCheckInterval = TimeSpan.FromSeconds(2);

        // The addon gives up on a DemoOCR request after 60 s and rewrites its location every second, so check often.
        private static readonly TimeSpan DemoOcrCheckInterval = TimeSpan.FromSeconds(1);

        // How often each addon folder is synced (each wait a random pick in this range, so the folders don't all check
        // at once), the raid item ignore list downloaded and waiting screenshots uploaded. A sync with nothing changed
        // only reads the files' sizes and dates; it sends nothing.
        private static readonly TimeSpan SyncIntervalMin = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan SyncIntervalMax = TimeSpan.FromSeconds(45);
        private static readonly TimeSpan IgnoreListInterval = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan UploadInterval = TimeSpan.FromSeconds(30);

        // How often GitHub is asked for a newer release, while the app runs.
        private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(6);

        // Steps in a schedule row's bar; enough that a 30 min backoff still moves every few seconds.
        private const int BarSteps = 1000;

        private const string DemoOcrNeedsDemosFolder =
            "Demo OCR: needs the PlayerDB addon's Demos folder; update the addon, run the game with it enabled, then stop and start again";

        private readonly AppSettings _settings = AppSettings.Load();
        private readonly SyncJob _players;
        private readonly SyncJob _homes;
        private readonly SyncJob _regrades;
        private readonly SyncJob _inventory;
        private readonly SyncJob _raids;
        private CancellationTokenSource? _cts;

        // While RaidsDB is synced: the raid item ignore list, downloaded every IgnoreListInterval, and the raid
        // screenshots waiting in RaidScreenshot.OutputFolder, uploaded every UploadInterval.
        private readonly PeriodicTask _ignoreList;
        private readonly PeriodicTask _imageUploads;
        private readonly RaidImageUploader _imageUploader;

        // Raid screenshots are taken on their own thread, started with the sync and ended by its cancellation.
        private Thread? _screenshotThread;
        // Mirrors the checkbox so the screenshot thread can read it.
        private volatile bool _screenshotsOn;

        // Demo OCR screenshots waiting in DemoOcr.OutputFolder, uploaded every UploadInterval while the PlayerDB Demos folder is synced.
        private readonly PeriodicTask _demoOcrUploads;
        private readonly DemoOcrUploader _demoOcrUploader;
        private string _demosFolder = "";

        // Demo OCR screenshots are taken on their own thread, started with the sync and ended by its cancellation.
        private Thread? _demoOcrThread;
        // Mirrors the checkbox so the Demo OCR thread can read it.
        private volatile bool _demoOcrOn;

        // Bumped on each version lookup, so a slow earlier one can't overwrite a newer result.
        private int _versionLookup;

        // The newer release btnUpdate offers, once one is found.
        private string? _updateTag;

        // Snapshot of the server and credentials taken when sync starts, so edits to the boxes don't affect a running sync.
        private string _baseUrl = "";
        private ApiCredentials _credentials;

        /// <summary>
        ///  Something done every <see cref="Interval"/> (or, given a <see cref="MaxInterval"/>, a random wait between the
        ///  two each time) while syncing, shown as a row (<see cref="Label"/>, a bar and a status) whose bar empties until
        ///  it is done again; scheduleTimer does it once the bar is empty. The wait can be longer still (a backoff); the
        ///  bar then covers the longer wait.
        /// </summary>
        private abstract class TimedEvent(string label, TimeSpan interval, TimeSpan? maxInterval = null)
        {
            public string Label { get; } = label;
            public TimeSpan Interval { get; } = interval;
            public TimeSpan MaxInterval { get; } = maxInterval ?? interval;

            /// <summary>The wait before it's next done.</summary>
            public TimeSpan NextWait() => Interval + (MaxInterval - Interval) * Random.Shared.NextDouble();

            /// <summary>"30–45 s", or "15 min" for a fixed interval, for the log and the Info page.</summary>
            public string Every => Form1.Every(Interval, MaxInterval);

            public abstract bool Enabled { get; }
            public bool Running { get; set; }

            // Its last run failed; its bar and status turn red.
            public bool Failing { get; set; }

            // The wait before it is next done: from when it was last done (or sync started) until it's due.
            public DateTime WaitStart { get; private set; }
            public DateTime DueAt { get; private set; }

            public void WaitUntil(DateTime dueAt)
            {
                WaitStart = DateTime.Now;
                DueAt = dueAt;
            }

            // Its row, while syncing.
            public ScheduleBar? Bar { get; set; }
            public Label? StatusLabel { get; set; }
        }

        /// <summary>
        ///  One addon data folder pushed to one API endpoint on its own schedule, with its own backoff,
        ///  so a failing endpoint doesn't hold up the others.
        /// </summary>
        private sealed class SyncJob(string name, bool inRaidsDb, string dataFolderName, string endpointPath,
            Dictionary<string, SyncRecord> syncedFiles, Action<string> log) : TimedEvent(name, SyncIntervalMin, SyncIntervalMax)
        {
            public string Name { get; } = name;

            /// <summary>True if the data folder is in the RaidsDB addon folder, false if in the PlayerDB one.</summary>
            public bool InRaidsDb { get; } = inRaidsDb;
            public string AddonName => InRaidsDb ? "RaidsDB" : "PlayerDB";
            public string DataFolderName { get; } = dataFolderName;
            public string EndpointPath { get; } = endpointPath;
            public DatUploader Uploader { get; } = new(m => log($"{name}: {m}"));
            public Dictionary<string, SyncRecord> SyncedFiles { get; } = syncedFiles;

            // Taken when sync starts. An empty Folder means this job is skipped.
            public string Folder { get; set; } = "";
            public string Url { get; set; } = "";
            public override bool Enabled => Folder.Length > 0;

            // Backoff state while the server (or folder) is failing.
            public int FailureCount { get; set; }
            public string? LastError { get; set; }
        }

        /// <summary>
        ///  Work repeated while syncing, unless <paramref name="enabled"/> says it's not needed for this sync.
        ///  <paramref name="run"/> returns why it failed, or null. A failure is logged as "Name: failure: why" when the
        ///  reason changes and the next run retries; once it works again, "Name: recovered" is logged.
        /// </summary>
        private sealed class PeriodicTask(string name, string label, string failure, string recovered, TimeSpan interval,
            Func<bool> enabled, Func<CancellationToken, Task<string?>> run) : TimedEvent(label, interval)
        {
            public string Name { get; } = name;
            public string Failure { get; } = failure;
            public string Recovered { get; } = recovered;
            public override bool Enabled => enabled();
            public Func<CancellationToken, Task<string?>> Run { get; } = run;

            public string? LastError { get; set; }
        }

        private IEnumerable<SyncJob> Jobs => [_players, _homes, _regrades, _inventory, _raids];

        private IEnumerable<PeriodicTask> Tasks => [_ignoreList, _imageUploads, _demoOcrUploads];

        // In the order their rows are shown.
        private IEnumerable<TimedEvent> Scheduled => Jobs.Concat<TimedEvent>(Tasks);

        // The PlayerDB addon keeps players, houses, regrades (once HomeDB and RegradeDB) and inventory side by side;
        // each is synced if its folder is there.
        private static readonly string[] PlayerDbDataFolders =
            [AppSettings.PlayersFolder, AppSettings.HousesFolder, AppSettings.RegradesFolder, AppSettings.InventoryFolder];

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

            _players = new SyncJob("Players", inRaidsDb: false, AppSettings.PlayersFolder, "/api/players", _settings.SyncedFiles, Log);
            _homes = new SyncJob("Homes", inRaidsDb: false, AppSettings.HousesFolder, "/api/homes", _settings.HomeSyncedFiles, Log);
            _regrades = new SyncJob("Regrades", inRaidsDb: false, AppSettings.RegradesFolder, "/api/regrades", _settings.RegradeSyncedFiles, Log);
            _inventory = new SyncJob("Inventory", inRaidsDb: false, AppSettings.InventoryFolder, "/api/inventory", _settings.InventorySyncedFiles, Log);
            _raids = new SyncJob("Raids", inRaidsDb: true, AppSettings.RaidsFolder, "/api/raids", _settings.RaidSyncedFiles, Log);

            _imageUploader = new RaidImageUploader(m => Log($"Screenshots: {m}"));
            _demoOcrUploader = new DemoOcrUploader(m => Log($"Demo OCR: {m}"));
            _ignoreList = new PeriodicTask("Raids", "Raid ignore list", "couldn't update the item ignore list", "ignore list download working again",
                IgnoreListInterval, () => _raids.Enabled, RefreshIgnoreListAsync);
            _imageUploads = new PeriodicTask("Screenshots", "Screenshots", "couldn't upload", "uploading working again",
                UploadInterval, () => _raids.Enabled,
                async ct => (await _imageUploader.UploadNextAsync(_baseUrl + RaidImageUploader.EndpointPath, _credentials, ct)).Error);
            _demoOcrUploads = new PeriodicTask("Demo OCR", "Demo OCR", "couldn't upload", "uploading working again",
                UploadInterval, () => _demosFolder.Length > 0,
                async ct => (await _demoOcrUploader.UploadNextAsync(_baseUrl + DemoOcrUploader.EndpointPath, _credentials, ct)).Error);

            ShowInfo();

            // A dev build always has the update orb, leading to the latest release; a release only once a newer one is out.
            if (!UpdateCheck.IsRelease(AppVersion))
            {
                toolTip.SetToolTip(btnUpdateOrb, $"Dev build ({AppVersion}): click for the latest release");
                btnUpdateOrb.Visible = true;
                LayoutHeader();
            }

            var updateTimer = new System.Windows.Forms.Timer { Interval = (int)UpdateCheckInterval.TotalMilliseconds };
            updateTimer.Tick += async (_, _) => await CheckForUpdateAsync();
            updateTimer.Start();
            Disposed += (_, _) => updateTimer.Dispose();
        }

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
            if (InputProblem() == null)
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
                AddonVersion.Newest(players, PlayerDbDataFolders),
                AddonVersion.Newest(raid, [AppSettings.RaidsFolder])));
            if (lookup == _versionLookup)
            {
                lblPlayerVersion.Text = playersVersion ?? "";
                lblRaidVersion.Text = raidVersion ?? "";
            }
        }

        private void btnBrowse_Click(object? sender, EventArgs e)
        {
            BrowseAddonFolder(txtFolder, "PlayerDB", PlayerDbDataFolders, AppSettings.DefaultFolder);
        }

        private void btnBrowseRaid_Click(object? sender, EventArgs e)
        {
            BrowseAddonFolder(txtRaidFolder, "RaidsDB", [AppSettings.RaidsFolder], "");
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

            var path = dialog.SelectedPath;
            // Picking the data folder itself is an easy slip; use the addon folder above it.
            var parent = Path.GetDirectoryName(path);
            if (!IsAddonFolder(path, dataFolders)
                && dataFolders.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                && parent != null && IsAddonFolder(parent, dataFolders))
            {
                path = parent;
            }
            box.Text = path;
            if (!IsAddonFolder(path, dataFolders))
            {
                ShowError(NotAddonMessage(path, addonName, dataFolders), box);
            }
        }

        private async void btnStart_Click(object? sender, EventArgs e)
        {
            if (_cts != null)
            {
                StopSync();
                return;
            }

            if (!ValidateInputs())
            {
                return;
            }

            SaveSettings();

            _settings.SyncTarget = Retarget(_players, _settings.SyncTarget);
            _settings.HomeSyncTarget = Retarget(_homes, _settings.HomeSyncTarget);
            _settings.RegradeSyncTarget = Retarget(_regrades, _settings.RegradeSyncTarget);
            _settings.InventorySyncTarget = Retarget(_inventory, _settings.InventorySyncTarget);
            _settings.RaidSyncTarget = Retarget(_raids, _settings.RaidSyncTarget);

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            SetInputsEnabled(false);
            btnStart.Text = "Stop";
            btnStart.Kind = ButtonKind.Secondary;
            ShowPage(pnlRunning);
            foreach (var job in Jobs)
            {
                job.Uploader.ResetCache();
                job.FailureCount = 0;
                job.LastError = null;
                if (job.Enabled)
                {
                    Log($"{job.Name}: watching {job.Folder} (checking every {job.Every})");
                }
            }
            foreach (var task in Tasks)
            {
                task.LastError = null;
            }
            // Each is done once now (below); this only holds off scheduleTimer until that has started.
            foreach (var ev in Scheduled)
            {
                ev.Failing = false;
                ev.WaitUntil(DateTime.Now + ev.NextWait());
            }
            _imageUploader.ResetCache();
            _demoOcrUploader.ResetCache();
            if (_demosFolder.Length > 0)
            {
                Log($"Demo OCR: uploading new screenshots from {DemoOcr.OutputFolder} every {_demoOcrUploads.Every}");
                var demosFolder = _demosFolder;
                _demoOcrThread = StartWatcher(_demoOcrThread, "Demo OCR screenshots", () => WatchDemoOcrRequests(demosFolder, token));
            }
            else if (chkDemoOcr.Checked)
            {
                Log(DemoOcrNeedsDemosFolder);
            }
            if (_raids.Enabled)
            {
                Log($"Raids: downloading the item ignore list every {_ignoreList.Every}");
                Log($"Screenshots: uploading new ones from {RaidScreenshot.OutputFolder} every {_imageUploads.Every}, up to {RaidImageUploader.MaxPerUpload} at a time");
                var raidsFolder = _raids.Folder;
                _screenshotThread = StartWatcher(_screenshotThread, "Raid screenshots", () => WatchScreenshotRequests(raidsFolder, token));
            }

            ShowScheduleRows(true);
            var firstRuns = Task.WhenAll(Jobs.Select(RunSyncAsync)
                .Append(RunAsync(_ignoreList))
                .Append(StartImageUploadsAsync())
                .Append(RunAsync(_demoOcrUploads)));
            scheduleTimer.Start();
            await firstRuns;
        }

        /// <summary>Does whatever is due, and moves each row's bar on.</summary>
        private void scheduleTimer_Tick(object? sender, EventArgs e)
        {
            var now = DateTime.Now;
            foreach (var ev in Scheduled.Where(ev => ev.Enabled))
            {
                if (!ev.Running && now >= ev.DueAt)
                {
                    // Both catch their own errors.
                    _ = ev switch
                    {
                        SyncJob job => RunSyncAsync(job),
                        PeriodicTask task => RunAsync(task),
                        _ => Task.CompletedTask,
                    };
                }
                ShowProgress(ev, now);
            }
        }

        /// <summary>
        ///  The bar is full when the event was just done and empties until it's due; a stretch of it sweeps along while
        ///  it's being done. It's red while the event is failing.
        /// </summary>
        private static void ShowProgress(TimedEvent ev, DateTime now)
        {
            if (ev.Bar == null)
            {
                return;
            }
            ev.Bar.Running = ev.Running;
            ev.Bar.FillColor = ev.Failing ? Theme.Bad : Theme.Accent;
            var wait = (ev.DueAt - ev.WaitStart).TotalMilliseconds;
            var left = (ev.DueAt - now).TotalMilliseconds;
            ev.Bar.Fraction = wait <= 0 ? 0 : (float)(left / wait);
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
            foreach (var ev in Scheduled)
            {
                ev.Bar = null;
                ev.StatusLabel = null;
            }

            foreach (var ev in syncing ? Scheduled.Where(ev => ev.Enabled) : [])
            {
                ev.Bar = new ScheduleBar
                {
                    Anchor = AnchorStyles.Left | AnchorStyles.Right,
                    Height = 16,
                    Fraction = 1,
                    Margin = new Padding(0, 5, 16, 5),
                };
                ev.StatusLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Theme.Muted };
                AddRow(tblSchedule,
                    new Label { Text = ev.Label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 5, 16, 5) },
                    ev.Bar,
                    ev.StatusLabel);
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

        private const string RepositoryUrl = $"https://github.com/{UpdateCheck.Repository}";

        // A release's version is the tag it was built from, a dev build's its commit (see the csproj); ProductVersion adds "+<commit>".
        private static string AppVersion => Application.ProductVersion.Split('+')[0];

        private static string ShownVersion(string version) => UpdateCheck.IsRelease(version) ? $"v{version.TrimStart('v', 'V')}" : version;

        /// <summary>The Info page: the version, what is synced where and how often, and the folders the app keeps things in.</summary>
        private void ShowInfo()
        {
            lblVersion.Text = ShownVersion(AppVersion);
            lblVersion.Left = lblAboutTitle.Right + 4;
            int x = lnkSite.Left;
            foreach (var link in new[] { lnkSite, lnkReleases, lnkReadme })
            {
                StyleLink(link);
                link.Left = x;
                x = link.Right + 20;
            }

            AddRow(tblUploads, InfoCell("From", Theme.Muted, Theme.Strong), InfoCell("Sent to", Theme.Muted, Theme.Strong), InfoCell("Every", Theme.Muted, Theme.Strong));
            foreach (var job in Jobs)
            {
                AddRow(tblUploads, InfoCell($"{job.AddonName} {job.DataFolderName} folder"), InfoCell(job.EndpointPath, Theme.Muted, Theme.Mono), InfoCell(job.Every));
            }
            AddRow(tblUploads, InfoCell("Raid screenshots"), InfoCell(RaidImageUploader.EndpointPath, Theme.Muted, Theme.Mono), InfoCell(Every(UploadInterval)));
            AddRow(tblUploads, InfoCell("Demo OCR screenshots"), InfoCell(DemoOcrUploader.EndpointPath, Theme.Muted, Theme.Mono), InfoCell(Every(UploadInterval)));
            AddRow(tblUploads, InfoCell("Raid item ignore list (downloaded)"), InfoCell(RaidIgnoreList.EndpointPath, Theme.Muted, Theme.Mono), InfoCell(Every(IgnoreListInterval)));

            foreach (var (name, folder) in new[]
            {
                ("Settings", AppSettings.SettingsFolder),
                ("Raid screenshots", RaidScreenshot.OutputFolder),
                ("Demo OCR screenshots", DemoOcr.OutputFolder),
            })
            {
                var link = new LinkLabel { Text = WithoutUserName(folder), AutoSize = true, Margin = new Padding(0, 3, 0, 3) };
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

        /// <summary>
        ///  <paramref name="path"/> with the user's profile folder name shown as %user% (C:\Users\%user%\AppData\…), so it can
        ///  be shown, e.g. in a screenshot, without giving away the Windows user name.
        /// </summary>
        private static string WithoutUserName(string path)
        {
            // The profile folder's name, which isn't always the user name (e.g. an account renamed after it was made).
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\');
            var users = Path.GetDirectoryName(profile);
            if (users == null || !(path.Equals(profile, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(profile + "\\", StringComparison.OrdinalIgnoreCase)))
            {
                return path;
            }
            return Path.Combine(users, "%user%") + path[profile.Length..];
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
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                url = AppSettings.DefaultUrl;
            }
            OpenUrl(url);
        }

        private void lnkReleases_LinkClicked(object? sender, LinkLabelLinkClickedEventArgs e) => OpenUrl($"{RepositoryUrl}/releases/latest");

        /// <summary>
        ///  Shows btnUpdate, and the orb by Start/Stop, when GitHub's latest release is newer than this one (never for a dev build).
        /// </summary>
        private async Task CheckForUpdateAsync()
        {
            var tag = await UpdateCheck.NewerReleaseAsync(AppVersion);
            if (tag == null || tag == _updateTag)
            {
                return;
            }
            _updateTag = tag;
            btnUpdate.Text = $"Update to {ShownVersion(tag)}";
            btnUpdate.Width = TextRenderer.MeasureText(btnUpdate.Text, btnUpdate.Font).Width + 32;
            btnUpdate.Left = cardAbout.ClientSize.Width - 16 - btnUpdate.Width;
            btnUpdate.Visible = true;
            toolTip.SetToolTip(btnUpdateOrb, $"{ShownVersion(tag)} is out: click to download it");
            btnUpdateOrb.Visible = true;
            LayoutHeader();
            Log($"{ShownVersion(tag)} is out (this is {ShownVersion(AppVersion)}): Info > {btnUpdate.Text} to download it");
        }

        private void btnUpdate_Click(object? sender, EventArgs e) => OpenUrl(_updateTag == null
            ? $"{RepositoryUrl}/releases/latest"
            : $"{RepositoryUrl}/releases/tag/{Uri.EscapeDataString(_updateTag)}");

        private void lnkReadme_LinkClicked(object? sender, LinkLabelLinkClickedEventArgs e) => OpenUrl($"{RepositoryUrl}#readme");

        private void OpenUrl(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
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
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log($"Couldn't open {folder}: {ex.Message}");
            }
        }

        /// <summary>
        ///  The sync target <paramref name="job"/> will record against. If the folder, endpoint or user changed,
        ///  its records are cleared so everything is pushed again. A skipped job keeps its old records and target.
        /// </summary>
        private string Retarget(SyncJob job, string oldTarget)
        {
            if (!job.Enabled)
            {
                return oldTarget;
            }
            var target = $"{job.Folder}|{job.Url}|{_credentials.UserId}";
            if (!string.Equals(oldTarget, target, StringComparison.OrdinalIgnoreCase))
            {
                job.SyncedFiles.Clear();
            }
            return target;
        }

        /// <summary>"5 min", "30 s", for the log.</summary>
        private static string Every(TimeSpan interval) =>
            interval.TotalMinutes >= 1 ? $"{interval.TotalMinutes:0.#} min" : $"{interval.TotalSeconds:0} s";

        /// <summary>"30–45 s" for a range (one unit, when both are under a minute), or as <see cref="Every(TimeSpan)"/> for a single interval.</summary>
        private static string Every(TimeSpan min, TimeSpan max) =>
            min == max ? Every(min)
            : max.TotalMinutes < 1 ? $"{min.TotalSeconds:0}–{max.TotalSeconds:0} s"
            : $"{Every(min)} – {Every(max)}";

        /// <summary>
        ///  Starts a screenshot thread for this sync, once <paramref name="previous"/> (the last sync's, which may still be
        ///  finishing a screenshot) has had time to end. It runs all the time; it does nothing while its box is unticked.
        /// </summary>
        private static Thread StartWatcher(Thread? previous, string name, ThreadStart watch)
        {
            previous?.Join(TimeSpan.FromSeconds(10));
            var thread = new Thread(watch)
            {
                IsBackground = true,
                Name = name,
            };
            thread.Start();
            return thread;
        }

        private async Task RunAsync(PeriodicTask task)
        {
            if (task.Running || _cts == null || !task.Enabled)
            {
                return;
            }

            task.Running = true;
            var ct = _cts.Token;
            string? error;
            try
            {
                error = await task.Run(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            finally
            {
                task.Running = false;
            }

            task.WaitUntil(DateTime.Now + task.NextWait());
            task.Failing = error != null;
            if (error == null)
            {
                if (task.LastError != null)
                {
                    Log($"{task.Name}: {task.Recovered}");
                }
                task.LastError = null;
                SetStatus(task, $"Last checked {DateTime.Now:T}");
                return;
            }
            SetStatus(task, $"Failing – next try {task.DueAt:T}");

            // Only log when the error changes; the next run retries.
            error = $"{task.Name}: {task.Failure}: {error} – retrying in {task.Every}";
            if (error != task.LastError)
            {
                Log(error);
                task.LastError = error;
            }
        }

        /// <summary>Downloads the raid item ignore list into the RaidsDB addon folder; returns why it couldn't, if it couldn't.</summary>
        private async Task<string?> RefreshIgnoreListAsync(CancellationToken ct)
        {
            var (items, error) = await RaidIgnoreList.DownloadAsync(_baseUrl + RaidIgnoreList.EndpointPath, _credentials, ct);
            if (items == null)
            {
                return error;
            }
            var addonFolder = Path.GetDirectoryName(_raids.Folder)!;
            if (await Task.Run(() => RaidIgnoreList.Save(addonFolder, items), ct))
            {
                Log($"Raids: saved the item ignore list ({items.Count} items) to {Path.Combine(addonFolder, RaidIgnoreList.FileName)}");
            }
            return null;
        }

        /// <summary>When sync starts: scales down screenshots waiting from before, then uploads the first few.</summary>
        private async Task StartImageUploadsAsync()
        {
            if (_cts == null || !_imageUploads.Enabled)
            {
                return;
            }
            var ct = _cts.Token;
            try
            {
                await _imageUploader.ShrinkWaitingAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Log($"Screenshots: couldn't resize the waiting screenshots: {ex.Message}");
            }
            await RunAsync(_imageUploads);
        }

        private void chkDemoOcr_CheckedChanged(object? sender, EventArgs e)
        {
            _demoOcrOn = chkDemoOcr.Checked;
            if (_settings.DemoOcrScreenshots == chkDemoOcr.Checked)
            {
                return;
            }
            SaveSettings();
            if (chkDemoOcr.Checked && _cts != null && _demosFolder.Length == 0)
            {
                Log(DemoOcrNeedsDemosFolder);
            }
        }

        private void chkRaidScreenshots_CheckedChanged(object? sender, EventArgs e)
        {
            _screenshotsOn = chkRaidScreenshots.Checked;
            if (_settings.RaidScreenshots == chkRaidScreenshots.Checked)
            {
                return;
            }
            SaveSettings();
            if (chkRaidScreenshots.Checked && _cts != null && !_raids.Enabled)
            {
                Log("Screenshots: needs the RaidsDB addon folder; stop and set it to use this");
            }
        }

        /// <summary>
        ///  Moves the addons' *.dat files (Players, Houses, Regrades, Inventory, Raids) to the Recycle Bin, so files saved by an
        ///  addon version the server doesn't take stop failing every sync. Their sync records go too.
        /// </summary>
        private async void btnClearData_Click(object? sender, EventArgs e)
        {
            var folders = Jobs
                .Select(job => (Job: job, Path: DataFolder(AddonFolderOf(job), job.DataFolderName)))
                .Where(f => f.Path.Length > 0)
                .Select(f => (f.Job, f.Path, Files: Directory.GetFiles(f.Path, "*.dat", SearchOption.TopDirectoryOnly)))
                .ToList();
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

            int cleared = 0;
            var failed = new List<string>();
            foreach (var (job, _, files) in folders)
            {
                foreach (var file in files)
                {
                    try
                    {
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(file,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                        cleared++;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
                    {
                        failed.Add($"{Path.GetFileName(file)}: {ex.Message}");
                    }
                }
                // Anything collected from now on is new, whatever its file is called.
                job.SyncedFiles.Clear();
                job.Uploader.ResetCache();
            }
            SaveSettings();

            var summary = $"Cleared {Words.Count(cleared, "addon data file")} (moved to the Recycle Bin)";
            Log(summary);
            foreach (var failure in failed)
            {
                Log($"Couldn't clear {failure}");
            }
            await RefreshVersionsAsync();

            TaskDialog.ShowDialog(this, new TaskDialogPage
            {
                Caption = "Clear addon data",
                Heading = "Please re-enable your addons",
                Text = summary + "." + (failed.Count > 0 ? $"\n\n{failed.Count} couldn't be cleared; the log says why." : ""),
                Icon = failed.Count > 0 ? TaskDialogIcon.Warning : TaskDialogIcon.Information,
                Buttons = { TaskDialogButton.OK },
            });
        }

        private void btnOpenScreenshots_Click(object? sender, EventArgs e) => OpenFolder(RaidScreenshot.OutputFolder);

        /// <summary>
        ///  The screenshot thread: checks for request.sht every <see cref="ScreenshotCheckInterval"/> and answers it.
        ///  Capturing, encoding and file work all happen here, never on the UI thread.
        /// </summary>
        private void WatchScreenshotRequests(string raidsFolder, CancellationToken ct)
        {
            var addonFolder = Path.GetDirectoryName(raidsFolder)!;
            // The current request.sht already has its screenshot saved but couldn't be removed yet.
            bool requestAnswered = false;
            string? lastError = null;

            while (WaitForNextCheck(ct))
            {
                if (!_screenshotsOn)
                {
                    continue;
                }
                if (!RaidScreenshot.HasRequest(raidsFolder))
                {
                    requestAnswered = false;
                    continue;
                }
                if (requestAnswered)
                {
                    // Don't take a second screenshot for the same request; just finish removing it.
                    requestAnswered = !RaidScreenshot.TryRemoveRequest(raidsFolder);
                    continue;
                }

                string error;
                try
                {
                    var request = RaidScreenshot.ReadRequest(raidsFolder);
                    if (request == null)
                    {
                        // Most likely the game is still writing it; read again next check.
                        error = "Screenshot requested but request.sht couldn't be read yet – retrying";
                    }
                    else
                    {
                        var (image, captureError) = GameCapture.Capture();
                        if (image != null)
                        {
                            using (image)
                            {
                                // The request names the raid; older requests don't, so fall back to current.dat.
                                var name = request.Value.RaidStart ?? RaidScreenshot.RaidStartName(addonFolder);
                                if (name == null)
                                {
                                    LogFromThread("Screenshot: no raid is recording in current.dat, naming it after the current time");
                                    name = RaidScreenshot.FileStamp(DateTime.UtcNow);
                                }
                                var path = RaidScreenshot.NextPath(name, request.Value.Number, request.Value.Reason);
                                RaidScreenshot.SaveWebp(image, path);
                                LogFromThread($"Screenshot saved: {path}");
                            }
                            lastError = null;
                            requestAnswered = !RaidScreenshot.TryRemoveRequest(raidsFolder);
                            continue;
                        }
                        error = $"Screenshot requested but {captureError} – retrying";
                    }
                }
                catch (Exception ex)
                {
                    error = $"Screenshot error: {ex.Message} – retrying";
                }

                // The request is kept, so this retries every check; only log when the reason changes.
                if (error != lastError)
                {
                    LogFromThread(error);
                    lastError = error;
                }
            }
        }

        /// <summary>
        ///  The Demo OCR thread: checks the PlayerDB addon's Demos\request.dat every <see cref="DemoOcrCheckInterval"/>.
        ///  While it says <c>status = true</c> and location.dat is up to date, it captures the game, saves the picture
        ///  named after that location and answers by setting the request to <c>status = false</c>; the upload follows.
        /// </summary>
        private void WatchDemoOcrRequests(string demosFolder, CancellationToken ct)
        {
            // The current request already has its screenshot saved but couldn't be answered yet.
            bool requestAnswered = false;
            string? lastError = null;

            while (WaitForNextCheck(ct, DemoOcrCheckInterval))
            {
                if (!_demoOcrOn)
                {
                    continue;
                }
                var requested = DemoOcr.IsRequested(demosFolder);
                if (requested == null)
                {
                    // Most likely the game is writing it; read again next check.
                    continue;
                }
                if (requested == false)
                {
                    requestAnswered = false;
                    lastError = null;
                    continue;
                }
                if (requestAnswered)
                {
                    // Don't take a second screenshot for the same request; just finish answering it.
                    requestAnswered = !DemoOcr.TryMarkDone(demosFolder);
                    continue;
                }

                string error;
                try
                {
                    var location = DemoOcr.ReadLocation(demosFolder, out var upToDate);
                    if (location == null || !upToDate)
                    {
                        error = "Demo OCR requested but location.dat isn't up to date yet – waiting";
                    }
                    else
                    {
                        var (image, captureError) = GameCapture.Capture();
                        if (image != null)
                        {
                            using (image)
                            {
                                // The location must still be the one the picture was taken at.
                                var after = DemoOcr.ReadLocation(demosFolder, out var stillUpToDate);
                                if (image.Width != DemoOcr.Width || image.Height != DemoOcr.Height)
                                {
                                    error = $"Demo OCR requested but the game window is {image.Width}x{image.Height}, it needs to be {DemoOcr.Width}x{DemoOcr.Height} – retrying";
                                }
                                else if (after != location || !stillUpToDate)
                                {
                                    error = "Demo OCR requested but the location changed while taking the screenshot – retrying";
                                }
                                else
                                {
                                    var path = DemoOcr.PathFor(DateTime.UtcNow, location.Value);
                                    DemoOcr.SaveWebp(image, path);
                                    LogFromThread($"Demo OCR: screenshot saved at {location.Value.Sextant}: {path}");
                                    lastError = null;
                                    requestAnswered = !DemoOcr.TryMarkDone(demosFolder);
                                    BeginUploadDemoOcr();
                                    continue;
                                }
                            }
                        }
                        else
                        {
                            error = $"Demo OCR requested but {captureError} – retrying";
                        }
                    }
                }
                catch (Exception ex)
                {
                    error = $"Demo OCR error: {ex.Message} – retrying";
                }

                // The request is kept, so this retries every check; only log when the reason changes.
                if (error != lastError)
                {
                    LogFromThread(error);
                    lastError = error;
                }
            }
        }

        /// <summary>
        ///  Starts an upload from a background thread once the new screenshot has settled
        ///  (<see cref="DemoOcrUploader.SettleTime"/>), so it isn't left waiting for the next tick.
        /// </summary>
        private void BeginUploadDemoOcr()
        {
            try
            {
                BeginInvoke(async () =>
                {
                    await Task.Delay(DemoOcrUploader.SettleTime + TimeSpan.FromMilliseconds(250));
                    await RunAsync(_demoOcrUploads);
                });
            }
            catch (InvalidOperationException)
            {
            }
        }

        /// <summary>Waits until the next check; false once the sync has stopped.</summary>
        private static bool WaitForNextCheck(CancellationToken ct, TimeSpan? interval = null)
        {
            try
            {
                return !ct.WaitHandle.WaitOne(interval ?? ScreenshotCheckInterval);
            }
            catch (ObjectDisposedException)
            {
                // Stopping cancels and disposes the token source, possibly while a screenshot was being taken.
                return false;
            }
        }

        /// <summary>Logs from a background thread; dropped if the form has closed meanwhile.</summary>
        private void LogFromThread(string message)
        {
            try
            {
                BeginInvoke(() => Log(message));
            }
            catch (InvalidOperationException)
            {
            }
        }

        private async Task RunSyncAsync(SyncJob job)
        {
            if (job.Running || _cts == null || !job.Enabled)
            {
                return;
            }

            job.Running = true;
            var ct = _cts.Token;
            SetStatus(job, "Checking for changes…");
            try
            {
                var result = await job.Uploader.SyncAsync(job.Folder, job.Url, _credentials, job.SyncedFiles, ct);
                if (result.StateChanged)
                {
                    SaveSettings();
                }
                if (result.Uploaded > 0)
                {
                    // New files may come from an updated addon.
                    await RefreshVersionsAsync();
                }

                if (result.Error == null)
                {
                    OnSyncSucceeded(job);
                }
                else
                {
                    OnSyncFailed(job, result.Error);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                OnSyncFailed(job, $"Sync error: {ex.Message}");
            }
            finally
            {
                job.Running = false;
            }
        }

        private void OnSyncSucceeded(SyncJob job)
        {
            if (job.FailureCount > 0)
            {
                Log($"{job.Name}: connection restored");
            }
            job.FailureCount = 0;
            job.LastError = null;
            job.Failing = false;
            job.WaitUntil(DateTime.Now + job.NextWait());
            SetStatus(job, $"Last checked {DateTime.Now:T}");
        }

        private void OnSyncFailed(SyncJob job, string error)
        {
            job.FailureCount++;
            job.Failing = true;

            // A sync's wait (30–45 s), doubled for each failure in a row, up to 30 min.
            var delay = job.NextWait() * Math.Pow(2, Math.Min(job.FailureCount - 1, 10));
            if (delay > MaxBackoff)
            {
                delay = MaxBackoff;
            }
            job.WaitUntil(DateTime.Now + delay);

            // Only log when the error changes; repeats just update the status line.
            if (error != job.LastError)
            {
                Log($"{job.Name}: {error} – retrying with backoff");
                job.LastError = error;
            }
            SetStatus(job, $"Failing ({job.FailureCount}x) – next try {job.DueAt:T}");
        }

        /// <summary>Shown beside the event's bar (red while failing); dropped once the sync has stopped and the rows have gone.</summary>
        private static void SetStatus(TimedEvent ev, string status)
        {
            if (ev.StatusLabel != null)
            {
                ev.StatusLabel.Text = status;
                ev.StatusLabel.ForeColor = ev.Failing ? Theme.Bad : Theme.Muted;
            }
        }

        private void StopSync()
        {
            scheduleTimer.Stop();
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            SetInputsEnabled(true);
            btnStart.Text = "Start";
            btnStart.Kind = ButtonKind.Primary;
            ShowScheduleRows(false);
            Log("Stopped");
        }

        /// <summary>The first thing wrong with the settings in the boxes, and the box to fix it in; null if there's nothing.</summary>
        private (string Message, Control Box)? InputProblem()
        {
            // Either folder may be left empty to skip that sync, but not both.
            var playersFolder = txtFolder.Text.Trim();
            var raidFolder = txtRaidFolder.Text.Trim();
            if (playersFolder.Length == 0 && raidFolder.Length == 0)
            {
                return ("Select a PlayerDB or RaidsDB addon folder to sync.", txtFolder);
            }
            if (playersFolder.Length > 0 && !IsAddonFolder(playersFolder, PlayerDbDataFolders))
            {
                return (NotAddonMessage(playersFolder, "PlayerDB", PlayerDbDataFolders), txtFolder);
            }
            if (raidFolder.Length > 0 && !IsAddonFolder(raidFolder, AppSettings.RaidsFolder))
            {
                return (NotAddonMessage(raidFolder, "RaidsDB", [AppSettings.RaidsFolder]), txtRaidFolder);
            }

            var url = AppSettings.NormalizeBaseUrl(txtUrl.Text);
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                return ("Enter a valid http(s) server URL.", txtUrl);
            }

            if (!long.TryParse(txtUserId.Text.Trim(), out var userId) || userId <= 0)
            {
                return ("User ID must be a positive number.", txtUserId);
            }

            if (string.IsNullOrEmpty(txtApiKey.Text))
            {
                return ("Enter your API key.", txtApiKey);
            }
            return null;
        }

        /// <summary>Checks the settings in the boxes and, if they're fine, takes them for the sync about to start.</summary>
        private bool ValidateInputs()
        {
            if (InputProblem() is { } problem)
            {
                ShowError(problem.Message, problem.Box);
                return false;
            }

            var url = AppSettings.NormalizeBaseUrl(txtUrl.Text);
            txtUrl.Text = url;
            foreach (var job in Jobs)
            {
                var addonFolder = AddonFolderOf(job);
                job.Folder = DataFolder(addonFolder, job.DataFolderName);
                job.Url = url + job.EndpointPath;
                if (addonFolder.Length > 0 && !job.Enabled)
                {
                    Log($"{job.Name}: skipped, the {job.AddonName} addon has no {job.DataFolderName} folder yet (stop and start again once it has)");
                }
            }
            _demosFolder = DataFolder(txtFolder.Text.Trim(), AppSettings.DemosFolder);
            _baseUrl = url;
            _credentials = new ApiCredentials(long.Parse(txtUserId.Text.Trim()), txtApiKey.Text);
            return true;
        }

        /// <summary>The addon folder typed in for <paramref name="job"/>'s addon; "" if none is.</summary>
        private string AddonFolderOf(SyncJob job) => (job.InRaidsDb ? txtRaidFolder : txtFolder).Text.Trim();

        /// <summary>True if <paramref name="path"/> has the addon marker file and at least one of <paramref name="dataFolders"/>.</summary>
        private static bool IsAddonFolder(string path, params string[] dataFolders) =>
            File.Exists(Path.Combine(path, AppSettings.AddonMarkerFile)) && dataFolders.Any(d => Directory.Exists(Path.Combine(path, d)));

        /// <summary>The data folder to sync, or "" (skipped) when no addon folder is set or it hasn't that folder yet.</summary>
        private static string DataFolder(string addonFolder, string dataFolder) =>
            addonFolder.Length > 0 && Directory.Exists(Path.Combine(addonFolder, dataFolder)) ? Path.Combine(addonFolder, dataFolder) : "";

        private static string NotAddonMessage(string path, string addonName, string[] dataFolders) =>
            $"This doesn't look like the {addonName} addon folder:\n{path}\n\nIt should contain {AppSettings.AddonMarkerFile} and " +
            (dataFolders.Length == 1 ? $"a {dataFolders[0]} folder." : $"a {string.Join(", ", dataFolders[..^1])} or {dataFolders[^1]} folder.");

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
            _settings.Folder = txtFolder.Text.Trim();
            _settings.RaidFolder = txtRaidFolder.Text.Trim();
            _settings.Url = txtUrl.Text.Trim();
            _settings.UserId = txtUserId.Text.Trim();
            _settings.SetApiKey(txtApiKey.Text);
            _settings.RaidScreenshots = chkRaidScreenshots.Checked;
            _settings.DemoOcrScreenshots = chkDemoOcr.Checked;
            try
            {
                _settings.Save();
            }
            catch (Exception ex)
            {
                Log($"Could not save settings: {ex.Message}");
            }
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
            _cts?.Cancel();
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
            mnuStartStop.Text = _cts == null ? "Start syncing" : "Stop syncing";

        private void mnuShow_Click(object? sender, EventArgs e) => ShowFromTray();

        private void mnuStartStop_Click(object? sender, EventArgs e)
        {
            // Starting with something to fix shows why, on the window.
            if (_cts == null && InputProblem() != null)
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
