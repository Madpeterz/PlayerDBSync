using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PlayerDBSync.Tests
{
    /// <summary>
    ///  Checks the app (and <see cref="FakePlayerDbServer"/>, which the other tests run against) against the PlayerDB site's
    ///  source: its routes, the fields it reads and signs, its limits and the replies it gives. The site is found from the
    ///  PLAYERDB_WEBSITE environment variable, else Documents\PlayerDBWebsite; without it these are skipped.
    /// </summary>
    public sealed class WebsiteContractTests
    {
        private static readonly string? WebsiteRoot = FindWebsite();

        private static string? FindWebsite()
        {
            var candidates = new[]
            {
                Environment.GetEnvironmentVariable("PLAYERDB_WEBSITE"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PlayerDBWebsite"),
            };
            return candidates.FirstOrDefault(c => !string.IsNullOrEmpty(c) && File.Exists(Path.Combine(c, "routes", "api.php")));
        }

        /// <summary>The site's folder; the test is skipped without it.</summary>
        private static string Website() =>
            WebsiteRoot ?? throw Xunit.Sdk.SkipException.ForSkip("The PlayerDB website source wasn't found: set PLAYERDB_WEBSITE to its folder.");

        /// <summary>A file of the site, whitespace runs made one space so code can be matched across lines.</summary>
        private static string Php(string relativePath) =>
            Regex.Replace(File.ReadAllText(Path.Combine(Website(), relativePath)), @"\s+", " ");

        private const string Api = "app/Http/Controllers/Api/";

        /// <summary>routes/api.php: each route's path (with the /api prefix) and its controller class.</summary>
        private static Dictionary<string, string> Routes() =>
            Regex.Matches(Php("routes/api.php"), @"Route::post\('(/[^']*)', (\w+)::class\)")
                .ToDictionary(m => "/api" + m.Groups[1].Value, m => m.Groups[2].Value);

        private static string Controller(string path) => Php($"{Api}{Routes()[path]}.php");

        /// <summary>The app's source folder, found from the test's build output.</summary>
        private static string AppSource()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "PlayerDBSync.csproj")))
                {
                    return dir.FullName;
                }
            }
            throw new InvalidOperationException("PlayerDBSync.csproj not found above " + AppContext.BaseDirectory);
        }

        [Fact]
        public void TheFakeServerHasTheSitesRoutes()
        {
            Assert.Equal(FakePlayerDbServer.Endpoints.Keys.Order(), Routes().Keys.Order());
        }

        [Fact]
        public void EveryEndpointTheAppPostsToIsARoute()
        {
            var routes = Routes();
            var used = Directory.EnumerateFiles(AppSource(), "*.cs")
                .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"""(/api/[a-z/]+)""").Select(m => (File: Path.GetFileName(f), Path: m.Groups[1].Value)))
                // The old players URLs earlier builds stored, trimmed off settings.
                .Where(u => u.File != "AppSettings.cs")
                .ToList();

            Assert.NotEmpty(used);
            Assert.All(used, u => Assert.True(routes.ContainsKey(u.Path), $"{u.File} posts to {u.Path}, which the site doesn't have"));
            Assert.Equal(routes.Keys.Order(), used.Select(u => u.Path).Distinct().Order());
        }

        [Fact]
        public void TheSiteReadsTheFieldsTheAppSends()
        {
            var read = Directory.EnumerateFiles(Path.Combine(Website(), Api), "*.php", SearchOption.AllDirectories)
                .SelectMany(f => Regex.Matches(Php(Path.GetRelativePath(Website(), f)), @"->input\('(\w+)'\)").Select(m => m.Groups[1].Value))
                .Distinct()
                .Order();

            Assert.Equal(["blobdat", "names", "secret", "sextant", "unixtime", "userid"], read);
        }

        [Fact]
        public void TheSecretIsSignedAsTheAppSignsIt()
        {
            var verifies = Php($"{Api}Concerns/VerifiesUploads.php");
            Assert.Contains("hash('sha256', $userId . $signed . $member->apikey . $unixTime)", verifies);
            Assert.Contains("hash_equals($expected, strtolower($secret))", verifies);
            Assert.Contains("$this->verifiedMember($request, $scope, implode(',', $blobs))", verifies);

            foreach (var (path, kind) in FakePlayerDbServer.Endpoints)
            {
                var controller = Controller(path);
                var signing = kind switch
                {
                    EndpointKind.Upload => "$this->verifiedTables($request, '",
                    EndpointKind.RaidImages => "implode(',', $names) . implode(',', $blobs), self::SCREENSHOT_MAX_AGE_SECONDS)",
                    EndpointKind.DemoOcr => "$sextant . $blob, self::SCREENSHOT_MAX_AGE_SECONDS)",
                    _ => "$this->verifiedMember($request, 'raids', '')",
                };
                Assert.True(controller.Contains(signing), $"{path} isn't signed as the app signs it: expected {signing}");
            }
        }

        [Fact]
        public void TheLimitsAreTheSites()
        {
            var verifies = Php($"{Api}Concerns/VerifiesUploads.php");
            Assert.Contains($"MAX_FILES = {FakePlayerDbServer.MaxFiles};", verifies);
            Assert.Contains($"MAX_AGE_SECONDS = {FakePlayerDbServer.MaxAgeSeconds};", verifies);
            Assert.Contains($"SCREENSHOT_MAX_AGE_SECONDS = {FakePlayerDbServer.ScreenshotMaxAgeSeconds};", verifies);
            Assert.Contains($"MAX_IMAGES = {FakePlayerDbServer.MaxImages};", Php($"{Api}RaidImageUploadController.php"));

            Assert.InRange(DatUploader.MaxBatchSize, 1, FakePlayerDbServer.MaxFiles);
            Assert.InRange(RaidImageUploader.MaxPerUpload, 1, FakePlayerDbServer.MaxImages);
        }

        [Fact]
        public void RaidImagesAreNamedAsTheSiteTakesThem()
        {
            var store = Php("app/Services/RaidImageStore.php");
            var pattern = Regex.Match(store, @"NAME_PATTERN = '/(.*?)/';").Groups[1].Value;

            Assert.Equal(FakePlayerDbServer.RaidImageNamePattern, pattern);
            Assert.Contains("strlen($m[2]) > 6", store);
            Assert.Contains("STAMP_FORMAT = 'Y-m-d_H-i-s'", store);
        }

        [Fact]
        public void TheSextantIsWrittenAsTheSiteReadsIt()
        {
            var queue = Php("app/Services/DemoOcrQueue.php");
            var part = Regex.Match(queue, @"PART = '((?:[^'\\]|\\.)*)';").Groups[1].Value.Replace(@"\'", "'");

            Assert.Equal(FakePlayerDbServer.SextantPartPattern, part);
            Assert.Contains(@"'/^\s*' . self::PART . '\s*,?\s*' . self::PART . '\s*$/u'", queue);
        }

        public static TheoryData<string, string> Replies => new()
        {
            { "/api/raids/images", "PlayerDBSync.RaidImageUploader+Response" },
            { "/api/raids/images", "PlayerDBSync.RaidImageUploader+Rejection" },
            { "/api/demoocr", "PlayerDBSync.DemoOcrUploader+Response" },
            { "/api/raids/ignored", "PlayerDBSync.RaidIgnoreList+Response" },
        };

        [Theory]
        [MemberData(nameof(Replies))]
        public void TheSiteRepliesWithTheKeysTheAppReads(string path, string replyType)
        {
            var keys = typeof(ApiClient).Assembly.GetType(replyType, throwOnError: true)!
                .GetProperties()
                .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name)
                .OfType<string>()
                .ToList();
            var controller = Controller(path);

            Assert.NotEmpty(keys);
            Assert.All(keys, key => Assert.True(controller.Contains($"'{key}'"), $"{Routes()[path]} doesn't reply with \"{key}\""));
        }

        [Fact]
        public void TheSiteAnswersWithTheStatusesTheAppHandles()
        {
            // 418 for any request turned away: the app tells it apart from a bad file.
            Assert.Contains("response('no', 418)", Php($"{Api}Concerns/VerifiesUploads.php"));
            // 422 for a bad file, which DatUploader splits off from the rest of its batch.
            Assert.Contains("is not a Lua table: \" . $e->getMessage(), 422)", Php($"{Api}Concerns/VerifiesUploads.php"));
            // Every raid image turned away: 422 with the same counts.
            Assert.Contains("$counts['added'] + $counts['known'] === 0 ? 422 : 200", Controller("/api/raids/images"));
            // Demo OCR: 422 a bad picture, 409 already queued or near a demo, 429 too many waiting.
            Assert.Contains(", 422)", Controller("/api/demoocr"));
            var queue = Php("app/Services/DemoOcrQueue.php");
            Assert.Contains("409", queue);
            Assert.Contains("429", queue);
        }
    }
}
