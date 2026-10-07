using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

namespace PlayerDBSync
{
    /// <summary>
    ///  Result of one scan. <see cref="StateChanged"/> means sync records were added or updated and should be saved.
    ///  <see cref="Error"/> is set when the scan was cut short by a problem
    ///  that affects every file (server down, auth failure, missing folder), so the caller should back off.
    /// </summary>
    internal readonly record struct SyncResult(int Uploaded, bool StateChanged, string? Error);

    /// <summary>
    ///  Scans a Players folder for changed *.dat files and pushes them to the PlayerDB API in batches.
    /// </summary>
    internal sealed class PlayerUploader
    {
        private const int MaxBatchSize = 500;

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(100) };

        // Files in the Players folder that aren't player records.
        private static readonly HashSet<string> IgnoredFiles = new(StringComparer.OrdinalIgnoreCase) { "_count.dat" };

        private readonly Action<string> _log;

        // Contents the server rejected as invalid, with the file stamp they were read at;
        // not retried until the file changes. Deliberately not persisted, so a restart retries them.
        private readonly Dictionary<string, (string Hash, long Size, DateTime LastWriteUtc)> _rejected = new(StringComparer.OrdinalIgnoreCase);

        // The server answers 418 both for a bad file and for bad credentials/clock. If two files are refused
        // on their own before anything this scan was accepted, it's almost certainly the credentials.
        private const int SingleRejectsMeaningAuthFailure = 2;
        private bool _anyAcceptedThisScan;
        private readonly List<string> _rejectedThisScan = new();

        private sealed record PendingFile(string Name, byte[] Bytes, string Hash, long Size, DateTime LastWriteUtc);

        private enum PushOutcome { Success, Rejected, ServerError }

        public PlayerUploader(Action<string> log)
        {
            _log = log;
        }

        public void ResetCache() => _rejected.Clear();

        /// <summary>
        ///  Uploads every *.dat file whose contents differ from <paramref name="syncedFiles"/>, recording
        ///  successful uploads there. Files whose size and modified time match their record are skipped
        ///  without being read. Changed files are sent in batches of up to <see cref="MaxBatchSize"/>.
        ///  Stops at the first server-level failure so a dead endpoint costs one request per scan.
        /// </summary>
        public async Task<SyncResult> SyncAsync(string folder, string url, long userId, string apiKey,
            Dictionary<string, SyncRecord> syncedFiles, CancellationToken ct)
        {
            if (!Directory.Exists(folder))
            {
                return new SyncResult(0, false, $"Folder not found: {folder}");
            }

            int uploaded = 0;
            bool changed = false;
            var pending = new List<PendingFile>();
            _anyAcceptedThisScan = false;
            _rejectedThisScan.Clear();

            foreach (var path in Directory.EnumerateFiles(folder, "*.dat", SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();
                var name = Path.GetFileName(path);
                if (IgnoredFiles.Contains(name))
                {
                    continue;
                }

                FileInfo info;
                byte[] bytes;
                try
                {
                    info = new FileInfo(path);
                    if (syncedFiles.TryGetValue(name, out var rec) && rec.Size == info.Length && rec.LastWriteUtc == info.LastWriteTimeUtc)
                    {
                        continue;
                    }
                    if (_rejected.TryGetValue(name, out var rej) && rej.Size == info.Length && rej.LastWriteUtc == info.LastWriteTimeUtc)
                    {
                        continue;
                    }

                    // The game may still have the file open; share access so we don't block it.
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        bytes = new byte[fs.Length];
                        await fs.ReadExactlyAsync(bytes, ct);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Usually the game mid-write; silently retry next scan.
                    continue;
                }

                var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));

                // Touched but not actually changed: remember the new stamp so it's skipped cheaply next time.
                if (syncedFiles.TryGetValue(name, out var existing) && existing.Hash == hash)
                {
                    existing.Size = info.Length;
                    existing.LastWriteUtc = info.LastWriteTimeUtc;
                    changed = true;
                    continue;
                }
                if (_rejected.TryGetValue(name, out var rejected) && rejected.Hash == hash)
                {
                    _rejected[name] = (hash, info.Length, info.LastWriteTimeUtc);
                    continue;
                }

                pending.Add(new PendingFile(name, bytes, hash, info.Length, info.LastWriteTimeUtc));
                if (pending.Count == MaxBatchSize)
                {
                    var (count, error) = await UploadAsync(pending, url, userId, apiKey, syncedFiles, ct);
                    uploaded += count;
                    changed |= count > 0;
                    if (error != null)
                    {
                        return new SyncResult(uploaded, changed, error);
                    }
                    pending.Clear();
                }
            }

