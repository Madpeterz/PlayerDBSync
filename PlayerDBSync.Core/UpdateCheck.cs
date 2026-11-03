using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace PlayerDBSync
{
    /// <summary>Whether GitHub has a newer release than this one (tags as release.yml takes them: 1.2.3, v1.2.3, 1.2.3-beta).</summary>
    internal static class UpdateCheck
    {
        public const string Repository = "Madpeterz/PlayerDBSync";

        // GitHub's API turns away requests without a User-Agent.
        private static readonly HttpClient Http = CreateClient();

        private sealed class Release
        {
            [JsonPropertyName("tag_name")]
            public string? TagName { get; set; }
        }

        private static HttpClient CreateClient()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PlayerDBSync", "1"));
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            return http;
        }

        /// <summary>
        ///  The latest release's tag if it is newer than <paramref name="current"/>; null if not, if it couldn't be
        ///  found out, or if this is a dev build (its version a commit, not a tag).
        /// </summary>
        public static async Task<string?> NewerReleaseAsync(string current)
        {
            if (!IsRelease(current))
            {
                return null;
            }
            try
            {
                // The latest full release: pre-releases and drafts aren't offered.
                var release = await Http.GetFromJsonAsync<Release>($"https://api.github.com/repos/{Repository}/releases/latest");
                return release?.TagName is string tag && IsNewer(tag, current) ? tag : null;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                return null;
            }
        }

        /// <summary>True for a release's version (a tag); false for a dev build's (a commit).</summary>
        public static bool IsRelease(string version) => TryParse(version, out _, out _);

        /// <summary>True if <paramref name="latest"/> is a later version, or the release of the pre-release <paramref name="current"/> is.</summary>
        public static bool IsNewer(string latest, string current)
        {
            if (!TryParse(latest, out var l, out var lPre) || !TryParse(current, out var c, out var cPre))
            {
                return false;
            }
            int cmp = l.CompareTo(c);
            return cmp > 0 || (cmp == 0 && cPre && !lPre);
        }

        private static bool TryParse(string tag, out Version version, out bool prerelease)
        {
            tag = tag.TrimStart('v', 'V');
            int dash = tag.IndexOf('-');
            prerelease = dash >= 0;
            if (!Version.TryParse(dash >= 0 ? tag[..dash] : tag, out var parsed))
            {
                version = new Version();
                return false;
            }
            // So 1.2 and 1.2.0 compare equal.
            version = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0));
            return true;
        }
    }
}
