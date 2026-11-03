using System.Reflection;

namespace PlayerDBSync
{
    /// <summary>What the running app is: its version, as the tag a release was built from or the commit a dev build was.</summary>
    internal static class AppInfo
    {
        public const string RepositoryUrl = $"https://github.com/{UpdateCheck.Repository}";

        /// <summary>
        ///  A release's version is the tag it was built from, a dev build's its commit (see AppVersion.props); the
        ///  informational version adds "+&lt;commit&gt;" to it.
        /// </summary>
        public static string Version { get; } =
            (Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev")
            .Split('+')[0];

        /// <summary>"v1.2.3" for a release's version, the commit as it is for a dev build's.</summary>
        public static string Shown(string version) => UpdateCheck.IsRelease(version) ? $"v{version.TrimStart('v', 'V')}" : version;
    }
}
