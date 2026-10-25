namespace PlayerDBSync
{
    public partial class Form1 : Form
    {
        private const int MaxLogLines = 500;
        private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan ScreenshotCheckInterval = TimeSpan.FromSeconds(2);

        // The addon gives up on a DemoOCR request after 60 s and rewrites its location every second, so check often.
        private static readonly TimeSpan DemoOcrCheckInterval = TimeSpan.FromSeconds(1);

        private const string DemoOcrNeedsDemosFolder =
            "Demo OCR: needs the PlayerDB addon's Demos folder; update the addon, run the game with it enabled, then stop and start again";

        private readonly AppSettings _settings = AppSettings.Load();
        private readonly SyncJob _players;
        private readonly SyncJob _homes;
        private readonly SyncJob _regrades;
        private readonly SyncJob _inventory;
        private readonly SyncJob _raids;
        private CancellationTokenSource? _cts;

        // While RaidsDB is synced: the raid item ignore list, downloaded on ignoreTimer, and the raid screenshots
        // waiting in RaidScreenshot.OutputFolder, uploaded on imagesTimer.
        private readonly PeriodicTask _ignoreList;
        private readonly PeriodicTask _imageUploads;
        private readonly RaidImageUploader _imageUploader;

        // Raid screenshots are taken on their own thread, started with the sync and ended by its cancellation.
        private Thread? _screenshotThread;
        // Mirrors the checkbox so the screenshot thread can read it.
        private volatile bool _screenshotsOn;

        // Demo OCR screenshots waiting in DemoOcr.OutputFolder, uploaded on demoOcrTimer while the PlayerDB Demos folder is synced.
        private readonly PeriodicTask _demoOcrUploads;
        private readonly DemoOcrUploader _demoOcrUploader;
        private string _demosFolder = "";

        // Demo OCR screenshots are taken on their own thread, started with the sync and ended by its cancellation.
        private Thread? _demoOcrThread;
        // Mirrors the checkbox so the Demo OCR thread can read it.
        private volatile bool _demoOcrOn;

        // Bumped on each version lookup, so a slow earlier one can't overwrite a newer result.
        private int _versionLookup;

        // Snapshot of the server and credentials taken when sync starts, so edits to the boxes don't affect a running sync.
        private string _baseUrl = "";
        private ApiCredentials _credentials;

        /// <summary>
        ///  One addon data folder pushed to one API endpoint on its own timer, with its own backoff,
        ///  so a failing endpoint doesn't hold up the others.
        /// </summary>
        private sealed class SyncJob(string name, bool inRaidsDb, string dataFolderName, string endpointPath,
            System.Windows.Forms.Timer timer, Dictionary<string, SyncRecord> syncedFiles, Action<string> log)
        {
            public string Name { get; } = name;

            /// <summary>True if the data folder is in the RaidsDB addon folder, false if in the PlayerDB one.</summary>
            public bool InRaidsDb { get; } = inRaidsDb;
            public string AddonName => InRaidsDb ? "RaidsDB" : "PlayerDB";
            public string DataFolderName { get; } = dataFolderName;
            public string EndpointPath { get; } = endpointPath;
            public System.Windows.Forms.Timer Timer { get; } = timer;
            public DatUploader Uploader { get; } = new(m => log($"{name}: {m}"));
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

        /// <summary>
        ///  Work repeated on a timer while syncing, unless <paramref name="enabled"/> says it's not needed for this sync.
        ///  <paramref name="run"/> returns why it failed, or null. A failure is logged as "Name: failure: why" when the
        ///  reason changes and the next tick retries; once it works again, "Name: recovered" is logged.
        /// </summary>
        private sealed class PeriodicTask(string name, string failure, string recovered, System.Windows.Forms.Timer timer,
            Func<bool> enabled, Func<CancellationToken, Task<string?>> run)
        {
            public string Name { get; } = name;
            public string Failure { get; } = failure;
            public string Recovered { get; } = recovered;
            public System.Windows.Forms.Timer Timer { get; } = timer;
            public bool Enabled => enabled();
            public Func<CancellationToken, Task<string?>> Run { get; } = run;

            public bool Running { get; set; }
            public string? LastError { get; set; }
        }

        private IEnumerable<SyncJob> Jobs => [_players, _homes, _regrades, _inventory, _raids];

        private IEnumerable<PeriodicTask> Tasks => [_ignoreList, _imageUploads, _demoOcrUploads];

        // The PlayerDB addon keeps players, houses, regrades (once HomeDB and RegradeDB) and inventory side by side;
        // each is synced if its folder is there.
        private static readonly string[] PlayerDbDataFolders =
            [AppSettings.PlayersFolder, AppSettings.HousesFolder, AppSettings.RegradesFolder, AppSettings.InventoryFolder];

        public Form1()
        {
            InitializeComponent();
            _players = new SyncJob("Players", inRaidsDb: false, AppSettings.PlayersFolder, "/api/players", syncTimer, _settings.SyncedFiles, Log);
            _homes = new SyncJob("Homes", inRaidsDb: false, AppSettings.HousesFolder, "/api/homes", homesTimer, _settings.HomeSyncedFiles, Log);
            _regrades = new SyncJob("Regrades", inRaidsDb: false, AppSettings.RegradesFolder, "/api/regrades", regradesTimer, _settings.RegradeSyncedFiles, Log);
            _inventory = new SyncJob("Inventory", inRaidsDb: false, AppSettings.InventoryFolder, "/api/inventory", inventoryTimer, _settings.InventorySyncedFiles, Log);
            _raids = new SyncJob("Raids", inRaidsDb: true, AppSettings.RaidsFolder, "/api/raids", raidsTimer, _settings.RaidSyncedFiles, Log);

            _imageUploader = new RaidImageUploader(m => Log($"Screenshots: {m}"));
            _demoOcrUploader = new DemoOcrUploader(m => Log($"Demo OCR: {m}"));
            _ignoreList = new PeriodicTask("Raids", "couldn't update the item ignore list", "ignore list download working again",
                ignoreTimer, () => _raids.Enabled, RefreshIgnoreListAsync);
            _imageUploads = new PeriodicTask("Screenshots", "couldn't upload", "uploading working again",
                imagesTimer, () => _raids.Enabled,
                async ct => (await _imageUploader.UploadNextAsync(_baseUrl + RaidImageUploader.EndpointPath, _credentials, ct)).Error);
            _demoOcrUploads = new PeriodicTask("Demo OCR", "couldn't upload", "uploading working again",
                demoOcrTimer, () => _demosFolder.Length > 0,
                async ct => (await _demoOcrUploader.UploadNextAsync(_baseUrl + DemoOcrUploader.EndpointPath, _credentials, ct)).Error);

            foreach (var job in Jobs)
            {
                job.Timer.Tick += async (_, _) => await RunSyncAsync(job);
            }
            foreach (var task in Tasks)
            {
                task.Timer.Tick += async (_, _) => await RunAsync(task);
            }
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

        private void Form1_Shown(object? sender, EventArgs e)
        {
            // Resume syncing automatically when a previous run left complete settings;
            // otherwise wait for the user to fill them in and press Start.
            if (InputProblem() == null)
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
            var token = _cts.Token;
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
                    Log($"{job.Name}: watching {job.Folder} (checking every {Every(job.Timer)})");
                }
            }
            foreach (var task in Tasks)
            {
                task.LastError = null;
            }
            _imageUploader.ResetCache();
            _demoOcrUploader.ResetCache();
            if (_demosFolder.Length > 0)
            {
                Log($"Demo OCR: uploading new screenshots from {DemoOcr.OutputFolder} every {Every(demoOcrTimer)}");
                var demosFolder = _demosFolder;
                _demoOcrThread = StartWatcher(_demoOcrThread, "Demo OCR screenshots", () => WatchDemoOcrRequests(demosFolder, token));
            }
            else if (chkDemoOcr.Checked)
            {
                Log(DemoOcrNeedsDemosFolder);
            }
            if (_raids.Enabled)
            {
                Log($"Raids: downloading the item ignore list every {Every(ignoreTimer)}");
                Log($"Screenshots: uploading new ones from {RaidScreenshot.OutputFolder} every {Every(imagesTimer)}, up to {RaidImageUploader.MaxPerUpload} at a time");
                var raidsFolder = _raids.Folder;
                _screenshotThread = StartWatcher(_screenshotThread, "Raid screenshots", () => WatchScreenshotRequests(raidsFolder, token));
            }

            await Task.WhenAll(Jobs.Select(RunSyncAsync)
                .Append(RunAsync(_ignoreList))
                .Append(StartImageUploadsAsync())
                .Append(RunAsync(_demoOcrUploads)));
            if (_cts != null)
            {
                foreach (var job in Jobs.Where(j => j.Enabled))
                {
                    job.Timer.Start();
                }
                foreach (var task in Tasks.Where(t => t.Enabled))
                {
                    task.Timer.Start();
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
            var target = $"{job.Folder}|{job.Url}|{_credentials.UserId}";
            if (!string.Equals(oldTarget, target, StringComparison.OrdinalIgnoreCase))
            {
                job.SyncedFiles.Clear();
            }
            return target;
        }

        /// <summary>"5 min", "30 s": how often <paramref name="timer"/> ticks, for the log.</summary>
        private static string Every(System.Windows.Forms.Timer timer)
        {
            var interval = TimeSpan.FromMilliseconds(timer.Interval);
            return interval.TotalMinutes >= 1 ? $"{interval.TotalMinutes:0.#} min" : $"{interval.TotalSeconds:0} s";
        }

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
                if (error == null)
                {
                    if (task.LastError != null)
                    {
                        Log($"{task.Name}: {task.Recovered}");
                    }
                    task.LastError = null;
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
                task.Running = false;
            }

            // Only log when the error changes; the next tick retries.
            error = $"{task.Name}: {task.Failure}: {error} – retrying in {Every(task.Timer)}";
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

        private void StopTimers()
        {
            foreach (var timer in Jobs.Select(j => j.Timer).Concat(Tasks.Select(t => t.Timer)))
            {
                timer.Stop();
            }
        }

        private void StopSync()
        {
            StopTimers();
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            SetInputsEnabled(true);
            btnStart.Text = "Start";
            lblStatus.Text = "Stopped";
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
            StopTimers();
            _cts?.Cancel();
            SaveSettings();
        }
    }
}
