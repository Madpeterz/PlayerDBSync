using System.Text;

namespace PlayerDBSync.Tests
{
    public sealed class DatUploaderTests : IDisposable
    {
        private readonly FakePlayerDbServer _server = new();
        private readonly TempFolder _folder = new();
        private readonly List<string> _log = [];
        private readonly Dictionary<string, SyncRecord> _synced = new(StringComparer.OrdinalIgnoreCase);
        private readonly DatUploader _uploader;

        public DatUploaderTests()
        {
            _uploader = new DatUploader(_log.Add);
        }

        public void Dispose()
        {
            _server.Dispose();
            _folder.Dispose();
        }

        private Task<SyncResult> SyncAsync(string path = "/api/players") =>
            _uploader.SyncAsync(_folder.Path, _server.Url(path), FakePlayerDbServer.Credentials, _synced, TestContext.Current.CancellationToken);

        [Theory]
        [InlineData("/api/players")]
        [InlineData("/api/homes")]
        [InlineData("/api/regrades")]
        [InlineData("/api/raids")]
        [InlineData("/api/inventory")]
        public async Task UploadsTheFilesAsTheSiteTakesThem(string path)
        {
            _folder.Write("ab.dat", "{ a = 1, }");
            _folder.Write("ac.dat", "{ a = 2, }");

            var result = await SyncAsync(path);

            Assert.Equal(new SyncResult(2, true, null), result);
            var request = Assert.Single(_server.Requests);
            Assert.Null(request.Rejection);
            Assert.Equal(["{ a = 1, }", "{ a = 2, }"], request.Blobs.Select(Encoding.UTF8.GetString).Order());
        }

        [Fact]
        public async Task SendsOneFileAsAString()
        {
            _folder.Write("ab.dat", "{ a = 1, }");

            await SyncAsync();

            Assert.Equal(System.Text.Json.JsonValueKind.String, Assert.Single(_server.Requests).Field("blobdat").ValueKind);
        }

        [Fact]
        public async Task LeavesOutTheAddonsBookkeepingFiles()
        {
            _folder.Write("ab.dat", "{ a = 1, }");
            _folder.Write("_count.dat", "{ count = 1, }");
            _folder.Write("_files.dat", "{ \"ab.dat\", }");
            _folder.Write("notes.txt", "not a .dat");

            await SyncAsync();

            Assert.Equal(["{ a = 1, }"], Assert.Single(_server.Requests).Blobs.Select(Encoding.UTF8.GetString));
        }

        [Fact]
        public async Task SendsOnlyWhatChangedSinceTheLastUpload()
        {
            _folder.Write("ab.dat", "{ a = 1, }");
            _folder.Write("ac.dat", "{ a = 2, }");
            await SyncAsync();

            Assert.Equal(new SyncResult(0, false, null), await SyncAsync());

            _folder.Write("ac.dat", "{ a = 3, }");
            Assert.Equal(new SyncResult(1, true, null), await SyncAsync());
            Assert.Equal(["{ a = 3, }"], _server.Requests[^1].Blobs.Select(Encoding.UTF8.GetString));
        }

        [Fact]
        public async Task NeverSendsMoreFilesAtOnceThanTheSiteTakes()
        {
            for (int i = 0; i < FakePlayerDbServer.MaxFiles + 1; i++)
            {
                _folder.Write($"{i:D4}.dat", $"{{ a = {i}, }}");
            }

            var result = await SyncAsync();

            Assert.Equal(FakePlayerDbServer.MaxFiles + 1, result.Uploaded);
            Assert.Equal(2, _server.Requests.Count);
            Assert.All(_server.Requests, r => Assert.Null(r.Rejection));
            Assert.Equal(DatUploader.MaxBatchSize, _server.Requests[0].Strings("blobdat").Count);
            Assert.InRange(DatUploader.MaxBatchSize, 1, FakePlayerDbServer.MaxFiles);
        }

        [Fact]
        public async Task ABadFileInABatchIsSplitOffAndTheRestUploaded()
        {
            // As the site does: one file that isn't a Lua table fails the whole batch with 422, saving nothing.
            _server.Respond = r => r.Blobs.Any(b => Encoding.UTF8.GetString(b) == "garbage")
                ? Reply.Text("blobdat[1] is not a Lua table", 422)
                : null;
            _folder.Write("ab.dat", "{ a = 1, }");
            _folder.Write("ac.dat", "garbage");
            _folder.Write("ad.dat", "{ a = 3, }");
            _folder.Write("ae.dat", "{ a = 4, }");

            var result = await SyncAsync();

            Assert.Equal(new SyncResult(3, true, null), result);
            Assert.Equal(["ab.dat", "ad.dat", "ae.dat"], _synced.Keys.Order());
            Assert.Contains(_log, l => l.StartsWith("Server rejected ac.dat: 422"));

            // Not sent again until it changes.
            var requests = _server.Requests.Count;
            Assert.Equal(new SyncResult(0, false, null), await SyncAsync());
            Assert.Equal(requests, _server.Requests.Count);
        }

        [Fact]
        public async Task EveryFileTurnedAwayMeansTheCredentials()
        {
            _server.ExpectedApiKey = "a different key";
            _folder.Write("ab.dat", "{ a = 1, }");
            _folder.Write("ac.dat", "{ a = 2, }");

            var result = await SyncAsync();

            Assert.Equal(0, result.Uploaded);
            Assert.StartsWith("Server refused every upload (418", result.Error);
            Assert.Empty(_synced);

            // Retried once the key is right, rather than remembered as bad files.
            _server.ExpectedApiKey = FakePlayerDbServer.ApiKey;
            Assert.Equal(2, (await SyncAsync()).Uploaded);
        }

        [Fact]
        public async Task AServerErrorStopsTheScan()
        {
            _server.Respond = _ => Reply.Text("Server Error", 500);
            _folder.Write("ab.dat", "{ a = 1, }");
            _folder.Write("ac.dat", "{ a = 2, }");

            var result = await SyncAsync();

            Assert.Equal(0, result.Uploaded);
            Assert.StartsWith("500", result.Error);
            Assert.Single(_server.Requests);
            Assert.Empty(_synced);
        }

        [Fact]
        public async Task AnEndpointTheSiteDoesNotHaveIsAnError()
        {
            _folder.Write("ab.dat", "{ a = 1, }");

            var result = await SyncAsync("/api/player");

            Assert.StartsWith("404", result.Error);
        }

        [Fact]
        public async Task AMissingFolderIsAnError()
        {
            var result = await _uploader.SyncAsync(Path.Combine(_folder.Path, "missing"), _server.Url("/api/players"),
                FakePlayerDbServer.Credentials, _synced, TestContext.Current.CancellationToken);

            Assert.StartsWith("Folder not found", result.Error);
            Assert.Empty(_server.Requests);
        }
    }
}
