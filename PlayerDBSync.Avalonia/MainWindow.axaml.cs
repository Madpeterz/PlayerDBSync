using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace PlayerDBSync
{
    /// <summary>The cross-platform window: fills in the settings, shows what <see cref="SyncEngine"/> is doing, and sits in the tray.</summary>
    public partial class MainWindow : Window
    {
        private const int MaxLogLines = 500;

        private readonly AppSettings _settings = AppSettings.Load();
        private readonly SyncEngine _engine;
        private readonly DispatcherTimer _scheduleTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
        private readonly Backdrop? _backdrop;
        private readonly List<string> _log = [];
        private TrayIcon? _tray;
        private NativeMenuItem? _trayStartStop;

        // Bumped on each version lookup, so a slow earlier one can't overwrite a newer result.
        private int _versionLookup;

        // The newer release the update buttons offer, once one is found.
        private string? _updateTag;

        // The row each event has while syncing: its bar and its status.
        private readonly Dictionary<TimedEvent, (ProgressBar Bar, TextBlock Status)> _rows = new();

        private readonly (Button Tab, Control Page)[] _pages;

        public MainWindow()
        {
            InitializeComponent();
            _pages = [(tabConfig, pageConfig), (tabRunning, pageRunning), (tabInfo, pageInfo)];
            foreach (var (tab, page) in _pages)
            {
                tab.Click += (_, _) => ShowPage(page);
            }

            try
            {
                _backdrop = new Backdrop(backdropA, backdropB);
            }
            catch (Exception ex)
            {
                // The pictures are only decoration.
                System.Diagnostics.Debug.WriteLine($"No backdrop: {ex.Message}");
            }
            // As tapping your name on the site: a different background now. A hidden extra, so nothing shows it can be clicked.
            lblBrand.PointerPressed += (_, _) => _backdrop?.ChangeNow();

            _engine = new SyncEngine(_settings, Platform.CreateGameCapture(), SynchronizationContext.Current);
            _engine.Logged += Log;
            _engine.StatusChanged += ShowStatus;
            _engine.AddonDataChanged += () => _ = RefreshVersionsAsync();

            if (!OperatingSystem.IsWindows())
            {
                lblCaptureNote.Text = "Screenshots need xdotool and either maim or ImageMagick (import) installed, and the game shown by X11 or XWayland.";
                lblCaptureNote.IsVisible = true;
            }

            btnStart.Click += (_, _) => _ = ToggleSyncAsync();
            btnBrowse.Click += async (_, _) => await BrowseAddonFolderAsync(txtFolder, "PlayerDB", AddonFolders.PlayerDbDataFolders, Platform.FolderPickerStart());
            btnBrowseRaid.Click += async (_, _) => await BrowseAddonFolderAsync(txtRaidFolder, "RaidsDB", AddonFolders.RaidsDbDataFolders, "");
            btnClear.Click += async (_, _) => await ClearDataAsync();
            btnOpenShots.Click += (_, _) => OpenFolder(RaidScreenshot.OutputFolder);
            txtFolder.TextChanged += async (_, _) => await RefreshVersionsAsync();
            txtRaidFolder.TextChanged += async (_, _) => await RefreshVersionsAsync();
            chkRaid.IsCheckedChanged += (_, _) => RaidScreenshotsChanged();
            chkDemo.IsCheckedChanged += (_, _) => DemoOcrChanged();
            lnkSite.Click += (_, _) => OpenSite();
            lnkReleases.Click += (_, _) => OpenUrl($"{AppInfo.RepositoryUrl}/releases/latest");
            lnkReadme.Click += (_, _) => OpenUrl($"{AppInfo.RepositoryUrl}#readme");
            btnUpdate.Click += (_, _) => OpenUpdate();
            btnUpdateOrb.Click += (_, _) => OpenUpdate();
            _scheduleTimer.Tick += (_, _) => ScheduleTick();

            LoadSettings();
            ShowInfo();
            CreateTray();

            // A dev build always has the update orb, leading to the latest release; a release only once a newer one is out.
            if (!UpdateCheck.IsRelease(AppInfo.Version))
            {
                ToolTip.SetTip(btnUpdateOrb, $"Dev build ({AppInfo.Version}): click for the latest release");
                btnUpdateOrb.IsVisible = true;
            }

            var updateTimer = new DispatcherTimer { Interval = Schedule.UpdateCheckInterval };
            updateTimer.Tick += async (_, _) => await CheckForUpdateAsync();
            updateTimer.Start();

            Opened += async (_, _) => await OnOpenedAsync();
            Closing += (_, _) => OnClosing();
        }

        private void LoadSettings()
        {
            txtFolder.Text = _settings.Folder;
            txtRaidFolder.Text = _settings.RaidFolder;
            txtUrl.Text = _settings.Url;
            txtUserId.Text = _settings.UserId;
            txtApiKey.Text = _settings.GetApiKey();
            chkRaid.IsChecked = _settings.RaidScreenshots;
            chkDemo.IsChecked = _settings.DemoOcrScreenshots;
        }

        private SyncInput CurrentInput() =>
            new(txtFolder.Text ?? "", txtRaidFolder.Text ?? "", txtUrl.Text ?? "", txtUserId.Text ?? "", txtApiKey.Text ?? "");

        private async Task OnOpenedAsync()
        {
            // Resume syncing automatically when a previous run left complete settings;
            // otherwise wait for the user to fill them in and press Start.
            if (AddonFolders.Check(CurrentInput()) == null)
            {
                await ToggleSyncAsync();
            }
            await CheckForUpdateAsync();
        }

        // ---- start and stop ----

        private async Task ToggleSyncAsync()
        {
            if (_engine.Syncing)
            {
                StopSync();
                return;
            }

            var input = CurrentInput();
            if (AddonFolders.Check(input) is { } problem)
            {
                await ShowErrorAsync(problem.Message, BoxFor(problem.Field));
                return;
            }
            txtUrl.Text = AppSettings.NormalizeBaseUrl(txtUrl.Text ?? "");
            input = CurrentInput();

            var (_, firstRuns) = _engine.Start(input, chkRaid.IsChecked == true, chkDemo.IsChecked == true);
            SetInputsEnabled(false);
            btnStart.Content = "Stop";
            btnStart.Classes.Remove("primary");
            ShowPage(pageRunning);
            ShowScheduleRows(true);
            _scheduleTimer.Start();
            await firstRuns;
        }

        private void StopSync()
        {
            _scheduleTimer.Stop();
            _engine.Stop();
            SetInputsEnabled(true);
            btnStart.Content = "Start";
            btnStart.Classes.Add("primary");
            ShowScheduleRows(false);
        }

        private TextBox BoxFor(InputField field) => field switch
        {
            InputField.AddonFolder => txtFolder,
            InputField.RaidFolder => txtRaidFolder,
            InputField.Url => txtUrl,
            InputField.UserId => txtUserId,
            _ => txtApiKey,
        };

        private void SetInputsEnabled(bool enabled)
        {
            foreach (var box in new[] { txtFolder, txtRaidFolder, txtUrl, txtUserId, txtApiKey })
            {
                box.IsReadOnly = !enabled;
            }
            btnBrowse.IsEnabled = enabled;
            btnBrowseRaid.IsEnabled = enabled;
            btnClear.IsEnabled = enabled;
        }

        private async Task ShowErrorAsync(string message, TextBox focus)
        {
            // The box to fix may be on another page.
            ShowPage(pageConfig);
            await Dialogs.ShowAsync(this, "PlayerDB Sync", null, message);
            focus.Focus();
        }

        // ---- schedule ----

        /// <summary>Does whatever is due, and moves each row's bar on.</summary>
        private void ScheduleTick()
        {
            _engine.Tick();
            var now = DateTime.Now;
            foreach (var ev in _engine.Scheduled.Where(ev => ev.Enabled))
            {
                if (_rows.TryGetValue(ev, out var row))
                {
                    row.Bar.IsIndeterminate = ev.Running;
                    row.Bar.Value = ev.Fraction(now);
                    row.Bar.Classes.Set("failing", ev.Failing);
                }
            }
        }

        /// <summary>Shown beside the event's bar (red while failing); dropped once the sync has stopped and the rows have gone.</summary>
        private void ShowStatus(TimedEvent ev)
        {
            if (_rows.TryGetValue(ev, out var row))
            {
                row.Status.Text = ev.Status;
                row.Status.Foreground = (Avalonia.Media.IBrush?)Application.Current!.FindResource(ev.Failing ? "PdbBad" : "PdbMuted");
                row.Bar.Classes.Set("failing", ev.Failing);
            }
        }

        /// <summary>
        ///  While syncing, a row for each event done this sync: its name, its bar and its status. While stopped, none,
        ///  just a note saying so.
        /// </summary>
        private void ShowScheduleRows(bool syncing)
        {
            gridSchedule.Children.Clear();
            gridSchedule.RowDefinitions.Clear();
            _rows.Clear();

            int row = 0;
            foreach (var ev in syncing ? _engine.Scheduled.Where(ev => ev.Enabled) : [])
            {
                gridSchedule.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var label = new TextBlock { Text = ev.Label, Margin = new Thickness(0, 5, 16, 5), VerticalAlignment = VerticalAlignment.Center };
                var bar = new ProgressBar { Minimum = 0, Maximum = 1, Value = 1, Margin = new Thickness(0, 5, 16, 5), VerticalAlignment = VerticalAlignment.Center };
                var status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
                status.Classes.Add("muted");
                Grid.SetRow(label, row);
                Grid.SetRow(bar, row);
                Grid.SetColumn(bar, 1);
                Grid.SetRow(status, row);
                Grid.SetColumn(status, 2);
                gridSchedule.Children.Add(label);
                gridSchedule.Children.Add(bar);
                gridSchedule.Children.Add(status);
                _rows[ev] = (bar, status);
                // What the event already said while its row wasn't there (its first run starts at once).
                ShowStatus(ev);
                row++;
            }

            lblNotSyncing.IsVisible = !syncing;
            lblStatus.Text = syncing ? "● Syncing" : "Stopped";
            lblStatus.Foreground = (Avalonia.Media.IBrush?)Application.Current!.FindResource(syncing ? "PdbGood" : "PdbMuted");
            if (_tray != null)
            {
                _tray.ToolTipText = syncing ? "PlayerDB Sync – syncing" : "PlayerDB Sync – stopped";
            }
        }

        private void ShowPage(Control page)
        {
            foreach (var (tab, p) in _pages)
            {
                tab.Classes.Set("active", p == page);
                p.IsVisible = p == page;
            }
        }

        // ---- folders and versions ----

        private async Task BrowseAddonFolderAsync(TextBox box, string addonName, string[] dataFolders, string fallback)
        {
            var current = box.Text ?? "";
            var start = await StorageProvider.TryGetFolderFromPathAsync(Directory.Exists(current) ? current : fallback);
            var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = $"Select the {addonName} addon folder (the one containing {AppSettings.AddonMarkerFile})",
                AllowMultiple = false,
                SuggestedStartLocation = start,
            });
            if (picked.Count == 0 || picked[0].TryGetLocalPath() is not { } selected)
            {
                return;
            }

            var path = AddonFolders.FromPicked(selected, dataFolders);
            box.Text = path;
            if (!AddonFolders.IsAddonFolder(path, dataFolders))
            {
                await ShowErrorAsync(AddonFolders.NotAddonMessage(path, addonName, dataFolders), box);
            }
        }

        /// <summary>Shows the addon_version from each addon's newest data file under its folder box.</summary>
        private async Task RefreshVersionsAsync()
        {
            int lookup = ++_versionLookup;
            var players = (txtFolder.Text ?? "").Trim();
            var raid = (txtRaidFolder.Text ?? "").Trim();
            var (playersVersion, raidVersion) = await Task.Run(() => (
                AddonVersion.Newest(players, AddonFolders.PlayerDbDataFolders),
                AddonVersion.Newest(raid, AddonFolders.RaidsDbDataFolders)));
            if (lookup == _versionLookup)
            {
                lblPlayerVersion.Text = playersVersion ?? "";
                lblRaidVersion.Text = raidVersion ?? "";
            }
        }

        // ---- settings ----

        private void SaveSettings()
        {
            _engine.Apply(CurrentInput(), chkRaid.IsChecked == true, chkDemo.IsChecked == true);
            _engine.SaveSettings();
        }

        private void RaidScreenshotsChanged()
        {
            var on = chkRaid.IsChecked == true;
            _engine.RaidScreenshots = on;
            if (_settings.RaidScreenshots == on)
            {
                return;
            }
            SaveSettings();
            if (on)
            {
                _engine.NoteRaidScreenshotsSwitchedOn();
            }
        }

        private void DemoOcrChanged()
        {
            var on = chkDemo.IsChecked == true;
            _engine.DemoOcrScreenshots = on;
            if (_settings.DemoOcrScreenshots == on)
            {
                return;
            }
            SaveSettings();
            if (on)
            {
                _engine.NoteDemoOcrSwitchedOn();
            }
        }

        /// <summary>
        ///  Moves the addons' *.dat files to the Trash, so files saved by an addon version the server doesn't take stop
        ///  failing every sync. Their sync records go too.
        /// </summary>
        private async Task ClearDataAsync()
        {
            var folders = _engine.PlanClear(CurrentInput());
            int total = folders.Sum(f => f.Files.Length);
            if (total == 0)
            {
                await Dialogs.ShowAsync(this, "PlayerDB Sync", null, "There are no addon data files to clear.");
                return;
            }

            // The addons would save their old data straight back if they're still running in the game.
            var bin = OperatingSystem.IsWindows() ? "Recycle Bin" : "Trash";
            var answer = await Dialogs.AskAsync(this, "Clear addon data",
                "Please make sure you have disabled the addons and reloaded if you are ingame first",
                $"{Words.Count(total, "addon data file")} will be moved to the {bin}:\n\n"
                + string.Join("\n", folders.Where(f => f.Files.Length > 0).Select(f => $"{f.Path}: {f.Files.Length}"))
                + "\n\nThe addons start collecting again from scratch.",
                "Clean", "Nevermind");
            if (answer != "Clean")
            {
                return;
            }

            SaveSettings();
            var result = _engine.Clear(folders);
            await RefreshVersionsAsync();
            await Dialogs.ShowAsync(this, "Clear addon data", "Please re-enable your addons",
                result.Summary + "." + (result.Failed.Count > 0 ? $"\n\n{result.Failed.Count} couldn't be cleared; the log says why." : ""));
        }

        // ---- info and updates ----

        /// <summary>The Info page: the version, what is synced where and how often, and the folders the app keeps things in.</summary>
        private void ShowInfo()
        {
            lblVersion.Text = AppInfo.Shown(AppInfo.Version);

            AddInfoRow(gridUploads, InfoCell("From", muted: true, bold: true), InfoCell("Sent to", muted: true, bold: true), InfoCell("Every", muted: true, bold: true));
            foreach (var job in _engine.Jobs)
            {
                AddInfoRow(gridUploads, InfoCell($"{job.AddonName} {job.DataFolderName} folder"), InfoCell(job.EndpointPath, mono: true), InfoCell(job.Every));
            }
            AddInfoRow(gridUploads, InfoCell("Raid screenshots"), InfoCell(RaidImageUploader.EndpointPath, mono: true), InfoCell(Schedule.Every(Schedule.UploadInterval)));
            AddInfoRow(gridUploads, InfoCell("Demo OCR screenshots"), InfoCell(DemoOcrUploader.EndpointPath, mono: true), InfoCell(Schedule.Every(Schedule.UploadInterval)));
            AddInfoRow(gridUploads, InfoCell("Raid item ignore list (downloaded)"), InfoCell(RaidIgnoreList.EndpointPath, mono: true), InfoCell(Schedule.Every(Schedule.IgnoreListInterval)));

            foreach (var (name, folder) in new[]
            {
                ("Settings", AppSettings.SettingsFolder),
                ("Raid screenshots", RaidScreenshot.OutputFolder),
                ("Demo OCR screenshots", DemoOcr.OutputFolder),
            })
            {
                var link = new Button { Content = Platform.WithoutUserName(folder), HorizontalAlignment = HorizontalAlignment.Left };
                link.Classes.Add("link");
                link.Click += (_, _) => OpenFolder(folder);
                AddInfoRow(gridFiles, InfoCell(name, muted: true), link);
            }
        }

        private static TextBlock InfoCell(string text, bool muted = false, bool bold = false, bool mono = false)
        {
            var cell = new TextBlock { Text = text, Margin = new Thickness(0, 3), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            if (muted)
            {
                cell.Classes.Add("muted");
            }
            if (bold)
            {
                cell.FontWeight = Avalonia.Media.FontWeight.SemiBold;
            }
            if (mono)
            {
                cell.Classes.Add("mono");
            }
            return cell;
        }

        private static void AddInfoRow(Grid grid, params Control[] cells)
        {
            int row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (int column = 0; column < cells.Length; column++)
            {
                Grid.SetRow(cells[column], row);
                Grid.SetColumn(cells[column], column);
                grid.Children.Add(cells[column]);
            }
        }

        private void OpenSite()
        {
            // Only ever a web address: the box could hold anything, and opening it would run it.
            var url = AppSettings.NormalizeBaseUrl(txtUrl.Text ?? "");
            OpenUrl(AddonFolders.IsWebAddress(url) ? url : AppSettings.DefaultUrl);
        }

        private void OpenUpdate() => OpenUrl(_updateTag == null
            ? $"{AppInfo.RepositoryUrl}/releases/latest"
            : $"{AppInfo.RepositoryUrl}/releases/tag/{Uri.EscapeDataString(_updateTag)}");

        /// <summary>Shows btnUpdate, and the orb by Start/Stop, when GitHub's latest release is newer than this one (never for a dev build).</summary>
        private async Task CheckForUpdateAsync()
        {
            var tag = await UpdateCheck.NewerReleaseAsync(AppInfo.Version);
            if (tag == null || tag == _updateTag)
            {
                return;
            }
            _updateTag = tag;
            btnUpdate.Content = $"Update to {AppInfo.Shown(tag)}";
            btnUpdate.IsVisible = true;
            ToolTip.SetTip(btnUpdateOrb, $"{AppInfo.Shown(tag)} is out: click to download it");
            btnUpdateOrb.IsVisible = true;
            Log($"{AppInfo.Shown(tag)} is out (this is {AppInfo.Shown(AppInfo.Version)}): Info > {btnUpdate.Content} to download it");
        }

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

        /// <summary>Opens <paramref name="folder"/> in the file manager, creating it if nothing has been saved there yet.</summary>
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

        // ---- log ----

        private void Log(string message)
        {
            if (_log.Count >= MaxLogLines)
            {
                _log.RemoveRange(0, _log.Count - MaxLogLines / 2);
            }
            _log.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
            txtLog.Text = string.Join('\n', _log);
            txtLog.CaretIndex = txtLog.Text.Length;
        }

        // ---- tray ----

        /// <summary>
        ///  An icon in the tray (where the desktop has one): click it to bring the window back, or use its menu to show the
        ///  window, start or stop syncing, or exit.
        /// </summary>
        private void CreateTray()
        {
            var show = new NativeMenuItem("Show");
            show.Click += (_, _) => ShowFromTray();
            _trayStartStop = new NativeMenuItem("Start syncing");
            _trayStartStop.Click += async (_, _) =>
            {
                // Starting with something to fix shows why, on the window.
                if (!_engine.Syncing && AddonFolders.Check(CurrentInput()) != null)
                {
                    ShowFromTray();
                }
                await ToggleSyncAsync();
            };
            var exit = new NativeMenuItem("Exit");
            exit.Click += (_, _) => Close();

            var menu = new NativeMenu { show, _trayStartStop, new NativeMenuItemSeparator(), exit };
            menu.Opening += (_, _) => _trayStartStop.Header = _engine.Syncing ? "Stop syncing" : "Start syncing";

            _tray = new TrayIcon { Icon = Icon, ToolTipText = "PlayerDB Sync – stopped", Menu = menu };
            _tray.Clicked += (_, _) => ShowFromTray();
            TrayIcon.SetIcons(Application.Current!, [_tray]);
        }

        /// <summary>The window back from the tray: shown, restored if it was minimised, and in front.</summary>
        private void ShowFromTray()
        {
            Show();
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Activate();
        }

        private void OnClosing()
        {
            _scheduleTimer.Stop();
            _engine.Dispose();
            SaveSettings();
            // Gone at once, rather than lingering in the tray.
            _tray?.Dispose();
        }
    }
}
