namespace PlayerDBSync
{
    public partial class Form1 : Form
    {
        private const int MaxLogLines = 500;
        private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(30);

        private readonly AppSettings _settings = AppSettings.Load();
        private readonly SyncJob _players;
        private readonly SyncJob _homes;
        private CancellationTokenSource? _cts;

        // Snapshot of the credentials taken when sync starts, so edits to the boxes don't affect a running sync.
        private long _userId;
        private string _apiKey = "";

        /// <summary>
        ///  One folder pushed to one API endpoint on its own timer, with its own backoff,
        ///  so a failing endpoint doesn't hold up the other.
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

        private IEnumerable<SyncJob> Jobs => [_players, _homes];

        public Form1()
        {
            InitializeComponent();
            _players = new SyncJob("Players", "/api/players", syncTimer, new DatUploader(m => Log($"Players: {m}")), _settings.SyncedFiles);
            _homes = new SyncJob("Homes", "/api/homes", homesTimer, new DatUploader(m => Log($"Homes: {m}")), _settings.HomeSyncedFiles);
        }

        private void Form1_Load(object? sender, EventArgs e)
        {
            txtFolder.Text = _settings.Folder;
            txtHomeFolder.Text = _settings.HomeFolder;
            txtUrl.Text = _settings.Url;
            txtUserId.Text = _settings.UserId;
            txtApiKey.Text = _settings.GetApiKey();
        }

        private void Form1_Shown(object? sender, EventArgs e)
        {
            // Resume syncing automatically when a previous run left complete settings;
            // otherwise wait for the user to fill them in and press Start.
            var players = txtFolder.Text.Trim();
            var home = txtHomeFolder.Text.Trim();
            if ((players.Length > 0 || home.Length > 0)
                && (players.Length == 0 || IsAddonFolder(players, AppSettings.PlayersFolder))
                && (home.Length == 0 || IsAddonFolder(home, AppSettings.HousesFolder))
                && txtUrl.Text.Trim().Length > 0
                && long.TryParse(txtUserId.Text.Trim(), out var id) && id > 0
                && txtApiKey.Text.Length > 0)
            {
                btnStart_Click(this, EventArgs.Empty);
            }
        }

        private void btnBrowse_Click(object? sender, EventArgs e)
        {
            BrowseAddonFolder(txtFolder, "PlayerDB", AppSettings.PlayersFolder, AppSettings.DefaultFolder);
        }

        private void btnBrowseHome_Click(object? sender, EventArgs e)
        {
            BrowseAddonFolder(txtHomeFolder, "HomeDB", AppSettings.HousesFolder, "");
        }

        private void BrowseAddonFolder(TextBox box, string addonName, string dataFolder, string fallback)
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
            if (!IsAddonFolder(path, dataFolder) && string.Equals(Path.GetFileName(path), dataFolder, StringComparison.OrdinalIgnoreCase)
                && parent != null && IsAddonFolder(parent, dataFolder))
            {
                path = parent;
            }
            box.Text = path;
            if (!IsAddonFolder(path, dataFolder))
            {
                ShowError(NotAddonMessage(path, addonName, dataFolder), box);
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

            await Task.WhenAll(RunSyncAsync(_players), RunSyncAsync(_homes));
            if (_cts != null)
            {
                foreach (var job in Jobs.Where(j => j.Enabled))
                {
                    job.Timer.Start();
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
            var homeFolder = txtHomeFolder.Text.Trim();
            if (playersFolder.Length == 0 && homeFolder.Length == 0)
            {
                ShowError("Select a PlayerDB or HomeDB addon folder to sync.", txtFolder);
                return false;
            }
            if (playersFolder.Length > 0 && !IsAddonFolder(playersFolder, AppSettings.PlayersFolder))
            {
                ShowError(NotAddonMessage(playersFolder, "PlayerDB", AppSettings.PlayersFolder), txtFolder);
                return false;
            }
            if (homeFolder.Length > 0 && !IsAddonFolder(homeFolder, AppSettings.HousesFolder))
            {
                ShowError(NotAddonMessage(homeFolder, "HomeDB", AppSettings.HousesFolder), txtHomeFolder);
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
            _players.Folder = playersFolder.Length > 0 ? Path.Combine(playersFolder, AppSettings.PlayersFolder) : "";
            _homes.Folder = homeFolder.Length > 0 ? Path.Combine(homeFolder, AppSettings.HousesFolder) : "";
            foreach (var job in Jobs)
            {
                job.Url = url + job.EndpointPath;
            }
            _userId = userId;
            _apiKey = txtApiKey.Text;
            return true;
        }

        private static bool IsAddonFolder(string path, string dataFolder) =>
            File.Exists(Path.Combine(path, AppSettings.AddonMarkerFile)) && Directory.Exists(Path.Combine(path, dataFolder));

        private static string NotAddonMessage(string path, string addonName, string dataFolder) =>
            $"This doesn't look like the {addonName} addon folder:\n{path}\n\nIt should contain {AppSettings.AddonMarkerFile} and a {dataFolder} folder.";

        private void ShowError(string message, Control focus)
        {
            MessageBox.Show(this, message, "PlayerDB Sync", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            focus.Focus();
        }

        private void SetInputsEnabled(bool enabled)
        {
            txtFolder.Enabled = enabled;
            btnBrowse.Enabled = enabled;
            txtHomeFolder.Enabled = enabled;
            btnBrowseHome.Enabled = enabled;
            txtUrl.Enabled = enabled;
            txtUserId.Enabled = enabled;
            txtApiKey.Enabled = enabled;
        }

        private void SaveSettings()
        {
            _settings.Folder = txtFolder.Text.Trim();
            _settings.HomeFolder = txtHomeFolder.Text.Trim();
            _settings.Url = txtUrl.Text.Trim();
            _settings.UserId = txtUserId.Text.Trim();
            _settings.SetApiKey(txtApiKey.Text);
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
            _cts?.Cancel();
            SaveSettings();
        }
    }
}
