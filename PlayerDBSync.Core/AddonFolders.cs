namespace PlayerDBSync
{
    /// <summary>The settings as typed in, before they're checked.</summary>
    internal sealed record SyncInput(string AddonFolder, string RaidFolder, string Url, string UserId, string ApiKey);

    /// <summary>The box a problem is in.</summary>
    internal enum InputField { AddonFolder, RaidFolder, Url, UserId, ApiKey }

    internal sealed record InputProblem(string Message, InputField Field);

    /// <summary>Recognising the addons' folders, and checking the settings that name them.</summary>
    internal static class AddonFolders
    {
        // The PlayerDB addon keeps players, houses, regrades (once HomeDB and RegradeDB) and inventory side by side;
        // each is synced if its folder is there.
        public static readonly string[] PlayerDbDataFolders =
            [AppSettings.PlayersFolder, AppSettings.HousesFolder, AppSettings.RegradesFolder, AppSettings.InventoryFolder];

        public static readonly string[] RaidsDbDataFolders = [AppSettings.RaidsFolder];

        /// <summary>True if <paramref name="path"/> has the addon marker file and at least one of <paramref name="dataFolders"/>.</summary>
        public static bool IsAddonFolder(string path, params string[] dataFolders) =>
            File.Exists(Path.Combine(path, AppSettings.AddonMarkerFile)) && dataFolders.Any(d => Directory.Exists(Path.Combine(path, d)));

        /// <summary>The data folder to sync, or "" (skipped) when no addon folder is set or it hasn't that folder yet.</summary>
        public static string DataFolder(string addonFolder, string dataFolder) =>
            addonFolder.Length > 0 && Directory.Exists(Path.Combine(addonFolder, dataFolder)) ? Path.Combine(addonFolder, dataFolder) : "";

        public static string NotAddonMessage(string path, string addonName, string[] dataFolders) =>
            $"This doesn't look like the {addonName} addon folder:\n{path}\n\nIt should contain {AppSettings.AddonMarkerFile} and " +
            (dataFolders.Length == 1 ? $"a {dataFolders[0]} folder." : $"a {string.Join(", ", dataFolders[..^1])} or {dataFolders[^1]} folder.");

        /// <summary>
        ///  The addon folder for a <paramref name="picked"/> one. Picking the data folder itself is an easy slip; the
        ///  addon folder above it is used then.
        /// </summary>
        public static string FromPicked(string picked, string[] dataFolders)
        {
            var parent = Path.GetDirectoryName(picked);
            return !IsAddonFolder(picked, dataFolders)
                && dataFolders.Contains(Path.GetFileName(picked), StringComparer.OrdinalIgnoreCase)
                && parent != null && IsAddonFolder(parent, dataFolders)
                ? parent
                : picked;
        }

        /// <summary>The first thing wrong with the settings, and the box to fix it in; null if there's nothing.</summary>
        public static InputProblem? Check(SyncInput input)
        {
            // Either folder may be left empty to skip that sync, but not both.
            var playersFolder = input.AddonFolder.Trim();
            var raidFolder = input.RaidFolder.Trim();
            if (playersFolder.Length == 0 && raidFolder.Length == 0)
            {
                return new("Select a PlayerDB or RaidsDB addon folder to sync.", InputField.AddonFolder);
            }
            if (playersFolder.Length > 0 && !IsAddonFolder(playersFolder, PlayerDbDataFolders))
            {
                return new(NotAddonMessage(playersFolder, "PlayerDB", PlayerDbDataFolders), InputField.AddonFolder);
            }
            if (raidFolder.Length > 0 && !IsAddonFolder(raidFolder, RaidsDbDataFolders))
            {
                return new(NotAddonMessage(raidFolder, "RaidsDB", RaidsDbDataFolders), InputField.RaidFolder);
            }

            if (!IsWebAddress(AppSettings.NormalizeBaseUrl(input.Url)))
            {
                return new("Enter a valid http(s) server URL.", InputField.Url);
            }

            if (!long.TryParse(input.UserId.Trim(), out var userId) || userId <= 0)
            {
                return new("User ID must be a positive number.", InputField.UserId);
            }

            if (string.IsNullOrEmpty(input.ApiKey))
            {
                return new("Enter your API key.", InputField.ApiKey);
            }
            return null;
        }

        /// <summary>True for an http or https address; the only kind that's ever opened, as the box could hold anything.</summary>
        public static bool IsWebAddress(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }
}
