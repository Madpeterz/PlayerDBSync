using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
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

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(100) };

        private readonly Action<string> _log;

        // Pictures the server found wrong (422), with the file stamp they were read at; not retried until the file changes.
        // Deliberately not persisted, so a restart retries them.
        private readonly Dictionary<string, (long Size, DateTime LastWriteUtc)> _rejected = new(StringComparer.OrdinalIgnoreCase);

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
        public async Task<(int Uploaded, string? Error)> UploadNextAsync(string url, long userId, string apiKey, CancellationToken ct)
        {
            int uploaded = 0;
            foreach (var file in await Task.Run(NextFiles, ct))
            {
                var error = await UploadAsync(file, url, userId, apiKey, ct);
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
                .Where(f => !(_rejected.TryGetValue(f.Name, out var rej) && rej.Size == f.Length && rej.LastWriteUtc == f.LastWriteTimeUtc))
                .OrderBy(f => f.LastWriteTimeUtc)
                .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .Take(MaxPerUpload)
                .ToList();
        }

        /// <returns>Null if the server dealt with the picture one way or another; otherwise why it didn't.</returns>
        private async Task<string?> UploadAsync(FileInfo file, string url, long userId, string apiKey, CancellationToken ct)
        {
            var location = DemoOcr.LocationOf(file.Name);
            if (location == null)
            {
                _rejected[file.Name] = (file.Length, file.LastWriteTimeUtc);
                _log($"Skipped {file.Name}: not named <time>_<location>.webp as the app saves them");
                return null;
            }

            string blob;
            try
            {
                blob = Convert.ToBase64String(await File.ReadAllBytesAsync(file.FullName, ct));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Being moved or deleted meanwhile; picked up next time if it's still there.
                return null;
            }

            var sextant = location.Value.Sextant;
            var unixtime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var secret = Convert.ToHexStringLower(SHA256.HashData(
                Encoding.UTF8.GetBytes($"{userId}{sextant}{blob}{apiKey}{unixtime}")));
            var payload = new { sextant, blobdat = blob, userid = userId, unixtime, secret };

            try
            {
                using var response = await Http.PostAsJsonAsync(url, payload, ct);
                var status = (int)response.StatusCode;
                if (response.IsSuccessStatusCode)
                {
                    var queued = await response.Content.ReadFromJsonAsync<Response>(ct);
                    Delete(file);
                    _rejected.Remove(file.Name);
                    _log(queued == null
                        ? $"Uploaded {sextant} ({file.Length:N0} bytes)"
                        : $"Uploaded {sextant} ({file.Length:N0} bytes), request {queued.Id}, place {queued.Position} in the queue");
                    return null;
                }

                // Collapse whitespace so multi-line errors fit on one log line.
                var body = string.Join(' ', (await response.Content.ReadAsStringAsync(ct))
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                if (body.Length > 300)
                {
                    body = body[..300] + "…";
                }
                switch (status)
                {
                    case 409:
                        // Already queued, or a demo or another request is within a few metres: sending it again changes nothing.
                        Delete(file);
                        _log($"Server already has {sextant}: {body}");
                        return null;
                    case 422:
                        _rejected[file.Name] = (file.Length, file.LastWriteTimeUtc);
                        _log($"Server rejected {file.Name}: {body} (will retry when the file changes)");
                        return null;
                    case 418:
                        return $"Server refused the upload ({status} {response.ReasonPhrase} {body}); check the user ID, API key, PC clock and that Demo OCR is enabled for your account";
                    default:
                        // 429 (ten waiting already) clears as the server reads them; the rest are the server being unwell.
                        return $"{status} {response.ReasonPhrase} {body}".TrimEnd();
                }
            }
            catch (HttpRequestException ex)
            {
                return ex.Message;
            }
            catch (System.Text.Json.JsonException)
            {
                return "the server's reply wasn't valid JSON";
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                return "Request timed out";
            }
        }

        private static void Delete(FileInfo file)
        {
            try
            {
                file.Delete();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Left where it is, it's sent again next time and the server turns it away as known.
            }
        }
    }
}
