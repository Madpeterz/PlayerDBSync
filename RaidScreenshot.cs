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

        // Larger captures are scaled down to fit, which keeps them well inside the server's size limit.
        public const int MaxWidth = 1920;
        public const int MaxHeight = 1080;

        public static readonly string OutputFolder = Path.Combine(Path.GetTempPath(), "PlayerDBSync", "Screenshots");

        // current.dat keeps times as unix seconds in strings: started = "1792264040",
        private static readonly Regex StartedPattern = new(@"\bstarted\s*=\s*""(\d+)""");
        private static readonly Regex RecordingPattern = new(@"\brecording\s*=\s*true\b");

        /// <summary>True if the addon is asking for a screenshot.</summary>
        public static bool HasRequest(string raidsFolder) => File.Exists(Path.Combine(raidsFolder, RequestFile));

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
        ///  The first unused "&lt;baseName&gt;-N.webp" in <see cref="OutputFolder"/>, counting from 1. Uploaded images
        ///  are deleted, so a number can come round again; the server gives a clash the next number free.
        /// </summary>
        public static string NextPath(string baseName)
        {
            Directory.CreateDirectory(OutputFolder);
            for (int n = 1; ; n++)
            {
                var path = Path.Combine(OutputFolder, $"{baseName}-{n}.webp");
                if (!File.Exists(path))
                {
                    return path;
                }
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
            var options = new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, WebpQuality);
            var scale = Scale(pixmap.Width, pixmap.Height);
            if (scale >= 1.0)
            {
                return pixmap.Encode(options);
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
            return scaledPixels.Encode(options);
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
