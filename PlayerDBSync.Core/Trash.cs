using System.Globalization;
using System.Text;

namespace PlayerDBSync
{
    /// <summary>Moves files to the Recycle Bin (Windows) or the desktop's Trash (Linux), so they can be restored.</summary>
    internal static class Trash
    {
        /// <summary>Throws an <see cref="IOException"/> (or <see cref="UnauthorizedAccessException"/>) if the file can't be moved.</summary>
        public static void Move(string file) => Move(file, ProcessRunner.Default);

        internal static void Move(string file, IProcessRunner runner)
        {
            if (OperatingSystem.IsWindows())
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(file,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                return;
            }
            // gio knows every desktop's trash, including one on another disk; without it the user's own is used.
            if (runner.Run("gio", ["trash", "--", file], TimeSpan.FromSeconds(20)) is { ExitCode: 0 })
            {
                return;
            }
            MoveToHomeTrash(file);
        }

        /// <summary>The freedesktop.org Trash spec's home trash: files/ holds the file, info/ says where it came from and when.</summary>
        internal static void MoveToHomeTrash(string file, string? trashRoot = null, DateTime? now = null)
        {
            var full = Path.GetFullPath(file);
            if (!File.Exists(full))
            {
                throw new FileNotFoundException("The file is already gone", full);
            }
            trashRoot ??= Path.Combine(
                Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } data
                    ? data
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"),
                "Trash");
            var files = Path.Combine(trashRoot, "files");
            var info = Path.Combine(trashRoot, "info");
            Directory.CreateDirectory(files);
            Directory.CreateDirectory(info);

            // A name nothing in the trash has, claimed by creating its .trashinfo first (as the spec says).
            var name = Path.GetFileName(full);
            var stem = Path.GetFileNameWithoutExtension(name);
            var extension = Path.GetExtension(name);
            var infoPath = Path.Combine(info, name + ".trashinfo");
            for (int n = 2; ; n++)
            {
                try
                {
                    using var fs = new FileStream(infoPath, FileMode.CreateNew, FileAccess.Write);
                    var text = "[Trash Info]\nPath=" + EscapePath(full) + "\nDeletionDate="
                        + (now ?? DateTime.Now).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) + "\n";
                    fs.Write(Encoding.UTF8.GetBytes(text));
                    break;
                }
                catch (IOException) when (File.Exists(infoPath))
                {
                    name = $"{stem}.{n}{extension}";
                    infoPath = Path.Combine(info, name + ".trashinfo");
                }
            }

            try
            {
                File.Move(full, Path.Combine(files, name));
            }
            catch
            {
                File.Delete(infoPath);
                throw;
            }
        }

        /// <summary>The path as a trashinfo file wants it: percent-encoded, slashes as they are.</summary>
        private static string EscapePath(string path) => string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
    }
}
