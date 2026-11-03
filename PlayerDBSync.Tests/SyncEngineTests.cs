using SkiaSharp;

namespace PlayerDBSync.Tests
{
    /// <summary>Answers each capture with a flat picture of the size it's told to (or why it can't), and counts them.</summary>
    internal sealed class FakeCapture(int width, int height, string? error = null) : IGameCapture
    {
        public int Captures;

        public (SKBitmap? Image, string? Error) Capture()
        {
            Interlocked.Increment(ref Captures);
            if (error != null)
            {
                return (null, error);
            }
            var image = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
            image.Erase(new SKColor(40, 90, 150));
            return (image, null);
        }
    }

    /// <summary>The whole app minus its window: the engine against the fake server, with the files an addon would write.</summary>
    public sealed class SyncEngineTests : IDisposable
    {
        private readonly FakePlayerDbServer _server = new();
        private readonly TempFolder _playerDb = new();
        private readonly TempFolder _raidsDb = new();
        private readonly List<string> _log = [];
        private SyncEngine? _engine;

        public SyncEngineTests()
        {
            _playerDb.Write("main.lua", "");
            Directory.CreateDirectory(Path.Combine(_playerDb.Path, "Players"));
            File.WriteAllText(Path.Combine(_playerDb.Path, "Players", "ab.dat"), "{ addon_version = \"2.7.1\", name = \"Ab\", }");
            _raidsDb.Write("main.lua", "");
            Directory.CreateDirectory(Path.Combine(_raidsDb.Path, "Raids"));
        }

        public void Dispose()
        {
            _engine?.Stop();
            _engine?.Dispose();
            _server.Dispose();
            _playerDb.Dispose();
            _raidsDb.Dispose();
        }

        private SyncInput Input(string raidFolder = "") =>
            new(_playerDb.Path, raidFolder, _server.BaseUrl + "/", FakePlayerDbServer.UserId.ToString(), FakePlayerDbServer.ApiKey);

        private SyncEngine Engine(IGameCapture? capture = null)
        {
            _engine = new SyncEngine(new AppSettings(), capture ?? new FakeCapture(100, 100));
            _engine.Logged += _log.Add;
            return _engine;
        }

        private static async Task<bool> WaitFor(Func<bool> done, int seconds = 15)
        {
            var until = DateTime.UtcNow.AddSeconds(seconds);
            while (!done())
            {
                if (DateTime.UtcNow > until)
                {
                    return false;
                }
                await Task.Delay(100, TestContext.Current.CancellationToken);
            }
            return true;
        }

        [Fact]
        public async Task StartsByUploadingWhatIsThere()
        {
            var engine = Engine();

            var (problem, firstRuns) = engine.Start(Input(), false, false);
            await firstRuns;

            Assert.Null(problem);
            Assert.True(engine.Syncing);
            var request = Assert.Single(_server.Requests, r => r.Path == "/api/players");
            Assert.Null(request.Rejection);
            Assert.Contains("addon_version", Assert.Single(request.Blobs.Select(System.Text.Encoding.UTF8.GetString)));
            Assert.Contains(_log, line => line.StartsWith("Players: Uploaded"));
            Assert.Contains(_log, line => line.StartsWith("Homes: skipped"));
            // A trailing slash on the address is dealt with, and what was synced is remembered.
            Assert.Equal(_server.BaseUrl, engine.Settings.Url);
            Assert.Single(engine.Settings.SyncedFiles);
        }

        [Fact]
        public async Task SaysWhatIsWrongAndDoesNotStart()
        {
            var engine = Engine();

            var (problem, firstRuns) = engine.Start(Input() with { UserId = "nope" }, false, false);
            await firstRuns;

            Assert.Equal(InputField.UserId, problem!.Field);
            Assert.False(engine.Syncing);
            Assert.Empty(_server.Requests);
        }

        [Fact]
        public async Task ShowsEachEventsStatusAndStopsCleanly()
        {
            var engine = Engine();
            var shown = new List<string>();
            engine.StatusChanged += ev => shown.Add($"{ev.Label}: {ev.Status}");

            var (_, firstRuns) = engine.Start(Input(), false, false);
            await firstRuns;
            engine.Stop();

            Assert.Contains(shown, s => s.StartsWith("Players: Checking for changes"));
            Assert.Contains(shown, s => s.StartsWith("Players: Last checked"));
            Assert.False(engine.Syncing);
            Assert.Equal("Stopped", _log[^1]);
            Assert.All(engine.Scheduled.Where(e => e.Enabled), e => Assert.InRange(e.Fraction(DateTime.Now), 0.5, 1.0));
        }

        [Fact]
        public async Task AServerErrorFailsWithBackoff()
        {
            var engine = Engine();
            _server.Respond = _ => Reply.Text("down for maintenance", 503);

            var (_, firstRuns) = engine.Start(Input(), false, false);
            await firstRuns;

            var players = engine.Jobs.Single(j => j.Name == "Players");
            Assert.True(players.Failing);
            Assert.Equal(1, players.FailureCount);
            Assert.Contains(_log, line => line.StartsWith("Players:") && line.Contains("retrying with backoff"));
            Assert.Contains("Failing (1x)", players.Status);
            // The next try is the usual 30 to 45 s away or more (doubling with each failure in a row).
            Assert.True(players.DueAt - players.WaitStart >= TimeSpan.FromSeconds(30));
        }

