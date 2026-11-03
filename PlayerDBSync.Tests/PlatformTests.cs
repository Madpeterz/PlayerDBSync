using System.Text;
using SkiaSharp;

namespace PlayerDBSync.Tests
{
    /// <summary>Runs nothing: answers each program from a table, and remembers what it was asked to run.</summary>
    internal sealed class FakeRunner(Func<string, IReadOnlyList<string>, ProcessResult?> answer) : IProcessRunner
    {
        public List<string> Calls { get; } = [];

        public ProcessResult? Run(string program, IReadOnlyList<string> arguments, TimeSpan timeout)
        {
            Calls.Add(program + " " + string.Join(' ', arguments));
            return answer(program, arguments);
        }

        public static ProcessResult Ok(string text) => new(0, Encoding.UTF8.GetBytes(text), "");
        public static ProcessResult Ok(byte[] bytes) => new(0, bytes, "");
    }

    public sealed class X11GameCaptureTests
    {
        private static byte[] Png(SKColor color, int width = 64, int height = 48)
        {
            using var bitmap = new SKBitmap(width, height);
            bitmap.Erase(color);
            using var data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }

        private static string? Env(string name) => name == "DISPLAY" ? ":0" : null;

        [Fact]
        public void ParsesWindowIdsAndGeometry()
        {
            Assert.Equal([44040194L, 12L], X11GameCapture.ParseWindowIds(Encoding.UTF8.GetBytes("44040194\n\n12\nnot-an-id\n")));
            Assert.Equal((2560, 1440), X11GameCapture.ParseGeometry("WINDOW=1\nX=10\nY=20\nWIDTH=2560\nHEIGHT=1440\nSCREEN=0\n"));
            Assert.Null(X11GameCapture.ParseGeometry("WINDOW=1\nX=10\n"));
        }

        [Fact]
        public void CapturesTheBiggestWindowOfTheGameClass()
        {
            var runner = new FakeRunner((program, args) => (program, args[0]) switch
            {
                ("xdotool", "search") => FakeRunner.Ok("100\n200\n"),
                ("xdotool", "getwindowgeometry") => FakeRunner.Ok(args[2] == "200" ? "WIDTH=1920\nHEIGHT=1080\n" : "WIDTH=10\nHEIGHT=10\n"),
                ("maim", _) => FakeRunner.Ok(Png(new SKColor(10, 120, 200))),
                _ => null,
            });

            var (image, error) = new X11GameCapture(runner, () => true, Env).Capture();

            Assert.Null(error);
            using (image)
            {
                Assert.Equal((64, 48), (image!.Width, image.Height));
                Assert.Equal(SKAlphaType.Opaque, image.AlphaType);
            }
            Assert.Contains("xdotool search --onlyvisible --class archeage", runner.Calls);
            Assert.Contains("maim --hidecursor --window 200", runner.Calls);
        }

        [Fact]
        public void FallsBackToImportWhenMaimIsNotInstalled()
        {
            var runner = new FakeRunner((program, args) => program switch
            {
                "xdotool" when args[0] == "search" => FakeRunner.Ok("7\n"),
                "xdotool" => FakeRunner.Ok("WIDTH=64\nHEIGHT=48\n"),
                "import" => FakeRunner.Ok(Png(SKColors.Teal)),
                _ => null,
            });

            var (image, error) = new X11GameCapture(runner, () => true, Env).Capture();

            Assert.Null(error);
            image!.Dispose();
            Assert.Contains("import -silent -window 7 png:-", runner.Calls);
        }

        [Fact]
        public void ABlackPictureIsNotSaved()
        {
            var runner = new FakeRunner((program, args) => program switch
            {
                "xdotool" when args[0] == "search" => FakeRunner.Ok("7\n"),
                "xdotool" => FakeRunner.Ok("WIDTH=64\nHEIGHT=48\n"),
                "maim" => FakeRunner.Ok(Png(SKColors.Black)),
                _ => null,
            });

            var (image, error) = new X11GameCapture(runner, () => true, Env).Capture();

            Assert.Null(image);
            Assert.Contains("came out black", error);
        }

        [Theory]
        [InlineData(true, "minimised")]
        [InlineData(false, "isn't running")]
        public void NoVisibleWindowIsMinimisedOrNotRunning(bool processRunning, string expected)
        {
            // xdotool finds nothing: an exit code of 1 and no output.
            var runner = new FakeRunner((_, _) => new ProcessResult(1, [], ""));

            var (image, error) = new X11GameCapture(runner, () => processRunning, Env).Capture();

            Assert.Null(image);
            Assert.Contains(expected, error);
        }

        [Fact]
        public void SaysWhatIsMissing()
        {
            Assert.Contains("DISPLAY", new X11GameCapture(new FakeRunner((_, _) => null), () => true, _ => null).Capture().Error);
            Assert.Contains("xdotool isn't installed", new X11GameCapture(new FakeRunner((_, _) => null), () => true, Env).Capture().Error);

            var noTools = new FakeRunner((program, args) => program == "xdotool"
                ? FakeRunner.Ok(args[0] == "search" ? "7\n" : "WIDTH=64\nHEIGHT=48\n")
                : null);
            Assert.Contains("install maim", new X11GameCapture(noTools, () => true, Env).Capture().Error);
        }

        [Fact]
        public void TheWindowClassCanBeOverridden()
        {
            var runner = new FakeRunner((_, _) => new ProcessResult(1, [], ""));
            new X11GameCapture(runner, () => false, name => name == "DISPLAY" ? ":0" : name == X11GameCapture.WindowClassVariable ? "wow" : null).Capture();
            Assert.Contains("xdotool search --onlyvisible --class wow", runner.Calls);
        }
    }

