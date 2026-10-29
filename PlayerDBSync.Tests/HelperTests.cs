namespace PlayerDBSync.Tests
{
    public sealed class UpdateCheckTests
    {
        [Theory]
        [InlineData("1.2.4", "1.2.3", true)]
        [InlineData("v1.3.0", "1.2.9", true)]
        [InlineData("2.0", "1.9.9", true)]
        [InlineData("1.2.3", "1.2.3-beta", true)]
        [InlineData("1.2.3", "1.2.3", false)]
        [InlineData("1.2", "1.2.0", false)]
        [InlineData("1.2.3-beta", "1.2.3", false)]
        [InlineData("1.2.2", "1.2.3", false)]
        [InlineData("1.2.4", "9e6a016", false)]
        [InlineData("latest", "1.2.3", false)]
        public void IsNewer(string latest, string current, bool newer) => Assert.Equal(newer, UpdateCheck.IsNewer(latest, current));

        [Theory]
        [InlineData("1.2.3", true)]
        [InlineData("v1.2.3", true)]
        [InlineData("1.2.3-beta", true)]
        [InlineData("9e6a016", false)]
        [InlineData("9e6a016-dirty", false)]
        [InlineData("dev", false)]
        public void IsRelease(string version, bool release) => Assert.Equal(release, UpdateCheck.IsRelease(version));
    }

    public sealed class AppSettingsTests
    {
        [Theory]
        [InlineData("https://playerdb.blackatom.win", "https://playerdb.blackatom.win")]
        [InlineData("  https://playerdb.blackatom.win/ ", "https://playerdb.blackatom.win")]
        [InlineData("https://playerdb.blackatom.win/api/players", "https://playerdb.blackatom.win")]
        [InlineData("https://playerdb.blackatom.win/api/player/", "https://playerdb.blackatom.win")]
        [InlineData("https://playerdb.blackatom.win/API/Players", "https://playerdb.blackatom.win")]
        [InlineData("http://localhost:8000/sub", "http://localhost:8000/sub")]
        public void NormalizeBaseUrl(string url, string normalized) => Assert.Equal(normalized, AppSettings.NormalizeBaseUrl(url));
    }

    public sealed class DemoLocationTests
    {
        [Fact]
        public void FileTokenComesBackAsTheSameLocation()
        {
            var location = new DemoLocation(16, 10, 20, 'S', 9, 41, 2, 'W');

            Assert.Equal("16-10-20S_9-41-2W", location.FileToken);
            Assert.Equal(location, DemoLocation.FromFileToken(location.FileToken));
        }

        [Theory]
        [InlineData("16-10-20W_9-41-2S")]
        [InlineData("16-10-20S")]
        [InlineData("a-b-cS_9-41-2W")]
        public void NotAFileToken(string token) => Assert.Null(DemoLocation.FromFileToken(token));

        [Fact]
        public void LocationOfReadsTheSavedFileName()
        {
            Assert.Equal(new DemoLocation(16, 10, 20, 'S', 9, 41, 2, 'W'), DemoOcr.LocationOf(@"C:\x\2026-10-23_13-08-00_16-10-20S_9-41-2W.webp"));
            Assert.Null(DemoOcr.LocationOf("2026-10-23_13-08-00.webp"));
            Assert.Null(DemoOcr.LocationOf("holiday.webp"));
        }
    }

    public sealed class FileHelperTests : IDisposable
    {
        private readonly TempFolder _folder = new();

        public void Dispose() => _folder.Dispose();

        [Fact]
        public void RejectedFilesAreSkippedUntilTheyChange()
        {
            var path = _folder.Write("ab.dat", "{ a = 1, }");
            var rejected = new RejectedFiles();
            rejected.Add("ab.dat", new FileInfo(path).Length, new FileInfo(path).LastWriteTimeUtc, "hash");

            Assert.True(rejected.Contains(new FileInfo(path)));
            Assert.True(rejected.HasContents("AB.DAT", "hash"));
            Assert.False(rejected.HasContents("ab.dat", "other"));

            File.WriteAllText(path, "{ a = 22, }");
            Assert.False(rejected.Contains(new FileInfo(path)));

            rejected.Remove("ab.dat");
            Assert.False(rejected.HasContents("ab.dat", "hash"));
        }

        [Fact]
        public void WriteAtomicReplacesTheFile()
        {
            var path = _folder.Write("ignore.dat", "old");

            FileHelper.WriteAtomic(path, "new");

            Assert.Equal("new", File.ReadAllText(path));
            Assert.Equal(["ignore.dat"], _folder.Files());
        }

        [Fact]
        public void TryDeleteOfAMissingFileIsFine() => Assert.True(FileHelper.TryDelete(Path.Combine(_folder.Path, "missing.webp")));

        [Theory]
        [InlineData(0, "0 files")]
        [InlineData(1, "1 file")]
        [InlineData(2, "2 files")]
        public void WordsCount(int n, string text) => Assert.Equal(text, Words.Count(n, "file"));
    }

    public sealed class AddonVersionTests : IDisposable
    {
        private readonly TempFolder _folder = new();

        public void Dispose() => _folder.Dispose();

        private void Write(string folder, string name, string text, int minutesAgo)
        {
            var path = Path.Combine(_folder.Path, folder, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-minutesAgo));
        }

        [Fact]
        public void TheNewestFileWithAVersionGivesIt()
        {
            Write("Players", "ab.dat", "{ { addon_version = \"2.4.0\", }, }", minutesAgo: 10);
            Write("Houses", "ac.dat", "{ { addon_version = \"2.5.0\", }, }", minutesAgo: 5);
            Write("Houses", "ad.dat", "{ { owner = \"Stumpy\", }, }", minutesAgo: 1);
            Write("Houses", "_count.dat", "{ addon_version = \"9.9.9\", }", minutesAgo: 0);

            Assert.Equal("2.5.0", AddonVersion.Newest(_folder.Path, ["Players", "Houses"]));
            Assert.Equal("2.4.0", AddonVersion.Newest(_folder.Path, ["Players"]));
        }

        [Fact]
        public void NoneWithoutAVersion()
        {
            Write("Players", "ab.dat", "{ }", minutesAgo: 1);

            Assert.Null(AddonVersion.Newest(_folder.Path, ["Players", "Missing"]));
            Assert.Null(AddonVersion.Newest("", ["Players"]));
        }
    }
}
