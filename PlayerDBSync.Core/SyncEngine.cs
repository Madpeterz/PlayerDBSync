namespace PlayerDBSync
{
    /// <summary>What <see cref="SyncEngine.PlanClear"/> found in one data folder.</summary>
    internal sealed record ClearFolder(SyncJob Job, string Path, string[] Files);

    /// <summary>What <see cref="SyncEngine.Clear"/> did.</summary>
    internal sealed record ClearResult(int Cleared, IReadOnlyList<string> Failed, string Summary);

    /// <summary>
    ///  Everything the app does while syncing, with no window of its own: each addon folder pushed to its endpoint on
    ///  its own schedule, the raid item ignore list, the screenshot watchers and their uploads. A window (WinForms or
    ///  Avalonia) fills in the settings, calls <see cref="Start"/> and <see cref="Stop"/>, calls <see cref="Tick"/> a few times a
    ///  second, and shows the <see cref="Scheduled"/> events' progress and what <see cref="Logged"/> says.
    ///  Make it on the window's thread and call it only from there: it continues there after each await, and the
    ///  screenshot threads hand their results back to it through the <see cref="SynchronizationContext"/> it was made in.
    /// </summary>
    internal sealed class SyncEngine : IDisposable
    {
        private const string DemoOcrNeedsDemosFolder =
            "Demo OCR: needs the PlayerDB addon's Demos folder; update the addon, run the game with it enabled, then stop and start again";

        private readonly AppSettings _settings;
        private readonly IGameCapture _capture;
        private readonly SynchronizationContext? _ui;
        private bool _disposed;

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

        // Snapshot of the server and credentials taken when sync starts, so edits to the boxes don't affect a running sync.
        private string _baseUrl = "";
        private ApiCredentials _credentials;

        /// <summary>
        ///  Something to show in the log. Raised on the window's thread. (The window puts its own time on it.)
        /// </summary>
        public event Action<string>? Logged;

        /// <summary>An event's <see cref="TimedEvent.Status"/> changed.</summary>
        public event Action<TimedEvent>? StatusChanged;

        /// <summary>Files were uploaded, so the addon's version shown may have changed.</summary>
        public event Action? AddonDataChanged;

        public SyncEngine(AppSettings settings, IGameCapture capture, SynchronizationContext? ui = null)
        {
            _settings = settings;
            _capture = capture;
            _ui = ui ?? SynchronizationContext.Current;
            _screenshotsOn = settings.RaidScreenshots;
            _demoOcrOn = settings.DemoOcrScreenshots;

            _players = new SyncJob("Players", inRaidsDb: false, AppSettings.PlayersFolder, "/api/players", settings.SyncedFiles, Log);
            _homes = new SyncJob("Homes", inRaidsDb: false, AppSettings.HousesFolder, "/api/homes", settings.HomeSyncedFiles, Log);
            _regrades = new SyncJob("Regrades", inRaidsDb: false, AppSettings.RegradesFolder, "/api/regrades", settings.RegradeSyncedFiles, Log);
            _inventory = new SyncJob("Inventory", inRaidsDb: false, AppSettings.InventoryFolder, "/api/inventory", settings.InventorySyncedFiles, Log);
            _raids = new SyncJob("Raids", inRaidsDb: true, AppSettings.RaidsFolder, "/api/raids", settings.RaidSyncedFiles, Log);

            _imageUploader = new RaidImageUploader(m => Log($"Screenshots: {m}"));
            _demoOcrUploader = new DemoOcrUploader(m => Log($"Demo OCR: {m}"));
            _ignoreList = new PeriodicTask("Raids", "Raid ignore list", "couldn't update the item ignore list", "ignore list download working again",
                Schedule.IgnoreListInterval, () => _raids.Enabled, RefreshIgnoreListAsync);
            _imageUploads = new PeriodicTask("Screenshots", "Screenshots", "couldn't upload", "uploading working again",
                Schedule.UploadInterval, () => _raids.Enabled,
                async ct => (await _imageUploader.UploadNextAsync(_baseUrl + RaidImageUploader.EndpointPath, _credentials, ct)).Error);
            _demoOcrUploads = new PeriodicTask("Demo OCR", "Demo OCR", "couldn't upload", "uploading working again",
                Schedule.UploadInterval, () => _demosFolder.Length > 0,
                async ct => (await _demoOcrUploader.UploadNextAsync(_baseUrl + DemoOcrUploader.EndpointPath, _credentials, ct)).Error);
        }

        public AppSettings Settings => _settings;

        /// <summary>True from <see cref="Start"/> until <see cref="Stop"/>.</summary>
        public bool Syncing => _cts != null;

        public IReadOnlyList<SyncJob> Jobs => [_players, _homes, _regrades, _inventory, _raids];

        public IReadOnlyList<PeriodicTask> Tasks => [_ignoreList, _imageUploads, _demoOcrUploads];

        /// <summary>Everything that is done on a schedule, in the order its rows are shown.</summary>
        public IReadOnlyList<TimedEvent> Scheduled => [.. Jobs, .. Tasks];

        /// <summary>True if the RaidsDB addon's Raids folder is being synced (so raid screenshots and the ignore list can work).</summary>
        public bool RaidsEnabled => _raids.Enabled;

        /// <summary>True if the PlayerDB addon's Demos folder is there, so Demo OCR can work.</summary>
        public bool DemoOcrEnabled => _demosFolder.Length > 0;

        // ---- settings ----

        /// <summary>Whether raid screenshots are taken on request; read by the screenshot thread.</summary>
        public bool RaidScreenshots
        {
            get => _screenshotsOn;
            set => _screenshotsOn = value;
        }

        /// <summary>Whether Demo OCR screenshots are taken on request; read by the Demo OCR thread.</summary>
        public bool DemoOcrScreenshots
        {
            get => _demoOcrOn;
            set => _demoOcrOn = value;
        }

        /// <summary>Puts the settings as typed in the settings, to be saved with <see cref="SaveSettings"/>.</summary>
        public void Apply(SyncInput input, bool raidScreenshots, bool demoOcrScreenshots)
        {
            _settings.Folder = input.AddonFolder.Trim();
            _settings.RaidFolder = input.RaidFolder.Trim();
            _settings.Url = input.Url.Trim();
            _settings.UserId = input.UserId.Trim();
            _settings.SetApiKey(input.ApiKey);
            _settings.RaidScreenshots = raidScreenshots;
            _settings.DemoOcrScreenshots = demoOcrScreenshots;
        }

        public void SaveSettings()
        {
            try
            {
                _settings.Save();
            }
            catch (Exception ex)
            {
                Log($"Could not save settings: {ex.Message}");
            }
        }

        /// <summary>Says so in the log if raid screenshots were just switched on while syncing without the RaidsDB addon folder.</summary>
        public void NoteRaidScreenshotsSwitchedOn()
        {
            if (Syncing && !_raids.Enabled)
            {
                Log("Screenshots: needs the RaidsDB addon folder; stop and set it to use this");
            }
        }

        /// <summary>Says so in the log if Demo OCR was just switched on while syncing without the PlayerDB addon's Demos folder.</summary>
        public void NoteDemoOcrSwitchedOn()
        {
            if (Syncing && _demosFolder.Length == 0)
            {
                Log(DemoOcrNeedsDemosFolder);
            }
        }

        // ---- starting and stopping ----

        /// <summary>
        ///  Checks <paramref name="input"/> and, if it's fine, starts syncing with it: everything is done once now (the
        ///  returned task ends when those first runs have), then <see cref="Tick"/> does each when it's due. If
        ///  something's wrong the problem is returned and nothing starts. The settings are saved.
        /// </summary>
        public (InputProblem? Problem, Task FirstRuns) Start(SyncInput input, bool raidScreenshots, bool demoOcrScreenshots)
        {
            if (Syncing)
            {
                return (null, Task.CompletedTask);
            }
            if (AddonFolders.Check(input) is { } problem)
            {
                return (problem, Task.CompletedTask);
            }

            var url = AppSettings.NormalizeBaseUrl(input.Url);
            input = input with { Url = url };
            foreach (var job in Jobs)
            {
                var addonFolder = (job.InRaidsDb ? input.RaidFolder : input.AddonFolder).Trim();
                job.Folder = AddonFolders.DataFolder(addonFolder, job.DataFolderName);
                job.Url = url + job.EndpointPath;
                if (addonFolder.Length > 0 && !job.Enabled)
                {
                    Log($"{job.Name}: skipped, the {job.AddonName} addon has no {job.DataFolderName} folder yet (stop and start again once it has)");
                }
            }
            _demosFolder = AddonFolders.DataFolder(input.AddonFolder.Trim(), AppSettings.DemosFolder);
            _baseUrl = url;
            _credentials = new ApiCredentials(long.Parse(input.UserId.Trim()), input.ApiKey);
            _screenshotsOn = raidScreenshots;
            _demoOcrOn = demoOcrScreenshots;

            Apply(input, raidScreenshots, demoOcrScreenshots);
            SaveSettings();

            _settings.SyncTarget = Retarget(_players, _settings.SyncTarget);
            _settings.HomeSyncTarget = Retarget(_homes, _settings.HomeSyncTarget);
            _settings.RegradeSyncTarget = Retarget(_regrades, _settings.RegradeSyncTarget);
            _settings.InventorySyncTarget = Retarget(_inventory, _settings.InventorySyncTarget);
            _settings.RaidSyncTarget = Retarget(_raids, _settings.RaidSyncTarget);

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
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
            // Each is done once now (below); this only holds off Tick until that has started.
            foreach (var ev in Scheduled)
            {
                ev.Failing = false;
                ev.Status = "";
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
            else if (_demoOcrOn)
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

            var firstRuns = Task.WhenAll(Jobs.Select(RunSyncAsync)
                .Append(RunAsync(_ignoreList))
                .Append(StartImageUploadsAsync())
                .Append(RunAsync(_demoOcrUploads)));
            return (null, firstRuns);
        }

        public void Stop()
        {
            if (_cts == null)
            {
                return;
            }
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
            Log("Stopped");
        }

        /// <summary>The window is closing: whatever is running is told to end. (The settings are the window's to save.)</summary>
        public void Dispose()
        {
            _disposed = true;
            _cts?.Cancel();
        }

        /// <summary>Does whatever is due. Call it every few hundred milliseconds while syncing.</summary>
        public void Tick()
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

        // ---- the scheduled work ----

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
                    AddonDataChanged?.Invoke();
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
            if (delay > Schedule.MaxBackoff)
            {
                delay = Schedule.MaxBackoff;
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

        private void SetStatus(TimedEvent ev, string status)
        {
            ev.Status = status;
            StatusChanged?.Invoke(ev);
        }

        // ---- screenshots ----

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

        /// <summary>
        ///  The screenshot thread: checks for request.sht every <see cref="Schedule.ScreenshotCheckInterval"/> and answers it.
        ///  Capturing, encoding and file work all happen here, never on the window's thread.
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
                        var (image, captureError) = _capture.Capture();
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
        ///  The Demo OCR thread: checks the PlayerDB addon's Demos\request.dat every <see cref="Schedule.DemoOcrCheckInterval"/>.
        ///  While it says <c>status = true</c> and location.dat is up to date, it captures the game, saves the picture
        ///  named after that location and answers by setting the request to <c>status = false</c>; the upload follows.
        /// </summary>
        private void WatchDemoOcrRequests(string demosFolder, CancellationToken ct)
        {
            // The current request already has its screenshot saved but couldn't be answered yet.
            bool requestAnswered = false;
            string? lastError = null;

            while (WaitForNextCheck(ct, Schedule.DemoOcrCheckInterval))
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
                        var (image, captureError) = _capture.Capture();
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
        private void BeginUploadDemoOcr() => PostToUi(async () =>
        {
            await Task.Delay(DemoOcrUploader.SettleTime + TimeSpan.FromMilliseconds(250));
            await RunAsync(_demoOcrUploads);
        });

        /// <summary>Waits until the next check; false once the sync has stopped.</summary>
        private static bool WaitForNextCheck(CancellationToken ct, TimeSpan? interval = null)
        {
            try
            {
                return !ct.WaitHandle.WaitOne(interval ?? Schedule.ScreenshotCheckInterval);
            }
            catch (ObjectDisposedException)
            {
                // Stopping cancels and disposes the token source, possibly while a screenshot was being taken.
                return false;
            }
        }

        // ---- clearing the addons' data ----

        /// <summary>
        ///  The *.dat files in each addon data folder that exists (Players, Houses, Regrades, Inventory, Raids), for
        ///  <see cref="Clear"/> to move to the Recycle Bin or Trash.
        /// </summary>
        public List<ClearFolder> PlanClear(SyncInput input) => Jobs
            .Select(job => (Job: job, Path: AddonFolders.DataFolder((job.InRaidsDb ? input.RaidFolder : input.AddonFolder).Trim(), job.DataFolderName)))
            .Where(f => f.Path.Length > 0)
            .Select(f => new ClearFolder(f.Job, f.Path, Directory.GetFiles(f.Path, "*.dat", SearchOption.TopDirectoryOnly)))
            .ToList();

        /// <summary>
        ///  Moves the planned files to the Recycle Bin (or the Trash), so files saved by an addon version the server
        ///  doesn't take stop failing every sync. Their sync records go too: anything collected from now on is new,
        ///  whatever its file is called.
        /// </summary>
        public ClearResult Clear(IEnumerable<ClearFolder> folders)
        {
            int cleared = 0;
            var failed = new List<string>();
            foreach (var (job, _, files) in folders)
            {
                foreach (var file in files)
                {
                    try
                    {
                        Trash.Move(file);
                        cleared++;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
                    {
                        failed.Add($"{Path.GetFileName(file)}: {ex.Message}");
                    }
                }
                job.SyncedFiles.Clear();
                job.Uploader.ResetCache();
            }
            SaveSettings();

            var summary = $"Cleared {Words.Count(cleared, "addon data file")} (moved to the {(OperatingSystem.IsWindows() ? "Recycle Bin" : "Trash")})";
            Log(summary);
            foreach (var failure in failed)
            {
                Log($"Couldn't clear {failure}");
            }
            AddonDataChanged?.Invoke();
            return new ClearResult(cleared, failed, summary);
        }

        // ---- logging and the window's thread ----

        private void Log(string message) => Logged?.Invoke(message);

        /// <summary>Logs from a background thread; dropped if the window has closed meanwhile.</summary>
        private void LogFromThread(string message) => PostToUi(() => Log(message));

        private void PostToUi(Action action) => PostToUi(() =>
        {
            action();
            return Task.CompletedTask;
        });

        private void PostToUi(Func<Task> action)
        {
            if (_disposed)
            {
                return;
            }
            if (_ui == null)
            {
                _ = Task.Run(action);
                return;
            }
            _ui.Post(async _ =>
            {
                if (!_disposed)
                {
                    await action();
                }
            }, null);
        }
    }
}