    public sealed class GameCaptureTests
    {
        [Fact]
        public void IsBlackLooksAtColourNotAlpha()
        {
            using var black = new SKBitmap(new SKImageInfo(40, 40, SKColorType.Bgra8888, SKAlphaType.Opaque));
            black.Erase(SKColors.Black);
            Assert.True(GameCapture.IsBlack(black));

            using var lit = new SKBitmap(new SKImageInfo(40, 40, SKColorType.Bgra8888, SKAlphaType.Opaque));
            lit.Erase(SKColors.Black);
            lit.SetPixel(8, 16, new SKColor(0, 0, 1));
            Assert.False(GameCapture.IsBlack(lit));
        }

        [Fact]
        public void SavesACaptureAsWebp()
        {
            using var folder = new TempFolder();
            using var image = new SKBitmap(new SKImageInfo(100, 60, SKColorType.Bgra8888, SKAlphaType.Opaque));
            image.Erase(new SKColor(200, 30, 90));
            var path = Path.Combine(folder.Path, "shot.webp");

            RaidScreenshot.SaveWebp(image, path);

            using var codec = SKCodec.Create(path);
            Assert.Equal(SKEncodedImageFormat.Webp, codec.EncodedFormat);
            Assert.Equal((100, 60), (codec.Info.Width, codec.Info.Height));
            Assert.Empty(Directory.GetFiles(folder.Path, "*.tmp"));
        }
    }

    public sealed class SecretProtectorTests
    {
        [Fact]
        public void RoundTrips()
        {
            var stored = SecretProtector.Protect("k3y-é");
            Assert.NotEqual("k3y-é", stored);
            Assert.DoesNotContain("k3y", stored);
            Assert.Equal("k3y-é", SecretProtector.Unprotect(stored));
        }

        [Theory]
        [InlineData("")]
        [InlineData("not base64 at all!")]
        [InlineData("AAAA")]
        public void UnreadableIsEmpty(string stored) => Assert.Equal("", SecretProtector.Unprotect(stored));

        [Fact]
        public void AKeyEncodedOnLinuxIsReadEverywhere() =>
            Assert.Equal("abc", SecretProtector.Unprotect("plain:" + Convert.ToBase64String("abc"u8.ToArray())));
    }

    public sealed class TrashTests
    {
        [Fact]
        public void MovesIntoTheHomeTrashWithAnInfoFile()
        {
            using var folder = new TempFolder();
            var file = folder.Write("a b.dat", "x");
            var trash = Path.Combine(folder.Path, "Trash");

            Trash.MoveToHomeTrash(file, trash, new DateTime(2026, 11, 3, 12, 30, 5));

            Assert.False(File.Exists(file));
            Assert.Equal("x", File.ReadAllText(Path.Combine(trash, "files", "a b.dat")));
            var info = File.ReadAllText(Path.Combine(trash, "info", "a b.dat.trashinfo"));
            Assert.StartsWith("[Trash Info]\n", info);
            Assert.Contains("\nPath=", info);
            Assert.Contains("%20", info);
            Assert.Contains("DeletionDate=2026-11-03T12:30:05\n", info);
        }

        [Fact]
        public void ANameAlreadyInTheTrashGetsANumber()
        {
            using var folder = new TempFolder();
            var trash = Path.Combine(folder.Path, "Trash");
            Trash.MoveToHomeTrash(folder.Write("one.dat", "1"), trash);
            Trash.MoveToHomeTrash(folder.Write("one.dat", "2"), trash);

            Assert.Equal("1", File.ReadAllText(Path.Combine(trash, "files", "one.dat")));
            Assert.Equal("2", File.ReadAllText(Path.Combine(trash, "files", "one.2.dat")));
            Assert.True(File.Exists(Path.Combine(trash, "info", "one.2.dat.trashinfo")));
        }

        [Fact]
        public void AMissingFileIsAnIoError()
        {
            using var folder = new TempFolder();
            Assert.Throws<FileNotFoundException>(() => Trash.MoveToHomeTrash(Path.Combine(folder.Path, "gone.dat"), Path.Combine(folder.Path, "Trash")));
        }
    }

    public sealed class AddonFoldersTests
    {
        [Fact]
        public void PickingADataFolderUsesTheAddonFolderAboveIt()
        {
            using var folder = new TempFolder();
            folder.Write("main.lua", "");
            Directory.CreateDirectory(Path.Combine(folder.Path, "Players"));

            Assert.Equal(folder.Path, AddonFolders.FromPicked(Path.Combine(folder.Path, "Players"), AddonFolders.PlayerDbDataFolders));
            Assert.Equal(folder.Path, AddonFolders.FromPicked(folder.Path, AddonFolders.PlayerDbDataFolders));
        }

        [Fact]
        public void ChecksTheSettingsInOrder()
        {
            using var folder = new TempFolder();
            folder.Write("main.lua", "");
            Directory.CreateDirectory(Path.Combine(folder.Path, "Players"));
            var ok = new SyncInput(folder.Path, "", "https://example.test", "12", "key");

            Assert.Null(AddonFolders.Check(ok));
            Assert.Equal(InputField.AddonFolder, AddonFolders.Check(ok with { AddonFolder = "" })!.Field);
            Assert.Equal(InputField.RaidFolder, AddonFolders.Check(ok with { RaidFolder = folder.Path })!.Field);
            Assert.Equal(InputField.Url, AddonFolders.Check(ok with { Url = "ftp://x" })!.Field);
            Assert.Equal(InputField.UserId, AddonFolders.Check(ok with { UserId = "0" })!.Field);
            Assert.Equal(InputField.ApiKey, AddonFolders.Check(ok with { ApiKey = "" })!.Field);
        }
    }
}
