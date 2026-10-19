namespace PlayerDBSync
{
    public partial class Form1 : Form
    {
        private const int MaxLogLines = 500;
        private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan ScreenshotCheckInterval = TimeSpan.FromSeconds(2);

        private readonly AppSettings _settings = AppSettings.Load();
        private readonly SyncJob _players;
        private readonly SyncJob _homes;
        private readonly SyncJob _regrades;
        private readonly SyncJob _inventory;
        private readonly SyncJob _raids;
        private CancellationTokenSource? _cts;

        // The raid item ignore list, downloaded when sync starts and on ignoreTimer while RaidsDB is synced.
        private string _raidIgnoreUrl = "";
        private bool _ignoreRefreshing;
        private string? _lastIgnoreError;

        // Raid screenshots waiting in RaidScreenshot.OutputFolder, uploaded on imagesTimer while RaidsDB is synced.
        private readonly RaidImageUploader _imageUploader;
        private string _raidImagesUrl = "";
        private bool _imagesUploading;
        private string? _lastImagesError;

        // Raid screenshots are taken on their own thread, started with the sync and ended by its cancellation.
        private Thread? _screenshotThread;
        // Mirrors the checkbox so the screenshot thread can read it.
        private volatile bool _screenshotsOn;

        // Bumped on each version lookup, so a slow earlier one can't overwrite a newer result.
        private int _versionLookup;

        // Snapshot of the credentials taken when sync starts, so edits to the boxes don't affect a running sync.
        private long _userId;
        private string _apiKey = "";

        /// <summary>
        ///  One folder pushed to one API endpoint on its own timer, with its own backoff,
        ///  so a failing endpoint doesn't hold up the others.
        /// </summary>
        private sealed class SyncJob(string name, string endpointPath, System.Windows.Forms.Timer timer,
            DatUploader uploader, Dictionary<string, SyncRecord> syncedFiles)
        {
            public string Name { get; } = name;
            public string EndpointPath { get; } = endpointPath;
            public System.Windows.Forms.Timer Timer { get; } = timer;
            public DatUploader Uploader { get; } = uploader;
            public Dictionary<string, SyncRecord> SyncedFiles { get; } = syncedFiles;

            // Taken when sync starts. An empty Folder means this job is skipped.
            public string Folder { get; set; } = "";
            public string Url { get; set; } = "";
            public bool Enabled => Folder.Length > 0;

            public bool Syncing { get; set; }
            public string Status { get; set; } = "";

            // Backoff state while the server (or folder) is failing.
            public int FailureCount { get; set; }
            public DateTime NextAttempt { get; set; } = DateTime.MinValue;
            public string? LastError { get; set; }
        }

        private IEnumerable<SyncJob> Jobs => [_players, _homes, _regrades, _inventory, _raids];

        // The PlayerDB addon keeps players, houses, regrades (once HomeDB and RegradeDB) and inventory side by side;
        // each is synced if its folder is there.
        private static readonly string[] PlayerDbDataFolders =
            [AppSettings.PlayersFolder, AppSettings.HousesFolder, AppSettings.RegradesFolder, AppSettings.InventoryFolder];

        public Form1()
        {
            InitializeComponent();
            _players = new SyncJob("Players", "/api/players", syncTimer, new DatUploader(m => Log($"Players: {m}")), _settings.SyncedFiles);
            _homes = new SyncJob("Homes", "/api/homes", homesTimer, new DatUploader(m => Log($"Homes: {m}")), _settings.HomeSyncedFiles);
            _regrades = new SyncJob("Regrades", "/api/regrades", regradesTimer, new DatUploader(m => Log($"Regrades: {m}")), _settings.RegradeSyncedFiles);
            _inventory = new SyncJob("Inventory", "/api/inventory", inventoryTimer, new DatUploader(m => Log($"Inventory: {m}")), _settings.InventorySyncedFiles);
            _raids = new SyncJob("Raids", "/api/raids", raidsTimer, new DatUploader(m => Log($"Raids: {m}")), _settings.RaidSyncedFiles);
            _imageUploader = new RaidImageUploader(m => Log($"Screenshots: {m}"));
        }

        private void Form1_Load(object? sender, EventArgs e)
        {
            txtFolder.Text = _settings.Folder;
            txtRaidFolder.Text = _settings.RaidFolder;
            txtUrl.Text = _settings.Url;
            txtUserId.Text = _settings.UserId;
            txtApiKey.Text = _settings.GetApiKey();
            chkRaidScreenshots.Checked = _settings.RaidScreenshots;
        }

        private void Form1_Shown(object? sender, EventArgs e)
        {
            // Resume syncing automatically when a previous run left complete settings;
            // otherwise wait for the user to fill them in and press Start.
            var players = txtFolder.Text.Trim();
            var raid = txtRaidFolder.Text.Trim();
            if ((players.Length > 0 || raid.Length > 0)
                && (players.Length == 0 || IsAddonFolder(players, PlayerDbDataFolders))
                && (raid.Length == 0 || IsAddonFolder(raid, AppSettings.RaidsFolder))
                && txtUrl.Text.Trim().Length > 0
                && long.TryParse(txtUserId.Text.Trim(), out var id) && id > 0
                && txtApiKey.Text.Length > 0)
            {
                btnStart_Click(this, EventArgs.Empty);
            }
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
            SetInputsEnabled(false);
            btnStart.Text = "Stop";
            foreach (var job in Jobs)
            {
                job.Uploader.ResetCache();
                job.FailureCount = 0;
                job.NextAttempt = DateTime.MinValue;
                job.LastError = null;
                job.Status = "";
                if (job.Enabled)
                {
                    Log($"{job.Name}: watching {job.Folder} (checking every {TimeSpan.FromMilliseconds(job.Timer.Interval).TotalMinutes:0.#} min)");
                }
            }
            _lastIgnoreError = null;
            _imageUploader.ResetCache();
            _lastImagesError = null;
            if (_raids.Enabled)
            {
                Log($"Raids: downloading the item ignore list every {TimeSpan.FromMilliseconds(ignoreTimer.Interval).TotalMinutes:0.#} min");
                Log($"Screenshots: uploading new ones from {RaidScreenshot.OutputFolder} every {TimeSpan.FromMilliseconds(imagesTimer.Interval).TotalSeconds:0} s, up to {RaidImageUploader.MaxPerUpload} at a time");

                // Runs all the time; it does nothing while the box is unticked.
                // A thread from the previous run may still be finishing a screenshot.
                _screenshotThread?.Join(TimeSpan.FromSeconds(10));
                var raidsFolder = _raids.Folder;
                var token = _cts.Token;
                _screenshotThread = new Thread(() => WatchScreenshotRequests(raidsFolder, token))
                {
                    IsBackground = true,
                    Name = "Raid screenshots",
                };
                _screenshotThread.Start();
            }

            await Task.WhenAll(Jobs.Select(RunSyncAsync).Append(RefreshIgnoreListAsync()).Append(StartImageUploadsAsync()));
            if (_cts != null)
            {
                foreach (var job in Jobs.Where(j => j.Enabled))
                {
                    job.Timer.Start();
                }
                if (_raids.Enabled)
                {
                    ignoreTimer.Start();
                    imagesTimer.Start();
                }
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
            var target = $"{job.Folder}|{job.Url}|{_userId}";
            if (!string.Equals(oldTarget, target, StringComparison.OrdinalIgnoreCase))
            {
                job.SyncedFiles.Clear();
            }
            return target;
        }

        private async void syncTimer_Tick(object? sender, EventArgs e)
        {
            await RunSyncAsync(_players);
        }

        private async void homesTimer_Tick(object? sender, EventArgs e)
        {
            await RunSyncAsync(_homes);
        }

        private async void regradesTimer_Tick(object? sender, EventArgs e)
        {
            await RunSyncAsync(_regrades);
        }

        private async void inventoryTimer_Tick(object? sender, EventArgs e)
        {
            await RunSyncAsync(_inventory);
        }

        private async void raidsTimer_Tick(object? sender, EventArgs e)
        {
            await RunSyncAsync(_raids);
        }

        private async void ignoreTimer_Tick(object? sender, EventArgs e)
        {
            await RefreshIgnoreListAsync();
        }

        /// <summary>Downloads the raid item ignore list into the RaidsDB addon folder.</summary>
        private async Task RefreshIgnoreListAsync()
        {
            if (_ignoreRefreshing || _cts == null || !_raids.Enabled)
            {
                return;
            }

            _ignoreRefreshing = true;
            var ct = _cts.Token;
            string? error;
            try
            {
                var (items, downloadError) = await RaidIgnoreList.DownloadAsync(_raidIgnoreUrl, _userId, _apiKey, ct);
                error = downloadError;
                if (items != null)
                {
                    var addonFolder = Path.GetDirectoryName(_raids.Folder)!;
                    if (await Task.Run(() => RaidIgnoreList.Save(addonFolder, items), ct))
                    {
                        Log($"Raids: saved the item ignore list ({items.Count} items) to {Path.Combine(addonFolder, RaidIgnoreList.FileName)}");
                    }
                    if (_lastIgnoreError != null)
                    {
                        Log("Raids: ignore list download working again");
                    }
                    _lastIgnoreError = null;
                    return;
                }
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
                _ignoreRefreshing = false;
            }

            // Only log when the error changes; the next tick retries.
            error = $"Raids: couldn't update the item ignore list: {error} – retrying in {TimeSpan.FromMilliseconds(ignoreTimer.Interval).TotalMinutes:0.#} min";
            if (error != _lastIgnoreError)
            {
                Log(error);
                _lastIgnoreError = error;
            }
        }

        private async void imagesTimer_Tick(object? sender, EventArgs e)
        {
            await UploadImagesAsync();
        }

        /// <summary>When sync starts: scales down screenshots waiting from before, then uploads the first few.</summary>
        private async Task StartImageUploadsAsync()
        {
            if (_cts == null || !_raids.Enabled)
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
            await UploadImagesAsync();
        }

        /// <summary>Uploads the next few raid screenshots waiting to be sent.</summary>
        private async Task UploadImagesAsync()
        {
            if (_imagesUploading || _cts == null || !_raids.Enabled)
            {
                return;
            }

            _imagesUploading = true;
            var ct = _cts.Token;
            string? error;
            try
            {
                (_, error) = await _imageUploader.UploadNextAsync(_raidImagesUrl, _userId, _apiKey, ct);
                if (error == null)
                {
                    if (_lastImagesError != null)
                    {
                        Log("Screenshots: uploading working again");
                    }
                    _lastImagesError = null;
                    return;
                }
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
                _imagesUploading = false;
            }

            // Only log when the error changes; the next tick retries.
            error = $"Screenshots: couldn't upload: {error} – retrying in {TimeSpan.FromMilliseconds(imagesTimer.Interval).TotalSeconds:0} s";
            if (error != _lastImagesError)
            {
                Log(error);
                _lastImagesError = error;
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
            var players = txtFolder.Text.Trim();
            var raid = txtRaidFolder.Text.Trim();
            var folders = new[]
                {
                    (Job: _players, Path: DataFolder(players, AppSettings.PlayersFolder)),
                    (Job: _homes, Path: DataFolder(players, AppSettings.HousesFolder)),
                    (Job: _regrades, Path: DataFolder(players, AppSettings.RegradesFolder)),
                    (Job: _inventory, Path: DataFolder(players, AppSettings.InventoryFolder)),
                    (Job: _raids, Path: DataFolder(raid, AppSettings.RaidsFolder)),
                }
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
                Text = $"{total} addon data file{(total == 1 ? "" : "s")} will be moved to the Recycle Bin:\n\n"
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

            Log($"Cleared {cleared} addon data file{(cleared == 1 ? "" : "s")} (moved to the Recycle Bin)");
            foreach (var failure in failed)
            {
                Log($"Couldn't clear {failure}");
            }
            await RefreshVersionsAsync();

            TaskDialog.ShowDialog(this, new TaskDialogPage
            {
                Caption = "Clear addon data",
                Heading = "Please re-enable your addons",
                Text = $"Cleared {cleared} addon data file{(cleared == 1 ? "" : "s")} (moved to the Recycle Bin)."
                    + (failed.Count > 0 ? $"\n\n{failed.Count} couldn't be cleared; the log says why." : ""),
                Icon = failed.Count > 0 ? TaskDialogIcon.Warning : TaskDialogIcon.Information,
                Buttons = { TaskDialogButton.OK },
            });
        }

        private void btnOpenScreenshots_Click(object? sender, EventArgs e)
        {
            try
            {
                // Created if no screenshot has been saved yet, so there's always something to open.
                Directory.CreateDirectory(RaidScreenshot.OutputFolder);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(RaidScreenshot.OutputFolder) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log($"Couldn't open the screenshot folder: {ex.Message}");
            }
        }

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
                        var (image, captureError) = RaidScreenshot.CaptureGame();
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

        /// <summary>Waits until the next check; false once the sync has stopped.</summary>
        private static bool WaitForNextCheck(CancellationToken ct)
        {
            try
            {
                return !ct.WaitHandle.WaitOne(ScreenshotCheckInterval);
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
            // Small tolerance so timer jitter doesn't push a retry out by a whole extra tick.
            if (job.Syncing || _cts == null || !job.Enabled || DateTime.Now + TimeSpan.FromSeconds(1) < job.NextAttempt)
            {
                return;
            }

            job.Syncing = true;
            var ct = _cts.Token;
            SetStatus(job, "Checking for changes…");
            try
            {
                var result = await job.Uploader.SyncAsync(job.Folder, job.Url, _userId, _apiKey, job.SyncedFiles, ct);
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
                job.Syncing = false;
            }
        }

        private void OnSyncSucceeded(SyncJob job)
        {
            if (job.FailureCount > 0)
            {
                Log($"{job.Name}: connection restored");
            }
            job.FailureCount = 0;
            job.NextAttempt = DateTime.MinValue;
            job.LastError = null;
            SetStatus(job, $"Last checked {DateTime.Now:T}");
        }

        private void OnSyncFailed(SyncJob job, string error)
        {
            job.FailureCount++;

            // 5m, 10m, 20m, then capped at 30m.
            var delay = TimeSpan.FromMilliseconds(job.Timer.Interval * Math.Pow(2, Math.Min(job.FailureCount - 1, 10)));
            if (delay > MaxBackoff)
            {
                delay = MaxBackoff;
            }
            job.NextAttempt = DateTime.Now + delay;

            // Only log when the error changes; repeats just update the status line.
            if (error != job.LastError)
            {
                Log($"{job.Name}: {error} – retrying with backoff");
                job.LastError = error;
            }
            SetStatus(job, $"Failing ({job.FailureCount}x) – next try {job.NextAttempt:T}");
        }

        private void SetStatus(SyncJob job, string status)
        {
            if (_cts == null)
            {
                return;
            }
            job.Status = status;
            lblStatus.Text = string.Join(Environment.NewLine, Jobs.Where(j => j.Enabled).Select(j => $"{j.Name}: {j.Status}"));
        }

        private void StopSync()
        {
            foreach (var job in Jobs)
            {
                job.Timer.Stop();
            }
            ignoreTimer.Stop();
            imagesTimer.Stop();
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            SetInputsEnabled(true);
            btnStart.Text = "Start";
            lblStatus.Text = "Stopped";
            Log("Stopped");
        }

        private bool ValidateInputs()
        {
            // Either folder may be left empty to skip that sync, but not both.
            var playersFolder = txtFolder.Text.Trim();
            var raidFolder = txtRaidFolder.Text.Trim();
            if (playersFolder.Length == 0 && raidFolder.Length == 0)
            {
                ShowError("Select a PlayerDB or RaidsDB addon folder to sync.", txtFolder);
                return false;
            }
            if (playersFolder.Length > 0 && !IsAddonFolder(playersFolder, PlayerDbDataFolders))
            {
                ShowError(NotAddonMessage(playersFolder, "PlayerDB", PlayerDbDataFolders), txtFolder);
                return false;
            }
            if (raidFolder.Length > 0 && !IsAddonFolder(raidFolder, AppSettings.RaidsFolder))
            {
                ShowError(NotAddonMessage(raidFolder, "RaidsDB", [AppSettings.RaidsFolder]), txtRaidFolder);
                return false;
            }

            var url = AppSettings.NormalizeBaseUrl(txtUrl.Text);
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                ShowError("Enter a valid http(s) server URL.", txtUrl);
                return false;
            }

            if (!long.TryParse(txtUserId.Text.Trim(), out var userId) || userId <= 0)
            {
                ShowError("User ID must be a positive number.", txtUserId);
                return false;
            }

            if (string.IsNullOrEmpty(txtApiKey.Text))
            {
                ShowError("Enter your API key.", txtApiKey);
                return false;
            }

            txtUrl.Text = url;
            _players.Folder = DataFolder(playersFolder, AppSettings.PlayersFolder);
            _homes.Folder = DataFolder(playersFolder, AppSettings.HousesFolder);
            _regrades.Folder = DataFolder(playersFolder, AppSettings.RegradesFolder);
            _inventory.Folder = DataFolder(playersFolder, AppSettings.InventoryFolder);
            _raids.Folder = DataFolder(raidFolder, AppSettings.RaidsFolder);
            if (playersFolder.Length > 0)
            {
                foreach (var (job, dataFolder) in new[] { (_players, AppSettings.PlayersFolder), (_homes, AppSettings.HousesFolder), (_regrades, AppSettings.RegradesFolder), (_inventory, AppSettings.InventoryFolder) })
                {
                    if (!job.Enabled)
                    {
                        Log($"{job.Name}: skipped, the PlayerDB addon has no {dataFolder} folder yet (stop and start again once it has)");
                    }
                }
            }
            foreach (var job in Jobs)
            {
                job.Url = url + job.EndpointPath;
            }
            _raidIgnoreUrl = url + RaidIgnoreList.EndpointPath;
            _raidImagesUrl = url + RaidImageUploader.EndpointPath;
            _userId = userId;
            _apiKey = txtApiKey.Text;
            return true;
        }

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
            MessageBox.Show(this, message, "PlayerDB Sync", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            focus.Focus();
        }

        private void SetInputsEnabled(bool enabled)
        {
            txtFolder.Enabled = enabled;
            btnBrowse.Enabled = enabled;
            txtRaidFolder.Enabled = enabled;
            btnBrowseRaid.Enabled = enabled;
            btnClearData.Enabled = enabled;
            txtUrl.Enabled = enabled;
            txtUserId.Enabled = enabled;
            txtApiKey.Enabled = enabled;
        }

        private void SaveSettings()
        {
            _settings.Folder = txtFolder.Text.Trim();
            _settings.RaidFolder = txtRaidFolder.Text.Trim();
            _settings.Url = txtUrl.Text.Trim();
            _settings.UserId = txtUserId.Text.Trim();
            _settings.SetApiKey(txtApiKey.Text);
            _settings.RaidScreenshots = chkRaidScreenshots.Checked;
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
            foreach (var job in Jobs)
            {
                job.Timer.Stop();
            }
            ignoreTimer.Stop();
            imagesTimer.Stop();
            _cts?.Cancel();
            SaveSettings();
        }
    }
}
