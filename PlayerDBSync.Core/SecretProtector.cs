using System.Security.Cryptography;
using System.Text;

namespace PlayerDBSync
{
    /// <summary>
    ///  Keeps the API key from sitting in settings.json as plain text. On Windows it is encrypted with DPAPI, so only
    ///  the current Windows user can read it. Linux has nothing equivalent every desktop shares, so the key is only
    ///  encoded there ("plain:" and base64) and the settings file is private to the user (mode 600, see
    ///  <see cref="FileHelper.WriteAtomicPrivate"/>).
    /// </summary>
    internal static class SecretProtector
    {
        private const string PlainPrefix = "plain:";

        public static string Protect(string secret)
        {
            var bytes = Encoding.UTF8.GetBytes(secret);
            return OperatingSystem.IsWindows()
                ? Convert.ToBase64String(ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser))
                : PlainPrefix + Convert.ToBase64String(bytes);
        }

        /// <summary>The secret, or "" if it can't be read (empty, damaged, or protected for another user or system).</summary>
        public static string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored))
            {
                return "";
            }
            try
            {
                if (stored.StartsWith(PlainPrefix, StringComparison.Ordinal))
                {
                    return Encoding.UTF8.GetString(Convert.FromBase64String(stored[PlainPrefix.Length..]));
                }
                if (!OperatingSystem.IsWindows())
                {
                    // A DPAPI blob from a settings file copied over from Windows.
                    return "";
                }
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), null, DataProtectionScope.CurrentUser));
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException)
            {
                return "";
            }
        }
    }
}
