namespace PlayerDBSync.Tests
{
    public sealed class RaidImageUploaderTests : IDisposable
    {
        private readonly FakePlayerDbServer _server = new();
        private readonly TempFolder _folder = new();
        private readonly List<string> _log = [];
        private readonly RaidImageUploader _uploader;

        private static readonly string RaidStart = RaidScreenshot.FileStamp(new DateTime(2026, 10, 17, 19, 7, 20, DateTimeKind.Utc));

        public RaidImageUploaderTests()
        {
            _uploader = new RaidImageUploader(_log.Add, _folder.Path);
        }

        public void Dispose()
        {
            _server.Dispose();
            _folder.Dispose();
        }

        private Task<(int Uploaded, string? Error)> UploadNextAsync() =>
            _uploader.UploadNextAsync(_server.Url(RaidImageUploader.EndpointPath), FakePlayerDbServer.Credentials, TestContext.Current.CancellationToken);

        // Oldest first: each a minute older than the next.
        private void Images(params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                _folder.Write(names[i], [(byte)i, 1, 2, 3], TimeSpan.FromMinutes(names.Length - i));
            }
        }

        [Fact]
        public void FileStampsAreUtcWhateverTheKindOfTime()
        {
            var utc = new DateTime(2026, 10, 17, 19, 7, 20, DateTimeKind.Utc);

            Assert.Equal("2026-10-17_19-07-20", RaidScreenshot.FileStamp(utc));
            Assert.Equal("2026-10-17_19-07-20", RaidScreenshot.FileStamp(utc.ToLocalTime()));
        }

        [Fact]
        public void TheAppNamesImagesAsTheSiteTakesThem()
        {
            Assert.Equal("2026-10-17_19-07-20", RaidStart);
            Assert.True(FakePlayerDbServer.IsRaidImageName($"{RaidStart}-1.webp"));
            Assert.True(FakePlayerDbServer.IsRaidImageName($"{RaidStart}-12-end.webp"));
        }

        [Fact]
        public async Task UploadsNamesAndImagesTogetherAndDeletesThem()
        {
            Images($"{RaidStart}-1-start.webp", $"{RaidStart}-2.webp");

            var result = await UploadNextAsync();

            Assert.Equal((2, (string?)null), result);
            var request = Assert.Single(_server.Requests);
            Assert.Null(request.Rejection);
            Assert.Equal([$"{RaidStart}-1-start.webp", $"{RaidStart}-2.webp"], request.Strings("names"));
            Assert.Equal([0, 1], request.Blobs.Select(b => (int)b[0]));
            Assert.Empty(_folder.Files());
        }

        [Fact]
        public async Task NeverSendsMoreImagesAtOnceThanTheSiteTakes()
        {
            Images(Enumerable.Range(1, 6).Select(n => $"{RaidStart}-{n}.webp").ToArray());

            var result = await UploadNextAsync();

            Assert.Equal(RaidImageUploader.MaxPerUpload, result.Uploaded);
            Assert.InRange(RaidImageUploader.MaxPerUpload, 1, FakePlayerDbServer.MaxImages);
            Assert.Equal([$"{RaidStart}-1.webp", $"{RaidStart}-2.webp", $"{RaidStart}-3.webp"], Assert.Single(_server.Requests).Strings("names"));
            Assert.Equal(3, _folder.Files().Count);
        }

        [Fact]
        public async Task KeepsTheImagesTheSiteTurnsAwayAndSaysWhy()
        {
            Images($"{RaidStart}-1.webp", "holiday.webp");

            var result = await UploadNextAsync();

            Assert.Equal((1, (string?)null), result);
            Assert.Equal(["holiday.webp"], _folder.Files());
            Assert.Contains("Server rejected holiday.webp: not named <raid start>-<n>[-<reason>].webp (will retry when the file changes)", _log);

            // Left alone until it changes.
            Assert.Equal((0, (string?)null), await UploadNextAsync());
            Assert.Single(_server.Requests);
        }

        [Fact]
        public async Task EveryImageTurnedAwayIsNotAnError()
        {
            // The site answers 422 with the same counts and reasons when it took none.
            Images("holiday.webp");

            var result = await UploadNextAsync();

            Assert.Equal((0, (string?)null), result);
            Assert.Equal(["holiday.webp"], _folder.Files());
        }

        [Fact]
        public async Task CountsTheImagesTheSiteAlreadyHad()
        {
            _server.Respond = r => Reply.Json(new { images = 2, added = 1, known = 1, rejected = Array.Empty<object>() });
            Images($"{RaidStart}-1.webp", $"{RaidStart}-2.webp");

            Assert.Equal((2, (string?)null), await UploadNextAsync());
            Assert.Contains("Uploaded 2 images (8 bytes, 1 the server already had)", _log);
        }

        [Fact]
        public async Task TurnedAwayAsAWholeIsAnError()
        {
            _server.ExpectedApiKey = "a different key";
            Images($"{RaidStart}-1.webp");

            var (uploaded, error) = await UploadNextAsync();

            Assert.Equal(0, uploaded);
            Assert.StartsWith("Server refused the upload (418", error);
            Assert.Single(_folder.Files());
        }

