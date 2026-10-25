namespace PlayerDBSync
{
    internal static class FileHelper
    {
        /// <summary>Opens a file for reading. The game may still have it open; share access so we don't block it.</summary>
        public static FileStream OpenShared(string path) =>
            new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        /// <summary>The file's text (see <see cref="OpenShared"/>), or null if it can't be read.</summary>
        public static string? ReadShared(string path)
        {
            try
            {
                using var fs = OpenShared(path);
                using var reader = new StreamReader(fs);
                return reader.ReadToEnd();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>Written to a temp file first, then renamed, so the game (or we) never read a half-written file.</summary>
        public static void WriteAtomic(string path, string text) =>
            WriteAtomic(path, tmp => File.WriteAllText(tmp, text));

        /// <summary>As <see cref="WriteAtomic(string, string)"/>, with <paramref name="write"/> writing the temp file it is given.</summary>
        public static void WriteAtomic(string path, Action<string> write)
        {
            var tmp = path + ".tmp";
            write(tmp);
            File.Move(tmp, path, overwrite: true);
        }

        /// <summary>False if the file can't be deleted yet (most likely the game has it open).</summary>
        public static bool TryDelete(string path)
        {
            try
            {
                File.Delete(path);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    /// <summary>
    ///  Files the server turned away, with the size and modified time they were read at (and their hash, if known);
    ///  skipped until the file changes. Deliberately not persisted, so a restart retries them.
    /// </summary>
    internal sealed class RejectedFiles
    {
        private readonly Dictionary<string, (string? Hash, long Size, DateTime LastWriteUtc)> _files = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>True if <paramref name="file"/> was turned away and hasn't changed since.</summary>
        public bool Contains(FileInfo file) =>
            _files.TryGetValue(file.Name, out var rej) && rej.Size == file.Length && rej.LastWriteUtc == file.LastWriteTimeUtc;

        /// <summary>True if the contents turned away for <paramref name="name"/> had <paramref name="hash"/>.</summary>
        public bool HasContents(string name, string hash) => _files.TryGetValue(name, out var rej) && rej.Hash == hash;

        public void Add(string name, long size, DateTime lastWriteUtc, string? hash = null) => _files[name] = (hash, size, lastWriteUtc);

        public void Add(FileInfo file) => Add(file.Name, file.Length, file.LastWriteTimeUtc);

        public void Remove(string name) => _files.Remove(name);

        public void Clear() => _files.Clear();
    }

    internal static class Words
    {
        /// <summary>"1 file", "3 files".</summary>
        public static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";
    }
}
