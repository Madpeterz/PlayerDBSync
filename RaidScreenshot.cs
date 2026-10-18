using System.Diagnostics;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace PlayerDBSync
{
    /// <summary>
    ///  Screenshots of the game requested by the RaidsDB addon. The addon asks by writing
    ///  <see cref="RequestFile"/> into its Raids folder; the picture is saved to <see cref="OutputFolder"/>,
    ///  named after the start time of the raid being recorded (from current.dat) plus "-1", "-2", …
    /// </summary>
    internal static class RaidScreenshot
    {
        public const string RequestFile = "request.sht";
        public const string CurrentRaidFile = "current.dat";
        private const string GameProcessName = "archeage";

        // Lossy WebP: a fraction of the size of PNG for game screenshots, with no visible loss at this quality.
        private const int WebpQuality = 80;

        // A screenshot encoding larger than this is redone at lower quality, a step at a time, until it fits.
        private const long MaxFileBytes = 390 * 1024;
        private const int WebpQualityStep = 5;
        private const int MinWebpQuality = 40;

        // Larger captures are scaled down to fit, which keeps them well inside the server's size limit.
        public const int MaxWidth = 1920;
        public const int MaxHeight = 1080;

        public static readonly string OutputFolder = Path.Combine(Path.GetTempPath(), "PlayerDBSync", "Screenshots");

        // current.dat keeps times as unix seconds in strings: started = "1792264040",
        private static readonly Regex StartedPattern = new(@"\bstarted\s*=\s*""(\d+)""");
        private static readonly Regex RecordingPattern = new(@"\brecording\s*=\s*true\b");

        // request.sht names the raid and which of its screenshots this is: raid_started = "1792312031", id = "1792312031-2",
        private static readonly Regex RequestStartedPattern = new(@"\braid_started\s*=\s*""(\d+)""");
        private static readonly Regex RequestIdPattern = new(@"\bid\s*=\s*""\d+-(\d+)""");
        // ...and why it was taken: reason = "end", (letters and digits only, as it goes into the file name)
        private static readonly Regex RequestReasonPattern = new(@"\breason\s*=\s*""([A-Za-z0-9]{1,20})""");

        /// <summary>True if the addon is asking for a screenshot.</summary>
        public static bool HasRequest(string raidsFolder) => File.Exists(Path.Combine(raidsFolder, RequestFile));

        /// <summary>
        ///  What request.sht says about the screenshot: the raid's start as a file stamp, the screenshot's number and
        ///  the reason (lower case), each null if it doesn't say. Null if the request can't be read yet or isn't fully
        ///  written (no closing brace).
        /// </summary>
        public static (string? RaidStart, int? Number, string? Reason)? ReadRequest(string raidsFolder)
        {
            string text;
            try
            {
                using var fs = new FileStream(Path.Combine(raidsFolder, RequestFile), FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(fs);
                text = reader.ReadToEnd();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
            if (!text.TrimEnd().EndsWith('}'))
            {
                return null;
            }

            var started = RequestStartedPattern.Match(text);
            var id = RequestIdPattern.Match(text);
            string? raidStart = started.Success && long.TryParse(started.Groups[1].Value, out var unix)
                ? FileStamp(DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime)
                : null;
            int? number = id.Success && int.TryParse(id.Groups[1].Value, out var n) && n > 0 ? n : null;
            var reason = RequestReasonPattern.Match(text);
            return (raidStart, number, reason.Success ? reason.Groups[1].Value.ToLowerInvariant() : null);
        }

        /// <summary>
        ///  Removes the request once its screenshot is saved. False if it can't be deleted yet (the game mid-write).
        /// </summary>
        public static bool TryRemoveRequest(string raidsFolder)
        {
            try
            {
                File.Delete(Path.Combine(raidsFolder, RequestFile));
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>
        ///  The raid's start time as the addon names its saved raids ("2026-10-17_19-07-20", UTC),
        ///  or null when current.dat has no raid recording.
        /// </summary>
        public static string? RaidStartName(string addonFolder)
        {
            string text;
            try
            {
                // The game may still have the file open; share access so we don't block it.
                using var fs = new FileStream(Path.Combine(addonFolder, CurrentRaidFile), FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(fs);
                text = reader.ReadToEnd();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }

            var started = StartedPattern.Match(text);
            if (!RecordingPattern.IsMatch(text) || !started.Success || !long.TryParse(started.Groups[1].Value, out var unix))
            {
                return null;
            }
            return FileStamp(DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime);
        }

        /// <summary>Same format as the addon's file names: "2026-10-17_19-07-20".</summary>
        public static string FileStamp(DateTime time) => time.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);

        /// <summary>
        ///  "&lt;baseName&gt;-N-&lt;reason&gt;.webp" in <see cref="OutputFolder"/>, or "&lt;baseName&gt;-N.webp" with no reason.
        ///  N is <paramref name="number"/> when the request gives one (a file already there is the same request again,
        ///  so it's replaced); otherwise the next number with no file, whatever its reason, counting from 1. Uploaded
        ///  images are deleted, so the last number used for the raid is kept in <see cref="LastNumberFile"/>; otherwise
        ///  a new screenshot would take the number of one just uploaded.
        /// </summary>
        public static string NextPath(string baseName, int? number = null, string? reason = null)
        {
            Directory.CreateDirectory(OutputFolder);
            int last = LastNumber(baseName);
            int n = number ?? last + 1;
            while (number == null && NumberTaken(baseName, n))
            {
                n++;
            }
            var path = Path.Combine(OutputFolder, reason == null ? $"{baseName}-{n}.webp" : $"{baseName}-{n}-{reason}.webp");
            try
            {
                File.WriteAllText(Path.Combine(OutputFolder, LastNumberFile), $"{baseName} {Math.Max(n, last)}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Only risks a repeated name, which the server gives the next number free.
            }
            return path;
        }

        /// <summary>True if a screenshot numbered <paramref name="n"/> is waiting, with or without a reason.</summary>
        private static bool NumberTaken(string baseName, int n) =>
            File.Exists(Path.Combine(OutputFolder, $"{baseName}-{n}.webp")) ||
            Directory.EnumerateFiles(OutputFolder, $"{baseName}-{n}-*.webp").Any();

        // "2026-10-17_19-07-20 3": the raid last screenshotted and the number its last screenshot got.
        private const string LastNumberFile = "last.txt";

        /// <summary>The number given to <paramref name="baseName"/>'s last screenshot; 0 if it has none.</summary>
        private static int LastNumber(string baseName)
        {
            try
            {
                var parts = File.ReadAllText(Path.Combine(OutputFolder, LastNumberFile)).Trim().Split(' ');
                return parts.Length == 2 && parts[0] == baseName && int.TryParse(parts[1], out var n) ? n : 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return 0;
            }
        }

        /// <summary>
        ///  Captures the client area of the archeage.exe window. When the game is in front this copies what is on
        ///  screen; when it's behind other windows it asks the window to draw itself, which most games support.
        /// </summary>
        public static (Bitmap? Image, string? Error) CaptureGame()
        {
            var processes = Process.GetProcessesByName(GameProcessName);
            if (processes.Length == 0)
            {
                return (null, "archeage.exe isn't running");
            }

            // Work in physical pixels so the window size is right on scaled (125%, 150%…) displays.
            var oldDpiContext = SetThreadDpiAwarenessContext(DpiAwarenessContextPerMonitorAwareV2);
            try
            {
                var hwnd = processes.Select(p => p.MainWindowHandle).FirstOrDefault(h => h != IntPtr.Zero);
                if (hwnd == IntPtr.Zero)
                {
                    return (null, "archeage.exe has no window yet");
                }
                if (IsIconic(hwnd))
                {
                    return (null, "the game window is minimised");
                }
                if (!GetClientRect(hwnd, out var rect) || rect.Right <= 0 || rect.Bottom <= 0)
                {
                    return (null, "couldn't read the game window size");
                }
                var origin = new NativePoint();
                ClientToScreen(hwnd, ref origin);

                var size = new Size(rect.Right, rect.Bottom);
                var image = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppRgb);
                using (var g = Graphics.FromImage(image))
                {
                    if (GetForegroundWindow() == hwnd)
                    {
                        g.CopyFromScreen(origin.X, origin.Y, 0, 0, size);
                    }
                    else
                    {
                        var hdc = g.GetHdc();
                        bool ok;
                        try
                        {
                            ok = PrintWindow(hwnd, hdc, PwClientOnly | PwRenderFullContent);
                        }
                        finally
                        {
                            g.ReleaseHdc(hdc);
                        }
                        if (!ok)
                        {
                            image.Dispose();
                            return (null, "the game window couldn't be captured");
                        }
                    }
                }
                return (image, null);
            }
            finally
            {
                if (oldDpiContext != IntPtr.Zero)
                {
                    SetThreadDpiAwarenessContext(oldDpiContext);
                }
                foreach (var p in processes)
                {
                    p.Dispose();
                }
            }
        }

        /// <summary>
        ///  Saves <paramref name="image"/> as a lossy WebP, scaled down to fit <see cref="MaxWidth"/> x <see cref="MaxHeight"/>
        ///  if it's larger.
        /// </summary>
        public static void SaveWebp(Bitmap image, string path)
        {
            var bits = image.LockBits(new Rectangle(Point.Empty, image.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            SKData? webp;
            try
            {
                // 32bppRgb is BGRA in memory; GDI leaves the alpha byte undefined, so treat it as opaque.
                var info = new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
                using var pixmap = new SKPixmap(info, bits.Scan0, bits.Stride);
                webp = EncodeWebp(pixmap);
            }
            finally
            {
                image.UnlockBits(bits);
            }
            WriteFile(webp, path);
        }

        /// <summary>
        ///  Scales a saved screenshot larger than <see cref="MaxWidth"/> x <see cref="MaxHeight"/> down to fit,
        ///  replacing the file. True if it was resized; false if it already fits or isn't an image.
        /// </summary>
        public static bool ShrinkIfLarge(string path)
        {
            // Kept, so uploads still go oldest first by when each screenshot was taken.
            var taken = File.GetLastWriteTimeUtc(path);
            SKData? webp;
            // Disposed before the file is replaced, as the codec keeps it open.
            using (var codec = SKCodec.Create(path))
            {
                if (codec == null || Scale(codec.Info.Width, codec.Info.Height) >= 1.0)
                {
                    return false;
                }
                using var bitmap = SKBitmap.Decode(codec);
                if (bitmap == null)
                {
                    return false;
                }
                using var pixmap = bitmap.PeekPixels();
                webp = EncodeWebp(pixmap);
            }
            WriteFile(webp, path);
            File.SetLastWriteTimeUtc(path, taken);
            return true;
        }

        /// <summary>How much an image must be scaled to fit <see cref="MaxWidth"/> x <see cref="MaxHeight"/>; 1 if it already does.</summary>
        private static double Scale(int width, int height) =>
            Math.Min(1.0, Math.Min((double)MaxWidth / width, (double)MaxHeight / height));

        private static SKData? EncodeWebp(SKPixmap pixmap)
        {
            var scale = Scale(pixmap.Width, pixmap.Height);
            if (scale >= 1.0)
            {
                return EncodeWithinSize(pixmap);
            }

            // Keeps the aspect ratio: 2560x1440 becomes 1920x1080, an ultrawide 3440x1440 1920x804.
            var scaledInfo = pixmap.Info.WithSize(Math.Max(1, (int)Math.Round(pixmap.Width * scale)),
                Math.Max(1, (int)Math.Round(pixmap.Height * scale)));
            using var scaled = new SKBitmap(scaledInfo);
            using var scaledPixels = scaled.PeekPixels();
            if (!pixmap.ScalePixels(scaledPixels, new SKSamplingOptions(SKCubicResampler.Mitchell)))
            {
                throw new InvalidOperationException("Resizing the screenshot failed");
            }
            return EncodeWithinSize(scaledPixels);
        }

        /// <summary>
        ///  Encodes at <see cref="WebpQuality"/>, then 5 lower each time the result is over <see cref="MaxFileBytes"/>,
        ///  down to <see cref="MinWebpQuality"/>. Always encodes from the original pixels, so quality isn't lost twice.
        /// </summary>
        private static SKData? EncodeWithinSize(SKPixmap pixmap)
        {
            for (int quality = WebpQuality; ; quality -= WebpQualityStep)
            {
                var webp = pixmap.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, quality));
                if (webp == null || webp.Size <= MaxFileBytes || quality - WebpQualityStep < MinWebpQuality)
                {
                    return webp;
                }
                webp.Dispose();
            }
        }

        /// <summary>Written to a temp file first, so the finished file never appears half-written.</summary>
        private static void WriteFile(SKData? webp, string path)
        {
            using (webp)
            {
                if (webp == null)
                {
                    throw new InvalidOperationException("WebP encoding failed");
                }
                var tmp = path + ".tmp";
                using (var fs = File.Create(tmp))
                {
                    webp.SaveTo(fs);
                }
                File.Move(tmp, path, overwrite: true);
            }
        }

        private const uint PwClientOnly = 0x1;
        private const uint PwRenderFullContent = 0x2;
        private static readonly IntPtr DpiAwarenessContextPerMonitorAwareV2 = -4;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X, Y;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr hWnd, out NativeRect rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ClientToScreen(IntPtr hWnd, ref NativePoint point);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    }
}
