using System.Net;
using System.Text.Json;

namespace PlayerDBSync.Tests
{
    public sealed class ApiClientTests : IDisposable
    {
        private readonly FakePlayerDbServer _server = new();

        public void Dispose() => _server.Dispose();

        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        private static readonly string Blob = Convert.ToBase64String("{ { name = \"Stumpy\", }, }"u8.ToArray());
        private static readonly string OtherBlob = Convert.ToBase64String("{ { name = \"Alicechan\", }, }"u8.ToArray());

        /// <summary>Each endpoint with the fields the app sends it, in the order they are signed.</summary>
        public static TheoryData<string, (string, object)[]> SignedRequests => new()
        {
            { "/api/players", [("blobdat", Blob)] },
            { "/api/players", [("blobdat", new[] { Blob, OtherBlob })] },
            { "/api/homes", [("blobdat", new[] { Blob, OtherBlob })] },
            { "/api/regrades", [("blobdat", Blob)] },
            { "/api/raids", [("blobdat", new[] { Blob, OtherBlob })] },
            { "/api/inventory", [("blobdat", Blob)] },
            { "/api/raids/images", [("names", new[] { "2026-10-17_19-07-20-1-start.webp", "2026-10-17_19-07-20-2.webp" }), ("blobdat", new[] { Blob, OtherBlob })] },
            { "/api/raids/images", [("names", new[] { "2026-10-17_19-07-20-1.webp" }), ("blobdat", new[] { Blob })] },
            { "/api/demoocr", [("sextant", "16° 10' 20\" S, 9° 41' 2\" W"), ("blobdat", Blob)] },
            { "/api/raids/ignored", [] },
        };

        [Theory]
        [MemberData(nameof(SignedRequests))]
        public async Task SignsEachEndpointAsTheSiteChecks(string path, (string, object)[] fields)
        {
            using var response = await ApiClient.PostAsync(_server.Url(path), FakePlayerDbServer.Credentials, fields, Ct);

            var request = Assert.Single(_server.Requests);
            Assert.Null(request.Rejection);
            Assert.True(response.IsSuccessStatusCode, await ApiClient.DescribeAsync(response, Ct));
        }

        [Fact]
        public async Task SendsJsonWithAContentLengthNotChunked()
        {
            using var _ = await ApiClient.PostAsync(_server.Url("/api/players"), FakePlayerDbServer.Credentials, [("blobdat", Blob)], Ct);

            var request = Assert.Single(_server.Requests);
            Assert.StartsWith("application/json", request.ContentType);
            Assert.Contains("charset=utf-8", request.ContentType);
            Assert.False(request.Chunked);
            Assert.True(request.ContentLength > 0);
        }

