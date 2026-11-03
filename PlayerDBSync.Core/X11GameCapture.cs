using System.Diagnostics;
using System.Globalization;
using System.Text;
using SkiaSharp;

namespace PlayerDBSync
{
    /// <summary>
    ///  Captures the game window on Linux, where the game runs under Wine or Proton and shows up as an X11 window (a
    ///  native X11 session, or XWayland on a Wayland one). The window is found with <c>xdotool</c> and its picture taken
    ///  by the first of <c>maim</c> and ImageMagick's <c>import</c> that is installed; both ask X11 for the window's own
    ///  contents, so a window another one covers can still be captured when a compositor is running (most desktops).
    ///  Wine names the window's class after the program, so the class is looked for ("archeage"; the
    ///  <see cref="WindowClassVariable"/> environment variable overrides it, e.g. for a game run under another name).
    /// </summary>
    internal sealed class X11GameCapture : IGameCapture
    {
        public const string WindowClassVariable = "PLAYERDBSYNC_WINDOW_CLASS";

        private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(20);

        /// <summary>The programs that can take a window's picture and print it as a PNG, best first.</summary>
        private static readonly (string Program, Func<long, string[]> Arguments)[] CaptureTools =
        [
            ("maim", id => ["--hidecursor", "--window", id.ToString(CultureInfo.InvariantCulture)]),
            ("import", id => ["-silent", "-window", id.ToString(CultureInfo.InvariantCulture), "png:-"]),
        ];

        private readonly IProcessRunner _runner;
        private readonly Func<bool> _gameRunning;
        private readonly Func<string, string?> _environment;

        public X11GameCapture(IProcessRunner runner, Func<bool>? gameRunning = null, Func<string, string?>? environment = null)
        {
            _runner = runner;
            _gameRunning = gameRunning ?? (() => IsGameRunning(runner));
            _environment = environment ?? Environment.GetEnvironmentVariable;
        }

        public (SKBitmap? Image, string? Error) Capture()
        {
            if (string.IsNullOrEmpty(_environment("DISPLAY")))
            {
                return (null, "there is no X11 display (DISPLAY isn't set); the game has to be shown through X11 or XWayland");
            }

            var windows = FindWindows(out var problem);
            if (problem != null)
            {
                return (null, problem);
            }
            if (windows.Count == 0)
            {
                // A minimised window is unmapped, so it's not among the visible ones.
                return (null, _gameRunning()
                    ? "the game window is minimised (or hasn't opened yet)"
                    : "the game isn't running (no game window found)");
            }

            var (image, error) = Grab(Biggest(windows));
            if (image == null)
            {
                return (null, error);
            }
            if (GameCapture.IsBlack(image))
            {
                image.Dispose();
                return (null, "the capture came out black (is the game in exclusive full screen, or the window not drawn by X11? Use windowed or borderless, and an X11 or XWayland window)");
            }
            return (image, null);
        }

        /// <summary>The ids of the visible windows of the game's class, or why they couldn't be looked up.</summary>
        private List<long> FindWindows(out string? problem)
        {
            problem = null;
            var windowClass = _environment(WindowClassVariable) is { Length: > 0 } custom ? custom : GameCapture.GameProcessName;
            var result = _runner.Run("xdotool", ["search", "--onlyvisible", "--class", windowClass], ToolTimeout);
            if (result == null)
            {
                problem = "xdotool isn't installed (or didn't answer); it's needed to find the game window";
                return [];
            }
            // No match is an exit code of 1 with nothing printed; a display that can't be opened says so.
            if (result.ExitCode != 0 && result.Output.Length == 0 && result.Error.Contains("display", StringComparison.OrdinalIgnoreCase))
            {
                problem = "couldn't open the X11 display: " + result.Error.Trim();
                return [];
            }
            return ParseWindowIds(result.Output);
        }

        /// <summary>The window with the most pixels (Wine makes small helper windows of the same class); the first if no size can be read.</summary>
        private long Biggest(List<long> windows)
        {
            (long Id, int Width, int Height) best = (windows[0], 0, 0);
            foreach (var id in windows.Take(32))
            {
                var result = _runner.Run("xdotool", ["getwindowgeometry", "--shell", id.ToString(CultureInfo.InvariantCulture)], ToolTimeout);
                if (result is { ExitCode: 0 } && ParseGeometry(Encoding.UTF8.GetString(result.Output)) is var (width, height)
                    && (long)width * height > (long)best.Width * best.Height)
                {
                    best = (id, width, height);
                }
            }
            return best.Id;
        }

        /// <summary>Takes the window's picture with the first tool that is installed and works.</summary>
        private (SKBitmap? Image, string? Error) Grab(long window)
        {
            string? lastFailure = null;
            foreach (var (program, arguments) in CaptureTools)
            {
                var result = _runner.Run(program, arguments(window), ToolTimeout);
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
                if (image != null)
                {
                    return (image, null);
                }
                lastFailure = $"{program} didn't give a picture";
            }
            return (null, lastFailure ?? "no screenshot tool is installed; install maim or ImageMagick (import)");
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

        /// <summary>The ids xdotool printed, one to a line.</summary>
        internal static List<long> ParseWindowIds(byte[] output) =>
            Encoding.UTF8.GetString(output)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => long.TryParse(line, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0)
                .Where(id => id > 0)
                .ToList();

        /// <summary>The width and height from <c>xdotool getwindowgeometry --shell</c> (WIDTH=1920 HEIGHT=1080…).</summary>
        internal static (int Width, int Height)? ParseGeometry(string shell)
        {
            int? width = null, height = null;
            foreach (var line in shell.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = line.Split('=', 2);
                if (parts.Length == 2 && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                {
                    if (parts[0] == "WIDTH")
                    {
                        width = value;
                    }
                    else if (parts[0] == "HEIGHT")
                    {
                        height = value;
                    }
                }
            }
            return width is { } w && height is { } h ? (w, h) : null;
        }

        /// <summary>
        ///  Under Wine/Proton the process isn't called archeage.exe (the kernel's name for it is cut to 15 characters and is
        ///  often "wine64-preloader" or similar), but the game's name is in its command line, so <c>pgrep -f</c> looks there.
        ///  Falls back to scanning process names when pgrep isn't installed.
        /// </summary>
        private static bool IsGameRunning(IProcessRunner runner)
        {
            var result = runner.Run("pgrep", ["-f", "-i", "--", GameCapture.GameProcessName], TimeSpan.FromSeconds(5));
            return result == null ? IsGameProcessRunning() : result.ExitCode == 0 && result.Output.Length > 0;
        }

        /// <summary>Wine shows the program's name as its process name, with or without ".exe".</summary>
        private static bool IsGameProcessRunning()
        {
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        var name = process.ProcessName;
                        if (name.Equals(GameCapture.GameProcessName, StringComparison.OrdinalIgnoreCase)
                            || name.Equals(GameCapture.GameProcessName + ".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                    {
                        // Gone since the list was made.
                    }
                }
            }
            return false;
        }
    }
}
