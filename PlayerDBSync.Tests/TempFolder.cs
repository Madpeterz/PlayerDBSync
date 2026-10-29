namespace PlayerDBSync.Tests
{
    /// <summary>An empty folder of its own for a test, deleted afterwards.</summary>
    internal sealed class TempFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PlayerDBSync.Tests", Guid.NewGuid().ToString("N"));

        public TempFolder()
        {
            Directory.CreateDirectory(Path);
        }

        public string Write(string name, string text) => Write(name, System.Text.Encoding.UTF8.GetBytes(text));

        /// <summary>Writes the file, <paramref name="age"/> old (some uploaders leave new files alone a moment).</summary>
        public string Write(string name, byte[] bytes, TimeSpan age = default)
        {
            var path = System.IO.Path.Combine(Path, name);
            File.WriteAllBytes(path, bytes);
            if (age != default)
            {
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow - age);
            }
            return path;
        }

        public IReadOnlyList<string> Files() =>
            Directory.EnumerateFiles(Path).Select(System.IO.Path.GetFileName).OfType<string>().Order().ToList();

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Left for the OS to clear out of the temp folder.
            }
        }
    }
}
