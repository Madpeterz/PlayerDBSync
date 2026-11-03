using SkiaSharp;

namespace PlayerDBSync.Tests
{
    /// <summary>
    ///  Runs real child processes, which only a Unix system can be counted on to have (sh), so these are skipped on
    ///  Windows. They are what shows the X11 capture's plumbing works: binary output coming back whole, a program that
    ///  isn't installed, one that hangs.
    /// </summary>
    public sealed class LinuxProcessTests
    {
        private static void RequireUnix()
        {
            if (OperatingSystem.IsWindows())
            {
                throw Xunit.Sdk.SkipException.ForSkip("Needs a Unix shell");
            }
        }

        [Fact]
        public void BinaryOutputComesBackWhole()
        {
            RequireUnix();
            // Every byte value, including the ones text decoding would mangle.
            var result = ProcessRunner.Default.Run("sh", ["-c", "i=0; while [ $i -lt 256 ]; do printf \"\\\\$(printf %03o $i)\"; i=$((i+1)); done"], TimeSpan.FromSeconds(20));

            Assert.NotNull(result);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal(Enumerable.Range(0, 256).Select(i => (byte)i), result.Output);
        }

        [Fact]
        public void ReportsExitCodeAndErrors()
        {
            RequireUnix();
            var result = ProcessRunner.Default.Run("sh", ["-c", "echo oops >&2; exit 3"], TimeSpan.FromSeconds(20));

            Assert.Equal(3, result!.ExitCode);
            Assert.Equal("oops", result.Error.Trim());
        }

        [Fact]
        public void AProgramThatIsNotInstalledIsNull()
        {
            Assert.Null(ProcessRunner.Default.Run("pdbs-no-such-program", [], TimeSpan.FromSeconds(5)));
        }

        [Fact]
        public void AProgramThatHangsIsStopped()
        {
            RequireUnix();
            var started = DateTime.UtcNow;

            Assert.Null(ProcessRunner.Default.Run("sleep", ["30"], TimeSpan.FromMilliseconds(300)));
            Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
        }

        [Fact]
        public void CapturesAWindowThroughRealPrograms()
        {
            RequireUnix();
            using var folder = new TempFolder();
            using var bitmap = new SKBitmap(2560, 1440);
            bitmap.Erase(new SKColor(30, 90, 160));
            using (var png = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100))
            {
                File.WriteAllBytes(Path.Combine(folder.Path, "window.png"), png.ToArray());
            }
            // Stand-ins for xdotool and maim, printing what the real ones would.
            WriteTool(folder, "xdotool", """
                case "$1" in
                  search) echo 4194305 ;;
                  getwindowgeometry) printf 'WINDOW=4194305\nX=0\nY=0\nWIDTH=2560\nHEIGHT=1440\nSCREEN=0\n' ;;
                esac
                """);
            WriteTool(folder, "maim", $"cat '{Path.Combine(folder.Path, "window.png")}'");

            var path = Environment.GetEnvironmentVariable("PATH");
            Environment.SetEnvironmentVariable("PATH", folder.Path + Path.PathSeparator + path);
            try
            {
                var (image, error) = new X11GameCapture(ProcessRunner.Default, () => true, name => name == "DISPLAY" ? ":0" : null).Capture();

                Assert.Null(error);
                using (image)
                {
                    Assert.Equal((DemoOcr.Width, DemoOcr.Height), (image!.Width, image.Height));
                    Assert.Equal(new SKColor(30, 90, 160), image.GetPixel(1000, 700));
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("PATH", path);
            }
        }

        private static void WriteTool(TempFolder folder, string name, string script)
        {
            var file = Path.Combine(folder.Path, name);
            File.WriteAllText(file, "#!/bin/sh\n" + script.ReplaceLineEndings("\n") + "\n");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        [Fact]
        [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
        public void TheSettingsFileIsPrivateToTheUser()
        {
            RequireUnix();
            using var folder = new TempFolder();
            var directory = Path.Combine(folder.Path, "config");
            var file = Path.Combine(directory, "settings.json");

            FileHelper.CreatePrivateDirectory(directory);
            FileHelper.WriteAtomicPrivate(file, "{}");
            // Written over a second time, as every save is.
            FileHelper.WriteAtomicPrivate(file, "{\"a\":1}");

            Assert.Equal("{\"a\":1}", File.ReadAllText(file));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(directory));
        }
    }
}
