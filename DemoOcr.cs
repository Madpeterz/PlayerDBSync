using System.Drawing.Imaging;
using System.Globalization;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace PlayerDBSync
{
    /// <summary>A sextant location, as the PlayerDB addon writes it to Demos\location.dat.</summary>
    internal readonly record struct DemoLocation(
        int DegLat, int MinLat, int SecLat, char Latitude,
        int DegLong, int MinLong, int SecLong, char Longitude)
    {
        // The notation the game shows and the server parses: 16° 10' 20" S, 9° 41' 2" W
        public string Sextant => $"{DegLat}° {MinLat}' {SecLat}\" {Latitude}, {DegLong}° {MinLong}' {SecLong}\" {Longitude}";

        // Kept in the file name of a waiting screenshot, so the location doesn't need a file of its own: 16-10-20S_9-41-2W
        public string FileToken => $"{DegLat}-{MinLat}-{SecLat}{Latitude}_{DegLong}-{MinLong}-{SecLong}{Longitude}";

        private static readonly Regex FileTokenPattern = new(@"^(\d+)-(\d+)-(\d+)([NS])_(\d+)-(\d+)-(\d+)([EW])$");

        public static DemoLocation? FromFileToken(string token)
        {
            var m = FileTokenPattern.Match(token);
            if (!m.Success)
            {
                return null;
            }
            int Int(int group) => int.Parse(m.Groups[group].Value, CultureInfo.InvariantCulture);
            return new DemoLocation(Int(1), Int(2), Int(3), m.Groups[4].Value[0], Int(5), Int(6), Int(7), m.Groups[8].Value[0]);
        }
    }

    /// <summary>
    ///  Demolition notice screenshots requested by the PlayerDB addon (its DemoOCR icon). The addon hands over
    ///  through the Demos folder of its addon folder:
    ///  <list type="bullet">
    ///   <item>request.dat <c>{ status = true }</c>: a screenshot is wanted. We set it back to <c>status = false</c> once the
    ///   picture is saved, which the addon shows as done (it gives up after 60 s without that).</item>
    ///   <item>location.dat: the player's sextant location, rewritten every second while a request is open. It is up to date
    ///   when the file was written after the request and in the last few seconds.</item>
    ///  </list>
    ///  Pictures wait in <see cref="OutputFolder"/> until <see cref="DemoOcrUploader"/> has sent them.
    /// </summary>
    internal static class DemoOcr
    {
        public const string RequestFile = "request.dat";
        public const string LocationFile = "location.dat";

        // The server takes only a WebP of exactly this size, up to this many bytes.
        public const int Width = 2560;
        public const int Height = 1440;
        public const long MaxFileBytes = 10 * 1024 * 1024;

        // The addon writes the location every second; older than this and it has stopped.
        private static readonly TimeSpan LocationMaxAge = TimeSpan.FromSeconds(3);

        // Lossless keeps the text sharp for reading; if that is over the limit, lossy from 95 down in steps of 5.
        private const int LosslessEffort = 75;
        private const int LossyStartQuality = 95;
        private const int LossyStep = 5;
        private const int MinLossyQuality = 60;

        // A subfolder of the raid screenshots, which the raid uploader (top folder only) leaves alone.
        public static readonly string OutputFolder = Path.Combine(RaidScreenshot.OutputFolder, "DemoOCR");

        private static readonly Regex StatusPattern = new(@"\bstatus\s*=\s*(true|false)\b");

        // "<stamp>_<location token>.webp", e.g. 2026-10-23_13-08-00_16-10-20S_9-41-2W.webp
        private static readonly Regex FileNamePattern = new(@"^\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}_(.+)\.webp$", RegexOptions.IgnoreCase);

        /// <summary>
        ///  True if request.dat asks for a screenshot, false if it doesn't. Null if it can't be read or isn't fully
        ///  written (no closing brace); the game may be mid-write, so the caller tries again.
        /// </summary>
        public static bool? IsRequested(string demosFolder)
        {
            var text = ReadShared(Path.Combine(demosFolder, RequestFile));
            if (text == null || !text.TrimEnd().EndsWith('}'))
            {
                return null;
            }
            var status = StatusPattern.Match(text);
            return status.Success ? status.Groups[1].Value == "true" : null;
        }

        /// <summary>
        ///  The location in location.dat, or null if it can't be read or is incomplete. <paramref name="upToDate"/> is
        ///  whether it was written after the request and in the last few seconds, so it is where the player is now.
        /// </summary>
        public static DemoLocation? ReadLocation(string demosFolder, out bool upToDate)
        {
            upToDate = false;
            var locationPath = Path.Combine(demosFolder, LocationFile);
            var text = ReadShared(locationPath);
            if (text == null || !text.TrimEnd().EndsWith('}'))
            {
                return null;
            }

            var location = Parse(text);
            if (location == null)
            {
                return null;
            }
            var written = File.GetLastWriteTimeUtc(locationPath);
            upToDate = DateTime.UtcNow - written <= LocationMaxAge
                && written >= File.GetLastWriteTimeUtc(Path.Combine(demosFolder, RequestFile));
            return location;
        }

        private static DemoLocation? Parse(string text)
        {
            // location.dat nests the numbers in a "location" table:  deg_lat = 17, latitude = "S",
            int? Number(string key)
            {
                var m = Regex.Match(text, $@"\b{key}\s*=\s*(\d+)(?:\.\d+)?\s*(?=[,}}])");
                return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : null;
            }
            char? Letter(string key, string allowed)
            {
                var m = Regex.Match(text, $@"\b{key}\s*=\s*""([{allowed}])""");
                return m.Success ? m.Groups[1].Value[0] : null;
            }

            var degLat = Number("deg_lat");
            var minLat = Number("min_lat");
            var secLat = Number("sec_lat");
            var latitude = Letter("latitude", "NS");
            var degLong = Number("deg_long");
            var minLong = Number("min_long");
            var secLong = Number("sec_long");
            var longitude = Letter("longitude", "EW");
            if (degLat == null || minLat == null || secLat == null || latitude == null
                || degLong == null || minLong == null || secLong == null || longitude == null)
            {
                return null;
            }
            return new DemoLocation(degLat.Value, minLat.Value, secLat.Value, latitude.Value,
                degLong.Value, minLong.Value, secLong.Value, longitude.Value);
        }

        /// <summary>
        ///  Answers the request: sets request.dat to <c>status = false</c>. False if it can't be written yet (the game
        ///  has the file open); the caller tries again.
        /// </summary>
        public static bool TryMarkDone(string demosFolder)
        {
            var path = Path.Combine(demosFolder, RequestFile);
            var tmp = path + ".tmp";
            try
            {
                File.WriteAllText(tmp, "{\n    status = false,\n}\n");
                File.Move(tmp, path, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>Where the picture taken at <paramref name="time"/> (UTC) at <paramref name="location"/> waits to be uploaded.</summary>
        public static string PathFor(DateTime time, DemoLocation location)
        {
            Directory.CreateDirectory(OutputFolder);
            return Path.Combine(OutputFolder, $"{RaidScreenshot.FileStamp(time)}_{location.FileToken}.webp");
        }

        /// <summary>The location a waiting picture was taken at, from its file name; null if it isn't one of ours.</summary>
        public static DemoLocation? LocationOf(string path)
        {
            var m = FileNamePattern.Match(Path.GetFileName(path));
            return m.Success ? DemoLocation.FromFileToken(m.Groups[1].Value) : null;
        }

        /// <summary>
        ///  Saves <paramref name="image"/>, which must be <see cref="Width"/> x <see cref="Height"/>, as a WebP of up to
        ///  <see cref="MaxFileBytes"/>. Never scaled: the server reads text off it.
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
                webp = Encode(pixmap);
            }
            finally
            {
                image.UnlockBits(bits);
            }
            if (webp != null && webp.Size > MaxFileBytes)
            {
                webp.Dispose();
                throw new InvalidOperationException($"the screenshot is over {MaxFileBytes / 1024 / 1024} MB even as a lossy WebP");
            }
            RaidScreenshot.WriteFile(webp, path);
        }

        private static SKData? Encode(SKPixmap pixmap)
        {
            var webp = pixmap.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossless, LosslessEffort));
            for (int quality = LossyStartQuality; webp != null && webp.Size > MaxFileBytes && quality >= MinLossyQuality; quality -= LossyStep)
            {
                webp.Dispose();
                webp = pixmap.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, quality));
            }
            return webp;
        }

        /// <summary>The game may have the file open; share access so we don't block it.</summary>
        private static string? ReadShared(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(fs);
                return reader.ReadToEnd();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
