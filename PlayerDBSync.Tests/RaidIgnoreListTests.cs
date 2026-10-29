namespace PlayerDBSync.Tests
{
    public sealed class RaidIgnoreListTests : IDisposable
    {
        private readonly FakePlayerDbServer _server = new();
        private readonly TempFolder _folder = new();

        public void Dispose()
        {
            _server.Dispose();
            _folder.Dispose();
        }

        private Task<(IReadOnlyList<string>? Items, string? Error)> DownloadAsync() =>
            RaidIgnoreList.DownloadAsync(_server.Url(RaidIgnoreList.EndpointPath), FakePlayerDbServer.Credentials, TestContext.Current.CancellationToken);

        [Fact]
        public async Task DownloadsTheItemsSignedWithNothingButTheCredentials()
        {
            _server.Respond = _ => Reply.Json(new { items = new[] { "Ash Shard", "Schematic Scrap" } });

            var (items, error) = await DownloadAsync();

            Assert.Null(error);
            Assert.Equal(["Ash Shard", "Schematic Scrap"], items);
            var request = Assert.Single(_server.Requests);
            Assert.Null(request.Rejection);
            Assert.Equal(["secret", "unixtime", "userid"], request.Body.EnumerateObject().Select(p => p.Name).Order());
        }

        [Fact]
        public async Task AnEmptyListIsAList()
        {
            var (items, error) = await DownloadAsync();

            Assert.Null(error);
            Assert.Empty(items!);
        }

        [Fact]
        public async Task LeavesOutNulls()
        {
            _server.Respond = _ => Reply.Json(new { items = new[] { "Ash Shard", null } });

            Assert.Equal(["Ash Shard"], (await DownloadAsync()).Items);
        }

        [Fact]
        public async Task AReplyWithoutItemsIsAnError()
        {
            _server.Respond = _ => Reply.Json(new { names = new[] { "Ash Shard" } });

            Assert.Equal((null, "the server's reply had no items list"), await DownloadAsync());
        }

        [Fact]
        public async Task AReplyThatIsNotJsonIsAnError()
        {
            _server.Respond = _ => new Reply(200, "<html>maintenance</html>", "text/html");

            Assert.Equal("the server's reply wasn't valid JSON", (await DownloadAsync()).Error);
        }

        [Fact]
        public async Task TurnedAwayIsAnError()
        {
            _server.ExpectedApiKey = "a different key";

            var (items, error) = await DownloadAsync();

            Assert.Null(items);
            Assert.StartsWith("418", error);
        }

        [Fact]
        public void SavesTheListAsALuaTableTheAddonReads()
        {
            Assert.True(RaidIgnoreList.Save(_folder.Path, ["Schematic Scrap", "Ash Shard", "Ash Shard", "Say \"hi\"\\\n"]));

            Assert.Equal(
                "{\r\n" +
                "    items = {\r\n" +
                "        [\"Ash Shard\"] = true,\r\n" +
                "        [\"Say \\\"hi\\\"\\\\\\n\"] = true,\r\n" +
                "        [\"Schematic Scrap\"] = true,\r\n" +
                "    },\r\n" +
                "}\r\n",
                File.ReadAllText(Path.Combine(_folder.Path, RaidIgnoreList.FileName)));
        }

        [Fact]
        public void EscapesControlCharactersWithThreeDigits()
        {
            RaidIgnoreList.Save(_folder.Path, ["a\u00011"]);

            Assert.Contains("[\"a\\0011\"] = true,", File.ReadAllText(Path.Combine(_folder.Path, RaidIgnoreList.FileName)));
        }

        [Fact]
        public void LeavesTheFileAloneWhenTheListIsTheSame()
        {
            Assert.True(RaidIgnoreList.Save(_folder.Path, ["Ash Shard", "Schematic Scrap"]));
            Assert.False(RaidIgnoreList.Save(_folder.Path, ["Schematic Scrap", "Ash Shard"]));
            Assert.True(RaidIgnoreList.Save(_folder.Path, ["Ash Shard"]));
        }
    }
}
