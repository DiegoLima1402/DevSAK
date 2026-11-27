using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using DevSAK.Models;

namespace DevSAK.Services
{
    /// <summary>
    /// Service for managing AppZip settings persistence.
    /// Stores settings as JSON in AppData\DevSAK\AppZipSettings.json
    /// </summary>
    public class AppZipSettingsService
    {
        private static readonly Lazy<AppZipSettingsService> _instance = 
            new(() => new AppZipSettingsService());

        /// <summary>
        /// Gets the singleton instance of the settings service.
        /// </summary>
        public static AppZipSettingsService Instance => _instance.Value;

        private readonly string _settingsDirectory;
        private readonly string _settingsFilePath;
        private AppZipSettings? _cachedSettings;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private AppZipSettingsService() : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DevSAK"))
        {
        }

        public AppZipSettingsService(string settingsDirectory)
        {
            // Create AppData\DevSAK directory structure
            // Store as %AppData%\DevSAK\AppZipSettings.json (as per requirements)
            _settingsDirectory = settingsDirectory;
            _settingsFilePath = Path.Combine(_settingsDirectory, "AppZipSettings.json");

            // Ensure directory exists
            Directory.CreateDirectory(_settingsDirectory);
        }

        /// <summary>
        /// Gets the settings directory path.
        /// </summary>
        public string SettingsDirectory => _settingsDirectory;

        /// <summary>
        /// Gets the settings file path.
        /// </summary>
        public string SettingsFilePath => _settingsFilePath;

        /// <summary>
        /// Loads AppZip settings from disk. Returns default settings if file doesn't exist.
        /// </summary>
        public async Task<AppZipSettings> LoadSettingsAsync()
        {
            try
            {
                // Return cached settings if available
                if (_cachedSettings != null)
                    return _cachedSettings;

                // If file doesn't exist, return default settings
                if (!File.Exists(_settingsFilePath))
                {
                    _cachedSettings = new AppZipSettings();
                    _cachedSettings.EnsureIgnoreProfiles();
                    return _cachedSettings;
                }

                // Read and deserialize settings
                var json = await File.ReadAllTextAsync(_settingsFilePath);
                _cachedSettings = JsonSerializer.Deserialize<AppZipSettings>(json, JsonOptions) 
                    ?? new AppZipSettings();

                if (_cachedSettings.EnsureIgnoreProfiles())
                    await SaveSettingsAsync(_cachedSettings);
                return _cachedSettings;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading AppZip settings: {ex}");
                // Return default settings on error
                _cachedSettings ??= new AppZipSettings();
                _cachedSettings.EnsureIgnoreProfiles();
                return _cachedSettings;
            }
        }

        /// <summary>
        /// Loads AppZip settings from disk synchronously. Returns default settings if file doesn't exist.
        /// </summary>
        public AppZipSettings LoadSettings()
        {
            try
            {
                // Return cached settings if available
                if (_cachedSettings != null)
                    return _cachedSettings;

                // If file doesn't exist, return default settings
                if (!File.Exists(_settingsFilePath))
                {
                    _cachedSettings = new AppZipSettings();
                    _cachedSettings.EnsureIgnoreProfiles();
                    return _cachedSettings;
                }

                // Read and deserialize settings
                var json = File.ReadAllText(_settingsFilePath);
                _cachedSettings = JsonSerializer.Deserialize<AppZipSettings>(json, JsonOptions) 
                    ?? new AppZipSettings();

                if (_cachedSettings.EnsureIgnoreProfiles())
                    SaveSettings(_cachedSettings);
                return _cachedSettings;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading AppZip settings: {ex}");
                // Return default settings on error
                _cachedSettings ??= new AppZipSettings();
                _cachedSettings.EnsureIgnoreProfiles();
                return _cachedSettings;
            }
        }

        /// <summary>
        /// Saves AppZip settings to disk.
        /// </summary>
        public async Task SaveSettingsAsync(AppZipSettings settings)
        {
            try
            {
                if (settings == null)
                    throw new ArgumentNullException(nameof(settings));

                // Update cache
                _cachedSettings = settings;

                // Serialize and write to file
                var json = JsonSerializer.Serialize(settings, JsonOptions);
                var temporaryPath = _settingsFilePath + ".tmp";
                await File.WriteAllTextAsync(temporaryPath, json);
                File.Move(temporaryPath, _settingsFilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving AppZip settings: {ex}");
                throw;
            }
        }

        /// <summary>
        /// Saves AppZip settings to disk synchronously.
        /// </summary>
        public void SaveSettings(AppZipSettings settings)
        {
            try
            {
                if (settings == null)
                    throw new ArgumentNullException(nameof(settings));

                // Update cache
                _cachedSettings = settings;

                // Serialize and write to file
                var json = JsonSerializer.Serialize(settings, JsonOptions);
                var temporaryPath = _settingsFilePath + ".tmp";
                File.WriteAllText(temporaryPath, json);
                File.Move(temporaryPath, _settingsFilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving AppZip settings: {ex}");
                throw;
            }
        }

        /// <summary>
        /// Resets the cached settings.
        /// </summary>
        public void ClearCache()
        {
            _cachedSettings = null;
        }

        /// <summary>
        /// Deletes the settings file from disk.
        /// </summary>
        public void DeleteSettings()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                    File.Delete(_settingsFilePath);

                _cachedSettings = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error deleting AppZip settings: {ex}");
                throw;
            }
        }
    }
}
