using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlayerDBSync
{
    /// <summary>
    ///  User settings persisted to %AppData%\PlayerDBSync\settings.json.
    ///  The API key is encrypted with DPAPI so only the current Windows user can read it.
    /// </summary>
    internal sealed class AppSettings
    {
        public const string DefaultFolder = @"C:\AAClassic\Documents\Addon\PlayerDB";

        // The addon folders are recognised by the marker file and their data folder.
        public const string AddonMarkerFile = "main.lua";
        public const string PlayersFolder = "Players";
        public const string HousesFolder = "Houses";
        public const string RegradesFolder = "Regrades";
        public const string RaidsFolder = "Raids";
        public const string DefaultUrl = "https://playerdb.blackatom.win";

        // Earlier builds stored the full players endpoint (or a wrong "/api/player" default); trimmed back to the base URL.
        private static readonly string[] OldEndpointSuffixes = { "/api/players", "/api/player" };

        private static readonly string SettingsDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PlayerDBSync");
        private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

        /// <summary>
        ///  The PlayerDB addon folder, with its Players, Houses and Regrades (earlier builds stored its Players
        ///  subfolder; upgraded on load). The separate HomeDB and RegradeDB folders of older builds are no longer read.
        /// </summary>
        public string Folder { get; set; } = DefaultFolder;
        public string RaidFolder { get; set; } = "";
        public string Url { get; set; } = DefaultUrl;
        public string UserId { get; set; } = "";
        public string ProtectedApiKey { get; set; } = "";

        /// <summary>Capture the game window when the RaidsDB addon writes Raids\request.sht.</summary>
        public bool RaidScreenshots { get; set; }

        /// <summary>
        ///  What was last uploaded for each file, keyed by file name.
        ///  Only valid for the folder/url/user it was recorded against (see <see cref="SyncTarget"/>).
        /// </summary>
        public Dictionary<string, SyncRecord> SyncedFiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string SyncTarget { get; set; } = "";

        /// <summary>As <see cref="SyncedFiles"/>/<see cref="SyncTarget"/>, for the PlayerDB Houses folder.</summary>
        public Dictionary<string, SyncRecord> HomeSyncedFiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string HomeSyncTarget { get; set; } = "";

        /// <summary>As <see cref="SyncedFiles"/>/<see cref="SyncTarget"/>, for the PlayerDB Regrades folder.</summary>
        public Dictionary<string, SyncRecord> RegradeSyncedFiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string RegradeSyncTarget { get; set; } = "";

        /// <summary>As <see cref="SyncedFiles"/>/<see cref="SyncTarget"/>, for the RaidsDB Raids folder.</summary>
        public Dictionary<string, SyncRecord> RaidSyncedFiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string RaidSyncTarget { get; set; } = "";

        /// <summary>Legacy (hash-only) form of <see cref="SyncedFiles"/>; read for migration, never written.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, string>? UploadedHashes { get; set; }

        public string GetApiKey()
        {
            if (string.IsNullOrEmpty(ProtectedApiKey))
            {
                return "";
            }
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(ProtectedApiKey), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception)
            {
                return "";
            }
        }

        public void SetApiKey(string apiKey)
        {
            ProtectedApiKey = string.IsNullOrEmpty(apiKey)
                ? ""
                : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(apiKey), null, DataProtectionScope.CurrentUser));
        }

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
                    if (settings != null)
                    {
                        settings.SyncedFiles = new(settings.SyncedFiles, StringComparer.OrdinalIgnoreCase);
                        settings.HomeSyncedFiles = new(settings.HomeSyncedFiles, StringComparer.OrdinalIgnoreCase);
                        settings.RegradeSyncedFiles = new(settings.RegradeSyncedFiles, StringComparer.OrdinalIgnoreCase);
                        settings.RaidSyncedFiles = new(settings.RaidSyncedFiles, StringComparer.OrdinalIgnoreCase);
                        if (settings.UploadedHashes != null)
                        {
                            // Size/time unknown, so the first scan re-hashes these once but won't re-upload them.
                            foreach (var (name, hash) in settings.UploadedHashes)
                            {
                                settings.SyncedFiles.TryAdd(name, new SyncRecord { Hash = hash, Size = -1 });
                            }
                            settings.UploadedHashes = null;
                        }
                        settings.Url = NormalizeBaseUrl(settings.Url);
                        var folder = settings.Folder.TrimEnd('\\', '/');
                        if (string.Equals(Path.GetFileName(folder), PlayersFolder, StringComparison.OrdinalIgnoreCase))
                        {
                            settings.Folder = Path.GetDirectoryName(folder) ?? folder;
                        }
                        return settings;
                    }
                }
            }
            catch (Exception)
            {
                // Corrupt or unreadable settings: fall back to defaults.
            }
            return new AppSettings();
        }

        /// <summary>Strips a trailing slash or an old players endpoint path, leaving the server's base URL.</summary>
        public static string NormalizeBaseUrl(string url)
        {
            url = url.Trim().TrimEnd('/');
            foreach (var suffix in OldEndpointSuffixes)
            {
                if (url.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return url[..^suffix.Length].TrimEnd('/');
                }
            }
            return url;
        }

        public void Save()
        {
            Directory.CreateDirectory(SettingsDir);
            var tmp = SettingsPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, SettingsPath, overwrite: true);
        }
    }

    /// <summary>The last successful upload of one file.</summary>
    internal sealed class SyncRecord
    {
        /// <summary>SHA-256 (lowercase hex) of the uploaded contents.</summary>
        public string Hash { get; set; } = "";

        /// <summary>Size and modified time of the file as last seen with this hash; -1 when unknown.</summary>
        public long Size { get; set; } = -1;
        public DateTime LastWriteUtc { get; set; }

        /// <summary>When the upload succeeded; null for records migrated from older versions.</summary>
        public DateTime? SyncedAtUtc { get; set; }
    }
}
