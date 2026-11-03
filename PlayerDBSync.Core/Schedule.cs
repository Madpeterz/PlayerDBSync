namespace PlayerDBSync
{
    /// <summary>How often the app does each thing while syncing.</summary>
    internal static class Schedule
    {
        public static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(30);
        public static readonly TimeSpan ScreenshotCheckInterval = TimeSpan.FromSeconds(2);

        // The addon gives up on a DemoOCR request after 60 s and rewrites its location every second, so check often.
        public static readonly TimeSpan DemoOcrCheckInterval = TimeSpan.FromSeconds(1);

        // How often each addon folder is synced (each wait a random pick in this range, so the folders don't all check
        // at once), the raid item ignore list downloaded and waiting screenshots uploaded. A sync with nothing changed
        // only reads the files' sizes and dates; it sends nothing.
        public static readonly TimeSpan SyncIntervalMin = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan SyncIntervalMax = TimeSpan.FromSeconds(45);
        public static readonly TimeSpan IgnoreListInterval = TimeSpan.FromMinutes(15);
        public static readonly TimeSpan UploadInterval = TimeSpan.FromSeconds(30);

        // How often GitHub is asked for a newer release, while the app runs.
        public static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(6);

        /// <summary>"5 min", "30 s", for the log.</summary>
        public static string Every(TimeSpan interval) =>
            interval.TotalMinutes >= 1 ? $"{interval.TotalMinutes:0.#} min" : $"{interval.TotalSeconds:0} s";

        /// <summary>"30–45 s" for a range (one unit, when both are under a minute), or as <see cref="Every(TimeSpan)"/> for a single interval.</summary>
        public static string Every(TimeSpan min, TimeSpan max) =>
            min == max ? Every(min)
            : max.TotalMinutes < 1 ? $"{min.TotalSeconds:0}–{max.TotalSeconds:0} s"
            : $"{Every(min)} – {Every(max)}";
    }

    /// <summary>
    ///  Something done every <see cref="Interval"/> (or, given a <see cref="MaxInterval"/>, a random wait between the
    ///  two each time) while syncing, shown as a row (<see cref="Label"/>, a bar and a status) whose bar empties until
    ///  it is done again; <see cref="SyncEngine.Tick"/> does it once the bar is empty. The wait can be longer still
    ///  (a backoff); the bar then covers the longer wait.
    /// </summary>
    internal abstract class TimedEvent(string label, TimeSpan interval, TimeSpan? maxInterval = null)
    {
        public string Label { get; } = label;
        public TimeSpan Interval { get; } = interval;
        public TimeSpan MaxInterval { get; } = maxInterval ?? interval;

        /// <summary>The wait before it's next done.</summary>
        public TimeSpan NextWait() => Interval + (MaxInterval - Interval) * Random.Shared.NextDouble();

        /// <summary>"30–45 s", or "15 min" for a fixed interval, for the log and the Info page.</summary>
        public string Every => Schedule.Every(Interval, MaxInterval);

        public abstract bool Enabled { get; }
        public bool Running { get; set; }

        // Its last run failed; its bar and status turn red.
        public bool Failing { get; set; }

        /// <summary>Shown beside its bar: when it was last checked, or that it's failing and when it next tries.</summary>
        public string Status { get; set; } = "";

        // The wait before it is next done: from when it was last done (or sync started) until it's due.
        public DateTime WaitStart { get; private set; }
        public DateTime DueAt { get; private set; }

        public void WaitUntil(DateTime dueAt)
        {
            WaitStart = DateTime.Now;
            DueAt = dueAt;
        }

        /// <summary>
        ///  The bar's fill: 1 when the event was just done, emptying to 0 as it falls due. A bar steps through this at
        ///  whatever resolution it has.
        /// </summary>
        public double Fraction(DateTime now)
        {
            var wait = (DueAt - WaitStart).TotalMilliseconds;
            var left = (DueAt - now).TotalMilliseconds;
            return wait <= 0 ? 0 : Math.Clamp(left / wait, 0, 1);
        }
    }

    /// <summary>
    ///  One addon data folder pushed to one API endpoint on its own schedule, with its own backoff,
    ///  so a failing endpoint doesn't hold up the others.
    /// </summary>
    internal sealed class SyncJob(string name, bool inRaidsDb, string dataFolderName, string endpointPath,
        Dictionary<string, SyncRecord> syncedFiles, Action<string> log) : TimedEvent(name, Schedule.SyncIntervalMin, Schedule.SyncIntervalMax)
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
    internal sealed class PeriodicTask(string name, string label, string failure, string recovered, TimeSpan interval,
        Func<bool> enabled, Func<CancellationToken, Task<string?>> run) : TimedEvent(label, interval)
    {
        public string Name { get; } = name;
        public string Failure { get; } = failure;
        public string Recovered { get; } = recovered;
        public override bool Enabled => enabled();
        public Func<CancellationToken, Task<string?>> Run { get; } = run;

        public string? LastError { get; set; }
    }
}
