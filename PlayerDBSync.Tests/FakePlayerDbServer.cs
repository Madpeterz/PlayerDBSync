using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PlayerDBSync.Tests
{
    /// <summary>What an endpoint takes, as the site's controller reads it (app/Http/Controllers/Api).</summary>
    internal enum EndpointKind
    {
        /// <summary>blobdat: one base64 .dat file or a list of up to <see cref="FakePlayerDbServer.MaxFiles"/> (verifiedTables).</summary>
        Upload,

        /// <summary>names and blobdat, up to <see cref="FakePlayerDbServer.MaxImages"/> each (RaidImageUploadController).</summary>
        RaidImages,

        /// <summary>sextant and one blobdat (DemoOcrUploadController).</summary>
        DemoOcr,

        /// <summary>Nothing but the signature (RaidIgnoredItemsController).</summary>
        SendsBack,
    }

    /// <summary>A request the fake server received, and why it was turned away (null if it wasn't).</summary>
    internal sealed record ReceivedRequest(string Path, string? ContentType, long ContentLength, bool Chunked, JsonElement Body, string? Rejection)
    {
        public JsonElement Field(string name) => Body.GetProperty(name);

        /// <summary>A string field, or a list of them, as a list.</summary>
        public IReadOnlyList<string> Strings(string name) => FakePlayerDbServer.StringsOf(Body, name)
            ?? throw new InvalidOperationException($"{name} is not a string or a list of them");

        public IReadOnlyList<byte[]> Blobs => Strings("blobdat").Select(Convert.FromBase64String).ToList();
    }

    /// <summary>The fake server's answer: a status, a body and its content type.</summary>
    internal sealed record Reply(int Status, string Body, string ContentType = "text/plain")
    {
        public static Reply Json(object value, int status = 200) => new(status, JsonSerializer.Serialize(value), "application/json");

        public static Reply Text(string text, int status) => new(status, text);
    }

    /// <summary>
    ///  A stand-in for the PlayerDB site's API (PlayerDBWebsite routes/api.php), on a local port. Requests are checked as the
    ///  site's VerifiesUploads checks them: the JSON fields each endpoint reads, unixtime within its window, and the secret,
    ///  SHA-256 hex of "&lt;userid&gt;&lt;signed fields&gt;&lt;apikey&gt;&lt;unixtime&gt;". A request that fails gets 418 "no", with why kept
    ///  in <see cref="ReceivedRequest.Rejection"/>. The rest get <see cref="Respond"/>'s reply, else one shaped as the site's.
    ///  The constants here are the site's; WebsiteContractTests checks them against its source.
    /// </summary>
    internal sealed class FakePlayerDbServer : IDisposable
    {
        public const long UserId = 4;
        public const string ApiKey = "test-api-key";
        public static readonly ApiCredentials Credentials = new(UserId, ApiKey);

        // VerifiesUploads
        public const int MaxFiles = 500;
        public const int MaxAgeSeconds = 10;
        public const int ScreenshotMaxAgeSeconds = 120;

        // RaidImageUploadController::MAX_IMAGES
        public const int MaxImages = 4;

        /// <summary>routes/api.php: every endpoint, and what it takes.</summary>
        public static readonly IReadOnlyDictionary<string, EndpointKind> Endpoints = new Dictionary<string, EndpointKind>
        {
            ["/api/players"] = EndpointKind.Upload,
            ["/api/homes"] = EndpointKind.Upload,
            ["/api/regrades"] = EndpointKind.Upload,
            ["/api/raids"] = EndpointKind.Upload,
            ["/api/raids/images"] = EndpointKind.RaidImages,
            ["/api/raids/ignored"] = EndpointKind.SendsBack,
            ["/api/inventory"] = EndpointKind.Upload,
            ["/api/demoocr"] = EndpointKind.DemoOcr,
        };

        // RaidImageStore::NAME_PATTERN, and its number of up to 6 digits
        public const string RaidImageNamePattern = @"^(\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2})-(\d+)(?:-([a-z0-9]{1,20}))?\.webp$";

        // DemoOcrQueue::PART, one sextant coordinate: parseSextant takes two, by a comma or not
        public const string SextantPartPattern = @"([NSEW])?\s*(\d{1,3})\s*°\s*(\d{1,2})\s*['′]\s*(\d{1,2})\s*[""″]\s*([NSEW])?";

        private static readonly Regex RaidImageName = new(RaidImageNamePattern);
        private static readonly Regex Sextant = new(@"^\s*" + SextantPartPattern + @"\s*,?\s*" + SextantPartPattern + @"\s*$");

        private readonly HttpListener _listener = new();
        private readonly ConcurrentQueue<ReceivedRequest> _requests = new();

        public FakePlayerDbServer()
        {
            BaseUrl = $"http://localhost:{FreePort()}";
            _listener.Prefixes.Add(BaseUrl + "/");
            _listener.Start();
            _ = ServeAsync();
        }

        /// <summary>Like the site's URL: the endpoints' paths go after it.</summary>
        public string BaseUrl { get; }

        /// <summary>The API key requests are checked with; change it to have every request turned away.</summary>
        public string ExpectedApiKey { get; set; } = ApiKey;

        /// <summary>The reply to a request that passed the checks; null (or returning null) for the site's usual one.</summary>
        public Func<ReceivedRequest, Reply?>? Respond { get; set; }

        public IReadOnlyList<ReceivedRequest> Requests => _requests.ToList();

        public string Url(string path) => BaseUrl + path;

        public void Dispose() => _listener.Close();

        /// <summary>Whether <paramref name="name"/> is a raid image name the site takes (RaidImageStore::parse).</summary>
        public static bool IsRaidImageName(string name)
        {
            var m = RaidImageName.Match(name);
            return m.Success && m.Groups[2].Value.Length <= 6
                && DateTime.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd_HH-mm-ss", null, System.Globalization.DateTimeStyles.None, out _);
        }

        /// <summary>Whether <paramref name="text"/> is a location the site takes (DemoOcrQueue::parseSextant).</summary>
        public static bool IsSextant(string text)
        {
            var m = Sextant.Match(text.ToUpperInvariant());
            if (!m.Success)
            {
                return false;
            }
            var axes = new HashSet<string>();
            foreach (var at in new[] { 1, 6 })
            {
                var letter = m.Groups[at].Value + m.Groups[at + 4].Value;
                int deg = int.Parse(m.Groups[at + 1].Value), min = int.Parse(m.Groups[at + 2].Value), sec = int.Parse(m.Groups[at + 3].Value);
                if (letter.Length != 1 || min > 59 || sec > 59)
                {
                    return false;
                }
                var lat = letter is "N" or "S";
                if (!axes.Add(lat ? "lat" : "long") || deg > (lat ? 90 : 180))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>A field that is a string or a list of strings, as a list; null if it is neither or missing.</summary>
        public static IReadOnlyList<string>? StringsOf(JsonElement body, string name)
        {
            if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty(name, out var value))
            {
                return null;
            }
            return value.ValueKind switch
            {
                JsonValueKind.String => [value.GetString()!],
                JsonValueKind.Array when value.EnumerateArray().All(e => e.ValueKind == JsonValueKind.String) =>
                    value.EnumerateArray().Select(e => e.GetString()!).ToList(),
                _ => null,
            };
        }

        private static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private async Task ServeAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
                {
                    return;
                }
                try
                {
                    Answer(context);
                }
                catch (Exception ex)
                {
                    Write(context.Response, new Reply(500, ex.ToString()));
                }
            }
        }

        private void Answer(HttpListenerContext context)
        {
            var request = context.Request;
            var path = request.Url!.AbsolutePath;
            if (request.HttpMethod != "POST" || !Endpoints.TryGetValue(path, out var kind))
            {
                Write(context.Response, Reply.Text("Not Found", 404));
                return;
            }

            string text;
            using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
            {
                text = reader.ReadToEnd();
            }
            JsonElement body;
            try
            {
                body = JsonDocument.Parse(text).RootElement.Clone();
            }
            catch (JsonException)
            {
                body = default;
            }

            var chunked = string.Equals(request.Headers["Transfer-Encoding"], "chunked", StringComparison.OrdinalIgnoreCase);
            var received = new ReceivedRequest(path, request.ContentType, request.ContentLength64, chunked, body, Check(kind, body));
            _requests.Enqueue(received);

            Write(context.Response, received.Rejection != null
                ? Reply.Text("no", 418)
                : Respond?.Invoke(received) ?? UsualReply(kind, received));
        }

        /// <summary>Why the site would turn the request away with 418, as VerifiesUploads and the controllers check; null if it wouldn't.</summary>
        private string? Check(EndpointKind kind, JsonElement body)
        {
            if (body.ValueKind != JsonValueKind.Object)
            {
                return "not a JSON object";
            }

            string signed;
            int maxAge = MaxAgeSeconds;
            switch (kind)
            {
                case EndpointKind.Upload:
                    var blobs = StringsOf(body, "blobdat");
                    if (BlobsProblem(blobs, MaxFiles) is string problem)
                    {
                        return problem;
                    }
                    signed = string.Join(',', blobs!);
                    break;

                case EndpointKind.RaidImages:
                    var images = StringsOf(body, "blobdat");
                    if (BlobsProblem(images, MaxImages) is string imagesProblem)
                    {
                        return imagesProblem;
                    }
                    var names = StringsOf(body, "names");
                    if (names == null)
                    {
                        return "names is not a string or a list of them";
                    }
                    if (names.Count != images!.Count)
                    {
                        return $"{names.Count} names for {images.Count} images";
                    }
                    signed = string.Join(',', names) + string.Join(',', images);
                    maxAge = ScreenshotMaxAgeSeconds;
                    break;

                case EndpointKind.DemoOcr:
                    if (!body.TryGetProperty("sextant", out var sextant) || sextant.ValueKind != JsonValueKind.String)
                    {
                        return "no sextant";
                    }
                    if (!body.TryGetProperty("blobdat", out var blob) || blob.ValueKind != JsonValueKind.String || blob.GetString() == "")
                    {
                        return "blobdat is not one image";
                    }
                    signed = sextant.GetString() + blob.GetString();
                    maxAge = ScreenshotMaxAgeSeconds;
                    break;

                default:
                    signed = "";
                    break;
            }

            var userId = IntOf(body, "userid");
            var unixTime = IntOf(body, "unixtime");
            if (!body.TryGetProperty("secret", out var secret) || secret.ValueKind != JsonValueKind.String || userId == null || unixTime == null)
            {
                return "no secret, userid or unixtime (or not a number)";
            }

            var age = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - unixTime.Value;
            if (age > maxAge || age < -MaxAgeSeconds)
            {
                return $"unixtime is {age} seconds old";
            }
            if (userId != UserId)
            {
                return $"no user {userId}";
            }

            var expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}{signed}{ExpectedApiKey}{unixTime}")));
            return expected == secret.GetString()!.ToLowerInvariant() ? null : "the secret doesn't match";
        }

        // VerifiesUploads::blobsProblem
        private static string? BlobsProblem(IReadOnlyList<string>? blobs, int max) => blobs switch
        {
            null => "blobdat is not a string or a list of them",
            [] => "blobdat is an empty list",
            _ when blobs.Count > max => $"blobdat has {blobs.Count} files, more than {max}",
            _ when blobs.Any(b => b == "") => "blobdat has an empty file",
            _ => null,
        };

        // FILTER_VALIDATE_INT: a JSON integer, or a string of one
        private static long? IntOf(JsonElement body, string name)
        {
            if (!body.TryGetProperty(name, out var value))
            {
                return null;
            }
            return value.ValueKind switch
            {
                JsonValueKind.Number when value.TryGetInt64(out var n) => n,
                JsonValueKind.String when long.TryParse(value.GetString(), out var n) => n,
                _ => null,
            };
        }

        /// <summary>The site's answer to a request it took, shaped as its controller's (with made-up counts).</summary>
        private static Reply UsualReply(EndpointKind kind, ReceivedRequest request)
        {
            switch (kind)
            {
                case EndpointKind.RaidImages:
                    var names = request.Strings("names");
                    var rejected = names.Where(n => !IsRaidImageName(n)).Select(n => new { name = n, reason = "not named <raid start>-<n>[-<reason>].webp" }).ToList();
                    var added = names.Count - rejected.Count;
                    return Reply.Json(new { images = names.Count, added, known = 0, rejected }, added == 0 ? 422 : 200);

                case EndpointKind.DemoOcr:
                    return IsSextant(request.Field("sextant").GetString()!)
                        ? Reply.Json(new { id = 12, position = 3 })
                        : Reply.Text("sextant is not a location like 16° 10' 20\" S, 9° 41' 2\" W", 422);

                case EndpointKind.SendsBack:
                    return Reply.Json(new { items = Array.Empty<string>() });

                default:
                    return Reply.Json(new { files = request.Strings("blobdat").Count, outdated = 0, added = 0, changes = 0 });
            }
        }

        private static void Write(HttpListenerResponse response, Reply reply)
        {
            var bytes = Encoding.UTF8.GetBytes(reply.Body);
            response.StatusCode = reply.Status;
            response.ContentType = reply.ContentType + "; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes);
            response.Close();
        }
    }
}
