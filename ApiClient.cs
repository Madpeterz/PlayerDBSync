using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PlayerDBSync
{
    /// <summary>The user ID and API key every request is signed with.</summary>
    internal readonly record struct ApiCredentials(long UserId, string ApiKey);

    /// <summary>
    ///  Requests to the server: each is a JSON POST of its fields plus userid, unixtime and secret, where the secret is
    ///  SHA-256 of the user ID, the fields exactly as sent (an array's strings joined with ","), the API key and the time.
    /// </summary>
    internal static class ApiClient
    {
        private const int MaxBodyLength = 300;

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(100) };

        /// <summary>Posts <paramref name="fields"/> (each a string or string[]), signed, in the order the secret covers them.</summary>
        public static Task<HttpResponseMessage> PostAsync(string url, ApiCredentials credentials,
            IReadOnlyList<(string Name, object Value)> fields, CancellationToken ct)
        {
            var unixtime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var signed = string.Concat(fields.Select(f => f.Value switch
            {
                string s => s,
                string[] a => string.Join(',', a),
                _ => throw new ArgumentException($"{f.Name} must be a string or string[]", nameof(fields)),
            }));
            var secret = Convert.ToHexStringLower(SHA256.HashData(
                Encoding.UTF8.GetBytes($"{credentials.UserId}{signed}{credentials.ApiKey}{unixtime}")));

            var payload = fields.ToDictionary(f => f.Name, f => f.Value);
            payload["userid"] = credentials.UserId;
            payload["unixtime"] = unixtime;
            payload["secret"] = secret;

            // Serialized up front so the request has a Content-Length: PostAsJsonAsync streams it chunked,
            // and PHP-FPM behind Apache reads a chunked body as empty (every field missing).
            var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(payload, JsonSerializerOptions.Web));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            return Http.PostAsync(url, content, ct);
        }

        /// <summary>The reply's body on one line (pretty-printed JSON errors included), cut short for the log.</summary>
        public static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken ct)
        {
            var body = string.Join(' ', (await response.Content.ReadAsStringAsync(ct))
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return body.Length > MaxBodyLength ? body[..MaxBodyLength] + "…" : body;
        }

        /// <summary>"404 Not Found &lt;body&gt;", for the log.</summary>
        public static string Describe(HttpResponseMessage response, string body) =>
            $"{(int)response.StatusCode} {response.ReasonPhrase} {body}".TrimEnd();

        public static async Task<string> DescribeAsync(HttpResponseMessage response, CancellationToken ct) =>
            Describe(response, await ReadBodyAsync(response, ct));

        /// <summary>
        ///  Why a request got no usable reply, for an exception that means that; null for any other exception
        ///  (cancellation by <paramref name="ct"/> included), so it can be a catch filter.
        /// </summary>
        public static string? FailureOf(Exception ex, CancellationToken ct) => ex switch
        {
            HttpRequestException => ex.Message,
            System.Text.Json.JsonException => "the server's reply wasn't valid JSON",
            TaskCanceledException when !ct.IsCancellationRequested => "Request timed out",
            _ => null,
        };
    }
}
