using System.Text.RegularExpressions;

namespace PlayerDBSync
{
    /// <summary>
    ///  The addon version written into an addon's data files (addon_version = "2.5.0",), shown under its folder.
    /// </summary>
    internal static class AddonVersion
    {
        private static readonly Regex Pattern = new(@"\baddon_version\s*=\s*""([0-9A-Za-z.\-]{1,32})""");

        // Files written by an older version of the addon may lack it; look a little further back before giving up.
        private const int FilesToTry = 5;

        /// <summary>
        ///  The version in the most recently written *.dat file of <paramref name="dataFolders"/> under
        ///  <paramref name="addonFolder"/> that has one; null if none does or the folder can't be read.
        /// </summary>
        public static string? Newest(string addonFolder, IEnumerable<string> dataFolders)
        {
            if (addonFolder.Length == 0)
            {
                return null;
            }
            try
            {
                var files = dataFolders
                    .Select(d => new DirectoryInfo(Path.Combine(addonFolder, d)))
                    .Where(d => d.Exists)
                    .SelectMany(d => d.EnumerateFiles("*.dat", SearchOption.TopDirectoryOnly))
                    .Where(f => !DatUploader.IsIgnored(f.Name))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Take(FilesToTry);
                foreach (var file in files)
                {
                    var version = FromFile(file.FullName);
                    if (version != null)
                    {
                        return version;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // A half-typed or unreadable path: nothing to show.
            }
            return null;
        }

        private static string? FromFile(string path)
        {
            try
            {
                // The game may still have the file open; share access so we don't block it.
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(fs);
                var match = Pattern.Match(reader.ReadToEnd());
                return match.Success ? match.Groups[1].Value : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
