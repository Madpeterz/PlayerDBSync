using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace PlayerDBSync
{
    /// <summary>
    ///  Uploads the raid screenshots in <see cref="RaidScreenshot.OutputFolder"/>, oldest first, a few at a time.
    ///  Once the server has an image (added now or already known) it is deleted, so the folder only ever
    ///  holds what is still to be sent.
    /// </summary>
    internal sealed class RaidImageUploader
    {
        public const string EndpointPath = "/api/raids/images";
        public const int MaxPerUpload = 3;

        private readonly Action<string> _log;

        private readonly RejectedFiles _rejected = new();

        private sealed record PendingImage(FileInfo File, byte[] Bytes)
        {
            public string Name => File.Name;
        }

        private sealed class Response
        {
            [JsonPropertyName("added")]
            public int Added { get; set; }

            [JsonPropertyName("known")]
            public int Known { get; set; }

            [JsonPropertyName("rejected")]
            public List<Rejection>? Rejected { get; set; }
        }

        private sealed class Rejection
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("reason")]
            public string? Reason { get; set; }
        }

        public RaidImageUploader(Action<string> log)
        {
            _log = log;
        }

        public void ResetCache() => _rejected.Clear();

        /// <summary>
        ///  Scales down (<see cref="RaidScreenshot.ShrinkIfLarge"/>) any waiting image larger than 1920x1080, such as
        ///  those taken before screenshots were resized as they were saved. Run when sync starts.
        /// </summary>
        public async Task ShrinkWaitingAsync(CancellationToken ct)
        {
            var (resized, errors) = await Task.Run(() =>
            {
                int resized = 0;
                var errors = new List<string>();
                if (!Directory.Exists(RaidScreenshot.OutputFolder))
                {
                    return (resized, errors);
                }
                foreach (var path in Directory.EnumerateFiles(RaidScreenshot.OutputFolder, "*.webp", SearchOption.TopDirectoryOnly))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        if (RaidScreenshot.ShrinkIfLarge(path))
                        {
                            resized++;
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                    {
                        // Uploaded as it is; the server says if it's too large.
                        errors.Add($"Couldn't resize {Path.GetFileName(path)}: {ex.Message}");
                    }
                }
                return (resized, errors);
            }, ct);

            // Logged here, back on the caller's (UI) thread.
            foreach (var error in errors)
            {
                _log(error);
            }
            if (resized > 0)
            {
                _log($"Resized {Words.Count(resized, "waiting screenshot")} to fit {RaidScreenshot.MaxWidth}x{RaidScreenshot.MaxHeight}");
            }
        }

        /// <summary>
        ///  Uploads the oldest (up to <see cref="MaxPerUpload"/>) images waiting, as one request.
        /// </summary>
        /// <returns>Images the server now has, and why the upload failed, if it did.</returns>
        public async Task<(int Uploaded, string? Error)> UploadNextAsync(string url, ApiCredentials credentials, CancellationToken ct)
        {
            var images = await Task.Run(NextImages, ct);
            if (images.Count == 0)
            {
                return (0, null);
            }

            var names = images.Select(i => i.Name).ToArray();
            var blobs = images.Select(i => Convert.ToBase64String(i.Bytes)).ToArray();

            Response? result;
            try
            {
                using var response = await ApiClient.PostAsync(url, credentials, [("names", names), ("blobdat", blobs)], ct);

                // 422 is every image turned away, with the same reply listing why.
                if (!response.IsSuccessStatusCode && (int)response.StatusCode != 422)
                {
                    var detail = await ApiClient.DescribeAsync(response, ct);
                    return (0, (int)response.StatusCode == 418
                        ? $"Server refused the upload ({detail}); check the user ID, API key and PC clock"
                        : detail);
                }
                result = await response.Content.ReadFromJsonAsync<Response>(ct);
            }
            catch (Exception ex) when (ApiClient.FailureOf(ex, ct) is string failure)
            {
                return (0, failure);
            }
            if (result == null)
            {
                return (0, "the server's reply was empty");
            }

            var reasons = (result.Rejected ?? [])
                .Where(r => r.Name != null)
                .GroupBy(r => r.Name!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Reason ?? "no reason given", StringComparer.OrdinalIgnoreCase);
            int uploaded = 0;
            foreach (var image in images)
            {
                if (reasons.TryGetValue(image.Name, out var reason))
                {
                    _rejected.Add(image.File);
                    _log($"Server rejected {image.Name}: {reason} (will retry when the file changes)");
                    continue;
                }
                uploaded++;
                _rejected.Remove(image.Name);
                // If it can't be deleted, it's sent again next time and the server counts it as known.
                FileHelper.TryDelete(image.File.FullName);
            }
            if (uploaded > 0)
            {
                var size = images.Where(i => !reasons.ContainsKey(i.Name)).Sum(i => (long)i.Bytes.Length);
                _log(result.Known == 0
                    ? $"Uploaded {Words.Count(uploaded, "image")} ({size:N0} bytes)"
                    : $"Uploaded {Words.Count(uploaded, "image")} ({size:N0} bytes, {result.Known} the server already had)");
            }
            return (uploaded, null);
        }

        /// <summary>The oldest images waiting that weren't turned away as they are now.</summary>
        private List<PendingImage> NextImages()
        {
            if (!Directory.Exists(RaidScreenshot.OutputFolder))
            {
                return [];
            }

            var images = new List<PendingImage>();
            var files = new DirectoryInfo(RaidScreenshot.OutputFolder)
                .EnumerateFiles("*.webp", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f.LastWriteTimeUtc)
                .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                if (_rejected.Contains(file))
                {
                    continue;
                }
                try
                {
                    images.Add(new PendingImage(file, File.ReadAllBytes(file.FullName)));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Being moved or deleted meanwhile; picked up next time if it's still there.
                    continue;
                }
                if (images.Count == MaxPerUpload)
                {
                    break;
                }
            }
            return images;
        }
    }
}
