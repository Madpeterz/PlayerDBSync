using System.Globalization;
using System.Text.Json.Serialization;
using SkiaSharp;

namespace PlayerDBSync
{
    /// <summary>A rectangle of the desktop, in pixels from its top left corner: one screen, or a box the user placed.</summary>
    internal sealed record CaptureArea(int X, int Y, int Width, int Height)
    {
        [JsonIgnore] public int Right => X + Width;
        [JsonIgnore] public int Bottom => Y + Height;

        /// <summary>Whether all of <paramref name="other"/> is inside this.</summary>
        public bool Contains(CaptureArea other) =>
            other.X >= X && other.Y >= Y && other.Right <= Right && other.Bottom <= Bottom;

        /// <summary>The smallest rectangle holding all of <paramref name="areas"/> (the desktop, for the screens); null if there are none.</summary>
        public static CaptureArea? Around(IEnumerable<CaptureArea> areas)
        {
            CaptureArea? around = null;
            foreach (var area in areas)
            {
                around = around == null
                    ? area
                    : FromEdges(Math.Min(around.X, area.X), Math.Min(around.Y, area.Y), Math.Max(around.Right, area.Right), Math.Max(around.Bottom, area.Bottom));
            }
            return around;
        }

        /// <summary>A <paramref name="width"/> by <paramref name="height"/> box in the middle of <paramref name="screen"/>, made smaller if it doesn't fit.</summary>
        public static CaptureArea CentredIn(CaptureArea screen, int width, int height)
        {
            width = Math.Clamp(width, 1, screen.Width);
            height = Math.Clamp(height, 1, screen.Height);
            return new CaptureArea(screen.X + (screen.Width - width) / 2, screen.Y + (screen.Height - height) / 2, width, height);
        }

        private static CaptureArea FromEdges(int left, int top, int right, int bottom) => new(left, top, right - left, bottom - top);

        /// <summary>As <c>WxH+X+Y</c>, the geometry maim and ImageMagick take.</summary>
        public string Geometry => string.Create(CultureInfo.InvariantCulture, $"{Width}x{Height}+{X}+{Y}");

        public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Width}×{Height} at {X},{Y}");
    }

    /// <summary>
    ///  Takes screenshots on Linux. Finding the game's window turned out to be unreliable (Wine and Proton name and draw
    ///  it in many ways), so the user picks what to capture instead: the whole desktop, or one <see cref="CaptureArea"/>
    ///  (a screen). The picture is made by the first of <c>grim</c> (Wayland), <c>maim</c> and ImageMagick's
    ///  <c>import</c> (X11) that is installed and gives something other than black.
    /// </summary>
    internal sealed class LinuxScreenCapture : IGameCapture
    {
        private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(20);

        /// <summary>The programs that can take a picture and print it as a PNG, with the arguments for an area (null: everything).</summary>
        private static readonly (string Program, bool Wayland, Func<CaptureArea?, string[]> Arguments)[] CaptureTools =
        [
            ("grim", true, area => area == null ? ["-"] : ["-g", string.Create(CultureInfo.InvariantCulture, $"{area.X},{area.Y} {area.Width}x{area.Height}"), "-"]),
            ("maim", false, area => area == null ? ["--hidecursor"] : ["--hidecursor", "--geometry", area.Geometry]),
            ("import", false, area => area == null
                ? ["-silent", "-window", "root", "png:-"]
                : ["-silent", "-window", "root", "-crop", area.Geometry, "+repage", "png:-"]),
        ];

        private readonly IProcessRunner _runner;
        private readonly Func<CaptureArea?> _area;
        private readonly Func<string, string?> _environment;

        /// <param name="area">What to capture, asked for at each capture so a change in the settings applies at once; null is the whole desktop.</param>
        public LinuxScreenCapture(IProcessRunner runner, Func<CaptureArea?> area, Func<string, string?>? environment = null)
        {
            _runner = runner;
            _area = area;
            _environment = environment ?? Environment.GetEnvironmentVariable;
        }

        /// <summary>What the last <see cref="Capture"/> took a picture of; null is the whole desktop.</summary>
        public CaptureArea? LastArea { get; private set; }

        public (SKBitmap? Image, string? Error) Capture()
        {
            var wayland = !string.IsNullOrEmpty(_environment("WAYLAND_DISPLAY"));
            if (!wayland && string.IsNullOrEmpty(_environment("DISPLAY")))
            {
                return (null, "there is no desktop to capture (neither DISPLAY nor WAYLAND_DISPLAY is set); start the app from your desktop session");
            }

            var area = _area();
            LastArea = area;
            string? lastFailure = null;
            // On Wayland grim goes first; maim and import can only see what XWayland shows there, which is often nothing.
            foreach (var (program, waylandOnly, arguments) in CaptureTools.OrderBy(tool => tool.Wayland == wayland ? 0 : 1))
            {
                if (waylandOnly && !wayland)
                {
                    continue;
                }
                var result = _runner.Run(program, arguments(area), ToolTimeout);
                if (result == null)
                {
                    continue;
                }
                if (result.ExitCode != 0 || result.Output.Length == 0)
                {
                    lastFailure = $"{program} failed" + (result.Error.Trim() is { Length: > 0 } why ? $": {why}" : "");
                    continue;
                }
                var image = DecodePng(result.Output);
                if (image == null)
                {
                    lastFailure = $"{program} didn't give a picture";
                    continue;
                }
                if (GameCapture.IsBlack(image))
                {
                    image.Dispose();
                    lastFailure = $"the capture by {program} came out black (is the game in exclusive full screen? Use windowed or borderless; on Wayland install grim)";
                    continue;
                }
                return (image, null);
            }
            return (null, lastFailure ?? "no screenshot tool is installed; install maim or ImageMagick (import), or grim on Wayland");
        }

        /// <summary>A PNG as an opaque picture, the way the Windows capture gives it; null if it isn't one.</summary>
        internal static SKBitmap? DecodePng(byte[] png)
        {
            using var decoded = SKBitmap.Decode(png);
            if (decoded == null)
            {
                return null;
            }
            // Whatever the tool wrote (with or without an alpha channel), as pixels with no transparency.
            var image = new SKBitmap(new SKImageInfo(decoded.Width, decoded.Height, SKImageInfo.PlatformColorType, SKAlphaType.Opaque));
            if (!decoded.PeekPixels().ReadPixels(image.PeekPixels()))
            {
                image.Dispose();
                return null;
            }
            return image;
        }
    }
}
