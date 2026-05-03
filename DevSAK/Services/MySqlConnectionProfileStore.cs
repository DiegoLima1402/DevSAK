using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DevSAK.Models;

namespace DevSAK.Services
{
    public class MySqlConnectionProfileStore
    {
        private readonly ProtectedSettingsService _protectedSettingsService;
        private readonly string _filePath;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public MySqlConnectionProfileStore(ProtectedSettingsService protectedSettingsService)
        {
            _protectedSettingsService = protectedSettingsService;

            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var directory = Path.Combine(appDataPath, "DevSAK");
            Directory.CreateDirectory(directory);
            _filePath = Path.Combine(directory, "MySqlConnections.json");
        }

        public async Task<IReadOnlyList<MySqlConnectionProfile>> LoadAsync()
        {
            if (!File.Exists(_filePath))
            {
                return Array.Empty<MySqlConnectionProfile>();
            }

            try
            {
                var json = await File.ReadAllTextAsync(_filePath).ConfigureAwait(false);
                var profiles = JsonSerializer.Deserialize<List<MySqlConnectionProfile>>(json, JsonOptions) ?? new List<MySqlConnectionProfile>();

                foreach (var profile in profiles)
                {
                    profile.Password = _protectedSettingsService.Unprotect(profile.EncryptedPassword);
                }

                return profiles;
            }
            catch
            {
                return Array.Empty<MySqlConnectionProfile>();
            }
        }

        public async Task SaveAsync(IEnumerable<MySqlConnectionProfile> profiles)
        {
            var serializedProfiles = profiles
                .Select(profile =>
                {
                    var clone = profile.Clone();
                    clone.EncryptedPassword = _protectedSettingsService.Protect(profile.Password);
                    clone.Password = string.Empty;
                    return clone;
                })
                .ToList();

            var json = JsonSerializer.Serialize(serializedProfiles, JsonOptions);
            await File.WriteAllTextAsync(_filePath, json).ConfigureAwait(false);
        }
    }
}