        [Fact]
        public async Task AnswersARaidScreenshotRequest()
        {
            var capture = new FakeCapture(1920, 1080);
            var engine = Engine(capture);
            var raids = Path.Combine(_raidsDb.Path, "Raids");
            File.WriteAllText(Path.Combine(raids, "request.sht"), "{\n    raid_started = \"1792312031\",\n    id = \"1792312031-2\",\n    reason = \"end\",\n}\n");

            var (_, firstRuns) = engine.Start(Input(_raidsDb.Path), raidScreenshots: true, demoOcrScreenshots: false);
            await firstRuns;

            // The watcher checks every 2 s, saves the picture and removes the request. (The upload that follows is on
            // the 30 s schedule the window's timer drives, and is the uploader's own tests' business.)
            Assert.True(await WaitFor(() => !File.Exists(Path.Combine(raids, "request.sht"))), "the request was never answered:\n" + string.Join('\n', _log));
            var saved = Path.Combine(RaidScreenshot.OutputFolder, "2026-10-18_08-27-11-2-end.webp");
            Assert.True(File.Exists(saved));
            using (var codec = SKCodec.Create(saved))
            {
                Assert.Equal((1920, 1080), (codec.Info.Width, codec.Info.Height));
            }
            Assert.True(capture.Captures >= 1);
            Assert.Contains(_log, line => line.Contains("2026-10-18_08-27-11-2-end.webp"));
        }

        [Fact]
        public async Task NothingIsCapturedWhileTheBoxIsUnticked()
        {
            var capture = new FakeCapture(100, 100);
            var engine = Engine(capture);
            var raids = Path.Combine(_raidsDb.Path, "Raids");
            File.WriteAllText(Path.Combine(raids, "request.sht"), "{ reason = \"end\", }");

            var (_, firstRuns) = engine.Start(Input(_raidsDb.Path), raidScreenshots: false, demoOcrScreenshots: false);
            await firstRuns;
            await Task.Delay(TimeSpan.FromSeconds(4.5), TestContext.Current.CancellationToken);

            Assert.Equal(0, capture.Captures);
            Assert.True(File.Exists(Path.Combine(raids, "request.sht")));
        }

        [Fact]
        public async Task ACaptureThatFailsIsRetriedAndLoggedOnce()
        {
            var capture = new FakeCapture(100, 100, "the game window is minimised");
            var engine = Engine(capture);
            var raids = Path.Combine(_raidsDb.Path, "Raids");
            File.WriteAllText(Path.Combine(raids, "request.sht"), "{ reason = \"end\", }");

            var (_, firstRuns) = engine.Start(Input(_raidsDb.Path), raidScreenshots: true, demoOcrScreenshots: false);
            await firstRuns;
            Assert.True(await WaitFor(() => capture.Captures >= 2), "the capture wasn't retried");

            Assert.Single(_log, line => line.Contains("the game window is minimised"));
            Assert.True(File.Exists(Path.Combine(raids, "request.sht")));
        }

        [Fact]
        public async Task AnswersADemoOcrRequest()
        {
            var demos = Path.Combine(_playerDb.Path, "Demos");
            Directory.CreateDirectory(demos);
            File.WriteAllText(Path.Combine(demos, "request.dat"), "{ status = true, }");
            // Written after the request and kept fresh, as the addon does while a request is open.
            File.WriteAllText(Path.Combine(demos, "location.dat"),
                "{ location = { deg_lat = 16, min_lat = 10, sec_lat = 20, latitude = \"S\", deg_long = 9, min_long = 41, sec_long = 2, longitude = \"W\", }, }");
            var engine = Engine(new FakeCapture(DemoOcr.Width, DemoOcr.Height));

            var (_, firstRuns) = engine.Start(Input(), raidScreenshots: false, demoOcrScreenshots: true);
            await firstRuns;
            using var keepFresh = new Timer(_ => File.SetLastWriteTimeUtc(Path.Combine(demos, "location.dat"), DateTime.UtcNow), null, 0, 500);

            Assert.True(await WaitFor(() => File.ReadAllText(Path.Combine(demos, "request.dat")).Contains("status = false")),
                "the request was never answered:\n" + string.Join('\n', _log));
            Assert.True(await WaitFor(() => _server.Requests.Any(r => r.Path == DemoOcrUploader.EndpointPath), 30),
                "the screenshot was never uploaded:\n" + string.Join('\n', _log));
            Assert.Contains(_log, line => line.Contains("16° 10' 20\" S, 9° 41' 2\" W"));
        }

        [Fact]
        public async Task ClearsTheAddonsDataAndForgetsWhatWasSynced()
        {
            var engine = Engine();
            var (_, firstRuns) = engine.Start(Input(), false, false);
            await firstRuns;
            engine.Stop();
            Assert.NotEmpty(engine.Settings.SyncedFiles);

            var plan = engine.PlanClear(Input());
            var folder = Assert.Single(plan);
            Assert.Equal(["ab.dat"], folder.Files.Select(Path.GetFileName));

            // The Trash or Recycle Bin is the user's own; only the failure path is exercised for real.
            File.Delete(folder.Files[0]);
            var result = engine.Clear(plan);

            Assert.Equal(0, result.Cleared);
            Assert.Single(result.Failed);
            Assert.Empty(engine.Settings.SyncedFiles);
        }
    }
}
