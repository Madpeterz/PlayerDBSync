using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace PlayerDBSync
{
    /// <summary>
    ///  Uploads the demolition notice screenshots waiting in <see cref="DemoOcr.OutputFolder"/> to /api/demoocr, oldest
    ///  first, one request each. Once the server has a picture (queued, or already in its queue or near a demo) it is
    ///  deleted, so the folder only holds what is still to be sent.
    /// </summary>
    internal sealed class DemoOcrUploader
    {
        public const string EndpointPath = "/api/demoocr";
        public const int MaxPerUpload = 3;

        /// <summary>A screenshot is left alone this long after it is saved, as it isn't always readable straight away.</summary>
        public static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(2);

        private readonly Action<string> _log;

        // Pictures the server found wrong (422), or not named as we save them.
        private readonly RejectedFiles _rejected = new();

        private sealed class Response
        {
            [JsonPropertyName("id")]
            public long Id { get; set; }

            [JsonPropertyName("position")]
            public int Position { get; set; }
        }

        public DemoOcrUploader(Action<string> log)
        {
            _log = log;
        }

        public void ResetCache() => _rejected.Clear();

        /// <summary>
        ///  Uploads the oldest (up to <see cref="MaxPerUpload"/>) pictures waiting, stopping at the first failure.
        /// </summary>
        /// <returns>Pictures the server now has, and why the upload stopped, if it did.</returns>
        public async Task<(int Uploaded, string? Error)> UploadNextAsync(string url, ApiCredentials credentials, CancellationToken ct)
        {
            int uploaded = 0;
            foreach (var file in await Task.Run(NextFiles, ct))
            {
                var error = await UploadAsync(file, url, credentials, ct);
                if (error != null)
                {
                    return (uploaded, error);
                }
                uploaded++;
            }
            return (uploaded, null);
        }

        /// <summary>The oldest pictures waiting that weren't turned away as they are now.</summary>
        private List<FileInfo> NextFiles()
        {
            if (!Directory.Exists(DemoOcr.OutputFolder))
            {
                return [];
            }
            var settled = DateTime.UtcNow - SettleTime;
            return new DirectoryInfo(DemoOcr.OutputFolder)
                .EnumerateFiles("*.webp", SearchOption.TopDirectoryOnly)
                .Where(f => f.LastWriteTimeUtc <= settled)
                .Where(f => !_rejected.Contains(f))
                .OrderBy(f => f.LastWriteTimeUtc)
                .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .Take(MaxPerUpload)
                .ToList();
        }

        /// <returns>Null if the server dealt with the picture one way or another; otherwise why it didn't.</returns>
        private async Task<string?> UploadAsync(FileInfo file, string url, ApiCredentials credentials, CancellationToken ct)
        {
            var location = DemoOcr.LocationOf(file.Name);
            if (location == null)
            {
                _rejected.Add(file);
                _log($"Skipped {file.Name}: not named <time>_<location>.webp as the app saves them");
                return null;
            }

            byte[] bytes;
            try
            {
                bytes = await File.ReadAllBytesAsync(file.FullName, ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Being moved or deleted meanwhile; picked up next time if it's still there.
                return null;
            }

            var sextant = location.Value.Sextant;
            try
            {
                using var response = await ApiClient.PostAsync(url, credentials,
                    [("sextant", sextant), ("blobdat", Convert.ToBase64String(bytes))], ct);
                if (response.IsSuccessStatusCode)
                {
                    var queued = await response.Content.ReadFromJsonAsync<Response>(ct);
                    // If it can't be deleted, it's sent again next time and the server turns it away as known.
                    FileHelper.TryDelete(file.FullName);
                    _rejected.Remove(file.Name);
                    _log(queued == null
                        ? $"Uploaded {sextant} ({bytes.Length:N0} bytes)"
                        : $"Uploaded {sextant} ({bytes.Length:N0} bytes), request {queued.Id}, place {queued.Position} in the queue");
                    return null;
                }

                var body = await ApiClient.ReadBodyAsync(response, ct);
                switch ((int)response.StatusCode)
                {
                    case 409:
                        // Already queued, or a demo or another request is within a few metres: sending it again changes nothing.
                        FileHelper.TryDelete(file.FullName);
                        _log($"Server already has {sextant}: {body}");
                        return null;
                    case 422:
                        _rejected.Add(file);
                        _log($"Server rejected {file.Name}: {body} (will retry when the file changes)");
                        return null;
                    case 418:
                        return $"Server refused the upload ({ApiClient.Describe(response, body)}); check the user ID, API key, PC clock and that Demo OCR is enabled for your account";
                    default:
                        // 429 (ten waiting already) clears as the server reads them; the rest are the server being unwell.
                        return ApiClient.Describe(response, body);
                }
            }
            catch (Exception ex) when (ApiClient.FailureOf(ex, ct) is string failure)
            {
                return failure;
            }
        }
    }
}
