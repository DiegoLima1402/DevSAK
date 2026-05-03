using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using DevSAK.Models;

namespace DevSAK.Services
{
    public sealed class SmtpTestSettingsStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        private readonly ProtectedSettingsService _protectedSettingsService = new();
        private readonly string _settingsFilePath;

        public SmtpTestSettingsStore()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _settingsFilePath = Path.Combine(appDataPath, "DevSAK", "SmtpTestSettings.json");
        }

        public async Task<SmtpTestSettings> LoadAsync()
        {
            if (!File.Exists(_settingsFilePath))
            {
                return new SmtpTestSettings();
            }

            try
            {
                var json = await File.ReadAllTextAsync(_settingsFilePath);
                var settings = JsonSerializer.Deserialize<SmtpTestSettings>(json) ?? new SmtpTestSettings();
                settings.Password = _protectedSettingsService.Unprotect(settings.EncryptedPassword);
                return settings;
            }
            catch
            {
                return new SmtpTestSettings();
            }
        }

        public async Task SaveAsync(SmtpTestSettings settings)
        {
            var directory = Path.GetDirectoryName(_settingsFilePath)!;
            Directory.CreateDirectory(directory);

            var snapshot = new SmtpTestSettings
            {
                SmtpServer = settings.SmtpServer,
                Port = settings.Port,
                UserName = settings.UserName,
                EncryptedPassword = _protectedSettingsService.Protect(settings.Password),
                SenderEmail = settings.SenderEmail,
                RecipientEmail = settings.RecipientEmail,
                Subject = settings.Subject,
                Message = settings.Message,
                UseSsl = settings.UseSsl,
                UseStartTls = settings.UseStartTls,
                AuthenticationEnabled = settings.AuthenticationEnabled,
                UseUserCredentials = settings.UseUserCredentials
            };

            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            await File.WriteAllTextAsync(_settingsFilePath, json);
        }
    }
}