        [Fact]
        public async Task SendsUserIdAndTimeAsNumbersAndTheSecretAsLowerCaseHex()
        {
            var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            using var _ = await ApiClient.PostAsync(_server.Url("/api/players"), FakePlayerDbServer.Credentials, [("blobdat", Blob)], Ct);

            var request = Assert.Single(_server.Requests);
            Assert.Equal(JsonValueKind.Number, request.Field("userid").ValueKind);
            Assert.Equal(FakePlayerDbServer.UserId, request.Field("userid").GetInt64());
            Assert.Equal(JsonValueKind.Number, request.Field("unixtime").ValueKind);
            Assert.InRange(request.Field("unixtime").GetInt64(), before, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            Assert.Matches("^[0-9a-f]{64}$", request.Field("secret").GetString());
        }

        [Fact]
        public async Task SendsOneFileAsAStringAndABatchAsAnArray()
        {
            using (await ApiClient.PostAsync(_server.Url("/api/players"), FakePlayerDbServer.Credentials, [("blobdat", Blob)], Ct)) { }
            using (await ApiClient.PostAsync(_server.Url("/api/players"), FakePlayerDbServer.Credentials, [("blobdat", new[] { Blob, OtherBlob })], Ct)) { }

            Assert.Equal(JsonValueKind.String, _server.Requests[0].Field("blobdat").ValueKind);
            Assert.Equal(JsonValueKind.Array, _server.Requests[1].Field("blobdat").ValueKind);
            Assert.Equal([Blob, OtherBlob], _server.Requests[1].Strings("blobdat"));
        }

        [Fact]
        public async Task SendsOnlyTheFieldsTheSiteReads()
        {
            using var _ = await ApiClient.PostAsync(_server.Url("/api/demoocr"), FakePlayerDbServer.Credentials,
                [("sextant", "16° 10' 20\" S, 9° 41' 2\" W"), ("blobdat", Blob)], Ct);

            var request = Assert.Single(_server.Requests);
            Assert.Equal(["blobdat", "secret", "sextant", "unixtime", "userid"], request.Body.EnumerateObject().Select(p => p.Name).Order());
            Assert.Equal("16° 10' 20\" S, 9° 41' 2\" W", request.Field("sextant").GetString());
        }

        [Fact]
        public async Task AWrongApiKeyIsTurnedAway()
        {
            using var response = await ApiClient.PostAsync(_server.Url("/api/players"), new ApiCredentials(FakePlayerDbServer.UserId, "wrong"),
                [("blobdat", Blob)], Ct);

            Assert.Equal(418, (int)response.StatusCode);
            Assert.Equal("the secret doesn't match", Assert.Single(_server.Requests).Rejection);
        }

        [Fact]
        public async Task FieldsSignedInAnotherOrderAreTurnedAway()
        {
            // The site signs names before blobdat: the order fields are given in is the order they are signed.
            using var response = await ApiClient.PostAsync(_server.Url("/api/raids/images"), FakePlayerDbServer.Credentials,
                [("blobdat", new[] { Blob }), ("names", new[] { "2026-10-17_19-07-20-1.webp" })], Ct);

            Assert.Equal(418, (int)response.StatusCode);
        }

        [Fact]
        public async Task RejectsAFieldThatIsNotAStringOrStringArray()
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
                ApiClient.PostAsync(_server.Url("/api/players"), FakePlayerDbServer.Credentials, [("blobdat", 5)], Ct));
        }

        [Fact]
        public async Task ReadBodyPutsTheReplyOnOneLineAndCutsItShort()
        {
            using var pretty = new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
            {
                Content = new StringContent("{\n    \"message\": \"blobdat[3] is not a Lua table\"\n}"),
            };
            Assert.Equal("{ \"message\": \"blobdat[3] is not a Lua table\" }", await ApiClient.ReadBodyAsync(pretty, Ct));

            using var huge = new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent(new string('x', 1000)) };
            Assert.Equal(new string('x', 300) + "…", await ApiClient.ReadBodyAsync(huge, Ct));
        }

        [Fact]
        public void DescribeGivesTheStatusReasonAndBody()
        {
            using var response = new HttpResponseMessage(HttpStatusCode.NotFound) { ReasonPhrase = "Not Found" };
            Assert.Equal("404 Not Found nothing here", ApiClient.Describe(response, "nothing here"));
            Assert.Equal("404 Not Found", ApiClient.Describe(response, ""));
        }

        [Fact]
        public void FailureOfOnlyNamesFailuresOfTheRequest()
        {
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();

            Assert.Equal("refused", ApiClient.FailureOf(new HttpRequestException("refused"), CancellationToken.None));
            Assert.Equal("the server's reply wasn't valid JSON", ApiClient.FailureOf(new JsonException(), CancellationToken.None));
            Assert.Equal("Request timed out", ApiClient.FailureOf(new TaskCanceledException(), CancellationToken.None));
            Assert.Null(ApiClient.FailureOf(new TaskCanceledException(), cancelled.Token));
            Assert.Null(ApiClient.FailureOf(new InvalidOperationException(), CancellationToken.None));
        }
    }
}
