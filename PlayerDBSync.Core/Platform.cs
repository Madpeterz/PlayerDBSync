using System.Diagnostics;

namespace PlayerDBSync
{
    /// <summary>The few things that differ between the systems the app runs on.</summary>
    internal static class Platform
    {
        /// <summary>
        ///  Where the PlayerDB addon usually is, to start looking from. Linux has no one place (the game runs in a Wine
        ///  prefix of the user's choosing), so nothing is assumed there.
        /// </summary>
        public static string DefaultAddonFolder => OperatingSystem.IsWindows() ? @"C:\AAClassic\Documents\Addon\PlayerDB" : "";

        /// <summary>Where a folder picker should start when nothing has been chosen yet.</summary>
        public static string FolderPickerStart()
        {
            if (OperatingSystem.IsWindows())
            {
                return DefaultAddonFolder;
            }
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var wine = Path.Combine(home, ".wine", "drive_c");
            return Directory.Exists(wine) ? wine : home;
        }

        /// <summary>The game's window capturer for this system.</summary>
        public static IGameCapture CreateGameCapture() =>
            OperatingSystem.IsWindows() ? new WindowsGameCapture() : new X11GameCapture(ProcessRunner.Default);

        /// <summary>
        ///  Opens a web address or a folder with whatever the system uses for it (Explorer, the browser, xdg-open).
        ///  Throws if nothing could be started.
        /// </summary>
        public static void Open(string target)
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
                return;
            }
            var start = new ProcessStartInfo("xdg-open") { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add(target);
            Process.Start(start)?.Dispose();
        }

        /// <summary>
        ///  <paramref name="path"/> with the user's home folder name shown as %user% (C:\Users\%user%\AppData\… or
        ///  /home/%user%/.config/…), so it can be shown, e.g. in a screenshot, without giving away the user name.
        /// </summary>
        public static string WithoutUserName(string path)
        {
            // The profile folder's name, which isn't always the user name (e.g. an account renamed after it was made).
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\', '/');
            var users = Path.GetDirectoryName(profile);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (profile.Length == 0 || users == null
                || !(path.Equals(profile, comparison) || path.StartsWith(profile + Path.DirectorySeparatorChar, comparison)))
            {
                return path;
            }
            return Path.Combine(users, "%user%") + path[profile.Length..];
        }
    }
}