        [Fact]
        public async Task NothingWaitingSendsNothing()
        {
            Assert.Equal((0, (string?)null), await UploadNextAsync());
            Assert.Empty(_server.Requests);
        }
    }

    public sealed class DemoOcrUploaderTests : IDisposable
    {
        private readonly FakePlayerDbServer _server = new();
        private readonly TempFolder _folder = new();
        private readonly List<string> _log = [];
        private readonly DemoOcrUploader _uploader;

        private static readonly DemoLocation Location = new(16, 10, 20, 'S', 9, 41, 2, 'W');

        public DemoOcrUploaderTests()
        {
            _uploader = new DemoOcrUploader(_log.Add, _folder.Path);
        }

        public void Dispose()
        {
            _server.Dispose();
            _folder.Dispose();
        }

        private Task<(int Uploaded, string? Error)> UploadNextAsync() =>
            _uploader.UploadNextAsync(_server.Url(DemoOcrUploader.EndpointPath), FakePlayerDbServer.Credentials, TestContext.Current.CancellationToken);

        // Named as DemoOcr.PathFor names them, and old enough to be sent.
        private string Picture(DemoLocation location, int minutesAgo = 1) =>
            Path.GetFileName(_folder.Write($"{RaidScreenshot.FileStamp(DateTime.UtcNow.AddMinutes(-minutesAgo))}_{location.FileToken}.webp",
                [1, 2, 3], TimeSpan.FromMinutes(minutesAgo)));

        [Fact]
        public void TheAppWritesTheSextantAsTheSiteReadsIt()
        {
            Assert.Equal("16° 10' 20\" S, 9° 41' 2\" W", Location.Sextant);
            Assert.True(FakePlayerDbServer.IsSextant(Location.Sextant));
            Assert.True(FakePlayerDbServer.IsSextant(new DemoLocation(90, 0, 0, 'N', 180, 59, 59, 'E').Sextant));
        }

        [Fact]
        public async Task UploadsThePictureWithItsSextantAndDeletesIt()
        {
            Picture(Location);

            var result = await UploadNextAsync();

            Assert.Equal((1, (string?)null), result);
            var request = Assert.Single(_server.Requests);
            Assert.Null(request.Rejection);
            Assert.Equal(Location.Sextant, request.Field("sextant").GetString());
            Assert.Equal(System.Text.Json.JsonValueKind.String, request.Field("blobdat").ValueKind);
            Assert.Equal([1, 2, 3], Assert.Single(request.Blobs));
            Assert.Contains("Uploaded 16° 10' 20\" S, 9° 41' 2\" W (3 bytes), request 12, place 3 in the queue", _log);
            Assert.Empty(_folder.Files());
        }

        [Fact]
        public async Task SendsOnePicturePerRequestOldestFirst()
        {
            Picture(Location, minutesAgo: 1);
            Picture(new DemoLocation(1, 2, 3, 'N', 4, 5, 6, 'E'), minutesAgo: 2);

            Assert.Equal((2, (string?)null), await UploadNextAsync());

            Assert.Equal(["1° 2' 3\" N, 4° 5' 6\" E", Location.Sextant], _server.Requests.Select(r => r.Field("sextant").GetString()));
        }

        [Fact]
        public async Task LeavesAJustSavedPictureAMoment()
        {
            _folder.Write($"{RaidScreenshot.FileStamp(DateTime.UtcNow)}_{Location.FileToken}.webp", [1, 2, 3]);

            Assert.Equal((0, (string?)null), await UploadNextAsync());
            Assert.Empty(_server.Requests);
        }

        [Fact]
        public async Task SkipsAPictureNotNamedAsTheAppSavesThem()
        {
            _folder.Write("holiday.webp", [1, 2, 3], TimeSpan.FromMinutes(1));

            Assert.Null((await UploadNextAsync()).Error);
            Assert.Empty(_server.Requests);
            Assert.Equal(["holiday.webp"], _folder.Files());
        }

        [Fact]
        public async Task OneTheSiteAlreadyHasIsDeleted()
        {
            _server.Respond = _ => Reply.Json(new
            {
                message = "there is already a demo within 4m of 16° 10' 20\" S, 9° 41' 2\" W: Stumpy's Lunar Scarecrow Garden",
                owner = "Stumpy",
                building_type = "Lunar Scarecrow Garden",
                building = "Moonbeans",
            }, 409);
            Picture(Location);

            Assert.Equal((1, (string?)null), await UploadNextAsync());
            Assert.Empty(_folder.Files());
            Assert.Contains(_log, l => l.StartsWith("Server already has") && l.Contains("Stumpy"));
        }

        [Fact]
        public async Task OneTheSiteFindsWrongIsKept()
        {
            _server.Respond = _ => Reply.Text("blobdat is not 2560x1440 (1920x1080)", 422);
            var name = Picture(Location);

            Assert.Null((await UploadNextAsync()).Error);
            Assert.Equal([name], _folder.Files());
            Assert.Contains($"Server rejected {name}: blobdat is not 2560x1440 (1920x1080) (will retry when the file changes)", _log);

            Assert.Equal((0, (string?)null), await UploadNextAsync());
            Assert.Single(_server.Requests);
        }

        [Fact]
        public async Task TooManyWaitingStopsTheUpload()
        {
            _server.Respond = _ => Reply.Text("you already have 10 requests waiting", 429);
            Picture(Location);

            var (uploaded, error) = await UploadNextAsync();

            Assert.Equal(0, uploaded);
            Assert.StartsWith("429", error);
            Assert.Single(_folder.Files());
        }

        [Fact]
        public async Task TurnedAwayIsAnErrorNamingTheDemoOcrFlag()
        {
            _server.ExpectedApiKey = "a different key";
            Picture(Location);

            var (_, error) = await UploadNextAsync();

            Assert.StartsWith("Server refused the upload (418", error);
            Assert.Contains("Demo OCR", error);
        }
    }
}