            if (pending.Count > 0)
            {
                var (count, error) = await UploadAsync(pending, url, userId, apiKey, syncedFiles, ct);
                uploaded += count;
                changed |= count > 0;
                if (error != null)
                {
                    return new SyncResult(uploaded, changed, error);
                }
            }
            return new SyncResult(uploaded, changed, null);
        }

        /// <summary>
        ///  Uploads <paramref name="files"/> as one request. If the server rejects the batch's data, it is split
        ///  in half and retried, which isolates a bad file (and copes with a too-large request) in a few requests.
        /// </summary>
        /// <returns>Files uploaded, and the server-level error that stopped the upload, if any.</returns>
        private async Task<(int Uploaded, string? Error)> UploadAsync(IReadOnlyList<PendingFile> files, string url, long userId,
            string apiKey, Dictionary<string, SyncRecord> syncedFiles, CancellationToken ct)
        {
            var (outcome, detail) = await PushAsync(url, userId, apiKey, files, ct);
            switch (outcome)
            {
                case PushOutcome.Success:
                    _anyAcceptedThisScan = true;
                    var now = DateTime.UtcNow;
                    foreach (var f in files)
                    {
                        syncedFiles[f.Name] = new SyncRecord { Hash = f.Hash, Size = f.Size, LastWriteUtc = f.LastWriteUtc, SyncedAtUtc = now };
                        _rejected.Remove(f.Name);
                    }
                    _log(files.Count == 1
                        ? $"Uploaded {files[0].Name} ({files[0].Bytes.Length:N0} bytes)"
                        : $"Uploaded {files.Count} files ({files.Sum(f => (long)f.Bytes.Length):N0} bytes)");
                    return (files.Count, null);

                case PushOutcome.Rejected when files.Count == 1:
                    var bad = files[0];
                    _rejected[bad.Name] = (bad.Hash, bad.Size, bad.LastWriteUtc);
                    _rejectedThisScan.Add(bad.Name);

                    if (!_anyAcceptedThisScan && _rejectedThisScan.Count >= SingleRejectsMeaningAuthFailure)
                    {
                        // Not the files' fault after all: un-mark them so they're retried after backoff.
                        foreach (var name in _rejectedThisScan)
                        {
                            _rejected.Remove(name);
                        }
                        return (0, $"Server refused every upload ({detail}); check the user ID, API key and PC clock");
                    }

                    _log($"Server rejected {bad.Name}: {detail} (will retry when the file changes)");
                    return (0, null);

                case PushOutcome.Rejected:
                    int half = files.Count / 2;
                    var first = await UploadAsync(files.Take(half).ToList(), url, userId, apiKey, syncedFiles, ct);
                    if (first.Error != null)
                    {
                        return first;
                    }
                    var second = await UploadAsync(files.Skip(half).ToList(), url, userId, apiKey, syncedFiles, ct);
                    return (first.Uploaded + second.Uploaded, second.Error);

                default:
                    return (0, detail);
            }
        }

        private static async Task<(PushOutcome, string)> PushAsync(string url, long userId, string apiKey,
            IReadOnlyList<PendingFile> files, CancellationToken ct)
        {
            var blobs = files.Select(f => Convert.ToBase64String(f.Bytes)).ToArray();
            var unixtime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // The secret covers blobdat exactly as sent; for an array, the strings joined with ",".
            var secret = Convert.ToHexStringLower(
                SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}{string.Join(',', blobs)}{apiKey}{unixtime}")));

            // A single file keeps the original string form; batches send an array.
            object blobdat = blobs.Length == 1 ? blobs[0] : blobs;
            var payload = new { blobdat, userid = userId, unixtime, secret };

            try
            {
                using var response = await Http.PostAsJsonAsync(url, payload, ct);
                if (response.IsSuccessStatusCode)
                {
                    return (PushOutcome.Success, "");
                }

                // Collapse whitespace so pretty-printed JSON errors fit on one log line.
                var body = string.Join(' ', (await response.Content.ReadAsStringAsync(ct))
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                if (body.Length > 300)
                {
                    body = body[..300] + "…";
                }
                var detail = $"{(int)response.StatusCode} {response.ReasonPhrase} {body}".TrimEnd();

                // Possibly a problem with the submitted data (the server answers 418 for any refusal, bad
                // credentials included; UploadAsync tells those apart). Anything else (404, 429, 5xx) affects every file.
                var dataProblem = (int)response.StatusCode == 418
                    || response.StatusCode is HttpStatusCode.BadRequest
                    or HttpStatusCode.RequestEntityTooLarge
                    or HttpStatusCode.UnprocessableEntity;
                return (dataProblem ? PushOutcome.Rejected : PushOutcome.ServerError, detail);
            }
            catch (HttpRequestException ex)
            {
                return (PushOutcome.ServerError, ex.Message);
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                return (PushOutcome.ServerError, "Request timed out");
            }
        }
    }
}
