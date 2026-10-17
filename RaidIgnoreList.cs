using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace PlayerDBSync
{
    /// <summary>
    ///  The server's list of raid items to ignore, downloaded into the RaidsDB addon folder as <see cref="FileName"/>
    ///  in the Lua table format the addon reads with api.File:Read:
    ///  <code>
    ///  {
    ///      items = {
    ///          ["Ash Shard"] = true,
    ///      },
    ///  }
    ///  </code>
    /// </summary>
    internal static class RaidIgnoreList
    {
        public const string FileName = "ignore.dat";
        public const string EndpointPath = "/api/raids/ignored";

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(100) };

        private sealed class Response
        {
            [JsonPropertyName("items")]
            public List<string?>? Items { get; set; }
        }

        /// <summary>Fetches the item names, or the reason it couldn't.</summary>
        public static async Task<(IReadOnlyList<string>? Items, string? Error)> DownloadAsync(string url, long userId, string apiKey,
            CancellationToken ct)
        {
            var unixtime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var secret = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}{apiKey}{unixtime}")));
            var payload = new { userid = userId, unixtime, secret };

            try
            {
                using var response = await Http.PostAsJsonAsync(url, payload, ct);
                if (!response.IsSuccessStatusCode)
                {
                    // Collapse whitespace so pretty-printed JSON errors fit on one log line.
                    var body = string.Join(' ', (await response.Content.ReadAsStringAsync(ct))
                        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                    if (body.Length > 300)
                    {
                        body = body[..300] + "…";
                    }
                    return (null, $"{(int)response.StatusCode} {response.ReasonPhrase} {body}".TrimEnd());
                }

                var list = await response.Content.ReadFromJsonAsync<Response>(ct);
                if (list?.Items == null)
                {
                    return (null, "the server's reply had no items list");
                }
                return (list.Items.OfType<string>().ToList(), null);
            }
            catch (HttpRequestException ex)
            {
                return (null, ex.Message);
            }
            catch (System.Text.Json.JsonException)
            {
                return (null, "the server's reply wasn't valid JSON");
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                return (null, "Request timed out");
            }
        }

        /// <summary>
        ///  Writes the list to <paramref name="addonFolder"/>, unless the file already holds exactly this list.
        ///  Returns true if the file was written.
        /// </summary>
        public static bool Save(string addonFolder, IReadOnlyList<string> items)
        {
            var path = Path.Combine(addonFolder, FileName);
            var lua = ToLua(items);
            if (File.Exists(path) && File.ReadAllText(path) == lua)
            {
                return false;
            }

            // Write then rename, so the game never reads a half-written file.
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, lua);
            File.Move(tmp, path, overwrite: true);
            return true;
        }

        /// <summary>Formatted like the addon's own files: 4-space indents, trailing commas, CRLF.</summary>
        private static string ToLua(IEnumerable<string> items)
        {
            var sb = new StringBuilder();
            sb.Append("{\r\n    items = {\r\n");
            // Sorted so the same list always gives the same file, whatever order the server sends.
            foreach (var item in items.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                sb.Append("        [").Append(LuaString(item)).Append("] = true,\r\n");
            }
            sb.Append("    },\r\n}\r\n");
            return sb.ToString();
        }

        private static string LuaString(string value)
        {
            var sb = new StringBuilder("\"");
            foreach (var c in value)
            {
                switch (c)
                {
                    case '\\': sb.Append(@"\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append(@"\n"); break;
                    case '\r': sb.Append(@"\r"); break;
                    default:
                        if (c < ' ' || c == '\x7f')
                        {
                            // Three digits, so a following digit can't be read as part of the escape.
                            sb.Append('\\').Append(((int)c).ToString("D3", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
