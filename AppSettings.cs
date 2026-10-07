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
        public const string DefaultFolder = @"C:\AAClassic\Documents\Addon\PlayerDB\Players";
        public const string DefaultUrl = "https://playerdb.blackatom.win/api/players";

        // Earlier builds shipped this (wrong) default; saved copies of it are upgraded on load.
        private const string OldDefaultUrl = "https://playerdb.blackatom.win/api/player";

        private static readonly string SettingsDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PlayerDBSync");
        private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

        public string Folder { get; set; } = DefaultFolder;
        public string Url { get; set; } = DefaultUrl;
        public string UserId { get; set; } = "";
        public string ProtectedApiKey { get; set; } = "";

        /// <summary>
        ///  What was last uploaded for each file, keyed by file name.
        ///  Only valid for the folder/url/user it was recorded against (see <see cref="SyncTarget"/>).
        /// </summary>
        public Dictionary<string, SyncRecord> SyncedFiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string SyncTarget { get; set; } = "";

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
                        if (settings.UploadedHashes != null)
                        {
                            // Size/time unknown, so the first scan re-hashes these once but won't re-upload them.
                            foreach (var (name, hash) in settings.UploadedHashes)
                            {
                                settings.SyncedFiles.TryAdd(name, new SyncRecord { Hash = hash, Size = -1 });
                            }
                            settings.UploadedHashes = null;
                        }
                        if (string.Equals(settings.Url, OldDefaultUrl, StringComparison.OrdinalIgnoreCase))
                        {
                            settings.Url = DefaultUrl;
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
