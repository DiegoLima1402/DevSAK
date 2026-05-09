using DevSAK.Models;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace DevSAK.Services
{
    public sealed class AppSettingsService
    {
        private static readonly Lazy<AppSettingsService> LazyInstance = new(() => new AppSettingsService());

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private readonly string _settingsFilePath;

        public static AppSettingsService Instance => LazyInstance.Value;

        private AppSettingsService()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var settingsDirectory = Path.Combine(appDataPath, "DevSAK");
            _settingsFilePath = Path.Combine(settingsDirectory, "appsettings.json");
        }

        public async Task<AppSettings> LoadAsync()
        {
            try
            {
                if (!File.Exists(_settingsFilePath))
                {
                    return new AppSettings();
                }

                var json = await File.ReadAllTextAsync(_settingsFilePath).ConfigureAwait(false);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }

        public async Task SaveAsync(AppSettings settings)
        {
            var directory = Path.GetDirectoryName(_settingsFilePath)!;
            Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            await File.WriteAllTextAsync(_settingsFilePath, json).ConfigureAwait(false);
        }
    }
}
