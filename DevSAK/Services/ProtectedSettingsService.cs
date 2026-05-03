using System;
using System.Security.Cryptography;
using System.Text;

namespace DevSAK.Services
{
    public class ProtectedSettingsService
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DevSAK.MySQL");

        public string Protect(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
            {
                return string.Empty;
            }

            var bytes = Encoding.UTF8.GetBytes(plainText);
            var protectedBytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }

        public string Unprotect(string protectedValue)
        {
            if (string.IsNullOrWhiteSpace(protectedValue))
            {
                return string.Empty;
            }

            try
            {
                var bytes = Convert.FromBase64String(protectedValue);
                var plainBytes = ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
