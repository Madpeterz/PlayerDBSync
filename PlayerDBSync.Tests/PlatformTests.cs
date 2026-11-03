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

    public sealed class LinuxScreenCaptureTests
    {
        private static byte[] Png(SKColor color, int width = 64, int height = 48)
        {
            using var bitmap = new SKBitmap(width, height);
            bitmap.Erase(color);
            using var data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }

        private static string? X11(string name) => name == "DISPLAY" ? ":0" : null;
        private static string? Wayland(string name) => name is "DISPLAY" or "WAYLAND_DISPLAY" ? "x" : null;

        private static LinuxScreenCapture Capturer(FakeRunner runner, CaptureArea? area, Func<string, string?> environment) =>
            new(runner, () => area, environment);

        [Fact]
        public void CapturesTheWholeDesktopWithMaim()
        {
            var runner = new FakeRunner((program, _) => program == "maim" ? FakeRunner.Ok(Png(new SKColor(10, 120, 200))) : null);

            var (image, error) = Capturer(runner, null, X11).Capture();

            Assert.Null(error);
            using (image)
            {
                Assert.Equal((64, 48), (image!.Width, image.Height));
                Assert.Equal(SKAlphaType.Opaque, image.AlphaType);
            }
            Assert.Equal(["maim --hidecursor"], runner.Calls);
        }

        [Fact]
        public void CapturesJustThePickedScreen()
        {
            var area = new CaptureArea(1920, 0, 2560, 1440);
            var maim = new FakeRunner((program, _) => program == "maim" ? FakeRunner.Ok(Png(SKColors.Teal)) : null);
            Capturer(maim, area, X11).Capture().Image!.Dispose();
            Assert.Equal(["maim --hidecursor --geometry 2560x1440+1920+0"], maim.Calls);

            var import = new FakeRunner((program, _) => program == "import" ? FakeRunner.Ok(Png(SKColors.Teal)) : null);
            Capturer(import, area, X11).Capture().Image!.Dispose();
            Assert.Contains("import -silent -window root -crop 2560x1440+1920+0 +repage png:-", import.Calls);
        }

        [Fact]
        public void WaylandTriesGrimFirstAndX11NeverDoes()
        {
            var area = new CaptureArea(10, 20, 800, 600);
            var wayland = new FakeRunner((program, _) => program == "grim" ? FakeRunner.Ok(Png(SKColors.Teal)) : null);
            Capturer(wayland, area, Wayland).Capture().Image!.Dispose();
            Assert.Equal(["grim -g 10,20 800x600 -"], wayland.Calls);

            var x11 = new FakeRunner((_, _) => null);
            Capturer(x11, area, X11).Capture();
            Assert.DoesNotContain(x11.Calls, call => call.StartsWith("grim"));
        }

        [Fact]
        public void AToolThatGivesBlackIsFollowedByTheNext()
        {
            var runner = new FakeRunner((program, _) => program switch
            {
                "grim" => FakeRunner.Ok(Png(SKColors.Black)),
                "maim" => FakeRunner.Ok(Png(SKColors.Teal)),
                _ => null,
            });

            var (image, error) = Capturer(runner, null, Wayland).Capture();

            Assert.Null(error);
            image!.Dispose();
        }

        [Fact]
        public void ABlackPictureIsNotSaved()
        {
            var runner = new FakeRunner((program, _) => program == "maim" ? FakeRunner.Ok(Png(SKColors.Black)) : null);

            var (image, error) = Capturer(runner, null, X11).Capture();

            Assert.Null(image);
            Assert.Contains("came out black", error);
        }

        [Fact]
        public void SaysWhatIsMissing()
        {
            Assert.Contains("DISPLAY", Capturer(new FakeRunner((_, _) => null), null, _ => null).Capture().Error);
            Assert.Contains("install maim", Capturer(new FakeRunner((_, _) => null), null, X11).Capture().Error);

            var failing = new FakeRunner((program, _) => program == "maim" ? new ProcessResult(1, [], "cannot open display") : null);
            Assert.Contains("maim failed: cannot open display", Capturer(failing, null, X11).Capture().Error);
        }

        [Fact]
        public void TheAreaIsReadAtEachCapture()
        {
            var runner = new FakeRunner((_, _) => null);
            CaptureArea? area = null;
            var capturer = new LinuxScreenCapture(runner, () => area, X11);

            capturer.Capture();
            area = new CaptureArea(0, 0, 100, 100);
            capturer.Capture();

            Assert.Contains("maim --hidecursor --geometry 100x100+0+0", runner.Calls);
        }

        [Fact]
        public void TheAreaSurvivesTheSettingsFile()
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new AppSettings { CaptureArea = new CaptureArea(1, 2, 3, 4) });
            Assert.Equal(new CaptureArea(1, 2, 3, 4), System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!.CaptureArea);
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
