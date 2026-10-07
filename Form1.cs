namespace PlayerDBSync
{
    public partial class Form1 : Form
    {
        private const int MaxLogLines = 500;
        private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(30);

        private readonly AppSettings _settings = AppSettings.Load();
        private readonly PlayerUploader _uploader;
        private CancellationTokenSource? _cts;
        private bool _syncing;

        // Backoff state while the server (or folder) is failing.
        private int _failureCount;
        private DateTime _nextAttempt = DateTime.MinValue;
        private string? _lastError;

        // Snapshot of the inputs taken when sync starts, so edits to the boxes don't affect a running sync.
        private string _folder = "";
        private string _url = "";
        private long _userId;
        private string _apiKey = "";

        public Form1()
        {
            InitializeComponent();
            _uploader = new PlayerUploader(Log);
        }

        private void Form1_Load(object? sender, EventArgs e)
        {
            txtFolder.Text = _settings.Folder;
            txtUrl.Text = _settings.Url;
            txtUserId.Text = _settings.UserId;
            txtApiKey.Text = _settings.GetApiKey();
        }

        private void Form1_Shown(object? sender, EventArgs e)
        {
            // Resume syncing automatically when a previous run left complete settings;
            // otherwise wait for the user to fill them in and press Start.
            if (Directory.Exists(txtFolder.Text.Trim())
                && txtUrl.Text.Trim().Length > 0
                && long.TryParse(txtUserId.Text.Trim(), out var id) && id > 0
                && txtApiKey.Text.Length > 0)
            {
                btnStart_Click(this, EventArgs.Empty);
            }
        }

        private void btnBrowse_Click(object? sender, EventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select the PlayerDB Players folder",
                UseDescriptionForTitle = true,
                SelectedPath = Directory.Exists(txtFolder.Text) ? txtFolder.Text : AppSettings.DefaultFolder,
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                txtFolder.Text = dialog.SelectedPath;
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

            var target = $"{_folder}|{_url}|{_userId}";
            if (!string.Equals(_settings.SyncTarget, target, StringComparison.OrdinalIgnoreCase))
            {
                // Different folder/server/user: everything needs to be pushed again.
                _settings.SyncedFiles.Clear();
                _settings.SyncTarget = target;
            }
            _uploader.ResetCache();
            _failureCount = 0;
            _nextAttempt = DateTime.MinValue;
            _lastError = null;

            _cts = new CancellationTokenSource();
            SetInputsEnabled(false);
            btnStart.Text = "Stop";
            Log($"Watching {_folder} (checking every {TimeSpan.FromMilliseconds(syncTimer.Interval).TotalMinutes:0.#} min)");

            await RunSyncAsync();
            if (_cts != null)
            {
                syncTimer.Start();
            }
        }

        private async void syncTimer_Tick(object? sender, EventArgs e)
        {
            await RunSyncAsync();
        }

        private async Task RunSyncAsync()
        {
            // Small tolerance so timer jitter doesn't push a retry out by a whole extra tick.
            if (_syncing || _cts == null || DateTime.Now + TimeSpan.FromSeconds(1) < _nextAttempt)
            {
                return;
            }

            _syncing = true;
            var ct = _cts.Token;
            lblStatus.Text = "Checking for changes…";
            try
            {
                var result = await _uploader.SyncAsync(_folder, _url, _userId, _apiKey, _settings.SyncedFiles, ct);
                if (result.StateChanged)
                {
                    SaveSettings();
                }

                if (result.Error == null)
                {
                    OnSyncSucceeded();
                }
                else
                {
                    OnSyncFailed(result.Error);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                OnSyncFailed($"Sync error: {ex.Message}");
            }
            finally
            {
                _syncing = false;
            }
        }

        private void OnSyncSucceeded()
        {
            if (_failureCount > 0)
            {
                Log("Connection restored");
            }
            _failureCount = 0;
            _nextAttempt = DateTime.MinValue;
            _lastError = null;
            lblStatus.Text = $"Last checked {DateTime.Now:T}";
        }

        private void OnSyncFailed(string error)
        {
            _failureCount++;

            // 5m, 10m, 20m, then capped at 30m.
            var delay = TimeSpan.FromMilliseconds(syncTimer.Interval * Math.Pow(2, Math.Min(_failureCount - 1, 10)));
            if (delay > MaxBackoff)
            {
                delay = MaxBackoff;
            }
            _nextAttempt = DateTime.Now + delay;

            // Only log when the error changes; repeats just update the status line.
            if (error != _lastError)
            {
                Log($"{error} – retrying with backoff");
                _lastError = error;
            }
            lblStatus.Text = $"Failing ({_failureCount}x) – next try {_nextAttempt:T}";
        }

        private void StopSync()
        {
            syncTimer.Stop();
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
            var folder = txtFolder.Text.Trim();
            if (!Directory.Exists(folder))
            {
                ShowError($"Folder not found:\n{folder}", txtFolder);
                return false;
            }

            var url = txtUrl.Text.Trim();
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

            _folder = folder;
            _url = url;
            _userId = userId;
            _apiKey = txtApiKey.Text;
            return true;
        }

        private void ShowError(string message, Control focus)
        {
            MessageBox.Show(this, message, "PlayerDB Sync", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            focus.Focus();
        }

        private void SetInputsEnabled(bool enabled)
        {
            txtFolder.Enabled = enabled;
            btnBrowse.Enabled = enabled;
            txtUrl.Enabled = enabled;
            txtUserId.Enabled = enabled;
            txtApiKey.Enabled = enabled;
        }

        private void SaveSettings()
        {
            _settings.Folder = txtFolder.Text.Trim();
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
            syncTimer.Stop();
            _cts?.Cancel();
            SaveSettings();
        }
    }
}
