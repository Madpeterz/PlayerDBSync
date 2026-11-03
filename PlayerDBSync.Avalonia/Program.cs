using Avalonia;

namespace PlayerDBSync
{
    internal static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (DllNotFoundException ex) when (OperatingSystem.IsLinux())
            {
                // A desktop without one of the libraries the window needs, e.g. a minimal install.
                Console.Error.WriteLine($"PlayerDB Sync couldn't start: {ex.Message.Split('\n')[0]}");
                Console.Error.WriteLine("It needs the X11 client libraries, fontconfig and freetype.");
                Console.Error.WriteLine("On Debian/Ubuntu: sudo apt install libx11-6 libxcursor1 libxrandr2 libxi6 libsm6 libice6 libfontconfig1 libfreetype6");
                return 1;
            }
        }

        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
    }
}
