using System.Runtime.CompilerServices;

namespace PlayerDBSync.Tests
{
    /// <summary>
    ///  Runs before anything else in the test process. The app keeps its settings and waiting screenshots in the user's
    ///  own folders; a test that starts the sync engine would otherwise save over a developer's real settings, or upload
    ///  (and so delete) the screenshots they have waiting. Everything goes to a folder of this run's own instead.
    /// </summary>
    internal static class TestSetup
    {
        public static readonly string RunFolder = Path.Combine(Path.GetTempPath(), "PlayerDBSync.Tests", "run-" + Guid.NewGuid().ToString("N"));

        [ModuleInitializer]
        internal static void Isolate()
        {
            Directory.CreateDirectory(RunFolder);
            Environment.SetEnvironmentVariable(AppSettings.SettingsFolderVariable, Path.Combine(RunFolder, "settings"));
            // Path.GetTempPath() reads these, whichever system it is.
            foreach (var name in new[] { "TMP", "TEMP", "TMPDIR" })
            {
                Environment.SetEnvironmentVariable(name, RunFolder);
            }
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try
                {
                    Directory.Delete(RunFolder, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Left for the OS to clear out of the temp folder.
                }
            };
        }
    }
}
