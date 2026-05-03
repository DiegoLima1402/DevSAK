using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using DevSAK.Models;
using DevSAK.Services;

namespace DevSAK.ViewModels
{
    public enum AppZipOverwriteChoice
    {
        Overwrite,
        GenerateRandom,
        Cancel
    }

    /// <summary>
    /// ViewModel for the AppZip page, handling UI logic and settings management.
    /// </summary>
    public class AppZipViewModel : INotifyPropertyChanged
    {
        private readonly AppZipSettingsService _settingsService;
        private readonly AppZipCompressionService _compressionService;
        private AppZipSettings _settings;
        private CancellationTokenSource? _cancellationTokenSource;

        private string _sourceDirectory = string.Empty;
        private string _destinationDirectory = string.Empty;
        private string _zipName = string.Empty;
        private bool _useAutomaticZipName = true;
        private DateTime? _startDate;
        private string _ignoredFiles = string.Empty;
        private string _ignoredFolders = string.Empty;
        private string _ignoredExtensions = string.Empty;
        private bool _isLoading = false;
        private bool _isProcessing = false;
        private double _progress = 0.0;
        private string _status = "Pronto para iniciar";
        private string _currentZipEntry = string.Empty;
        private bool _useDateFilter = false;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Event raised when compression completes successfully.
        /// The string parameter contains the full path to the created ZIP file.
        /// </summary>
        public event EventHandler<string>? CompressionCompleted;
        
        public Func<string, Task<AppZipOverwriteChoice>>? ConfirmOverwriteRequested { get; set; }

        public AppZipViewModel()
        {
            _settingsService = AppZipSettingsService.Instance;
            _compressionService = new AppZipCompressionService();
            _settings = new AppZipSettings();

            RecentSourceDirectories = new ObservableCollection<string>();
            RecentDestinationDirectories = new ObservableCollection<string>();
            IgnoredFilesList = new ObservableCollection<string>();
            IgnoredFoldersList = new ObservableCollection<string>();
            IgnoredExtensionsList = new ObservableCollection<string>();

            // If automatic mode is enabled by default, generate an initial automatic zip name
            if (_useAutomaticZipName)
            {
                ZipName = string.Empty; // Antes estava ZipName = GenerateAutomaticZipName();
            }
        }

        #region Properties

        public string SourceDirectory
        {
            get => _sourceDirectory;
            set
            {
                if (_sourceDirectory != value)
                {
                    _sourceDirectory = value;
                    OnPropertyChanged();
                }
            }
        }

        public string DestinationDirectory
        {
            get => _destinationDirectory;
            set
            {
                if (_destinationDirectory != value)
                {
                    _destinationDirectory = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ZipName
        {
            get => _zipName;
            set
            {
                if (_zipName != value)
                {
                    _zipName = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool UseAutomaticZipName
        {
            get => _useAutomaticZipName;
            set
            {
                if (_useAutomaticZipName != value)
                {
                    _useAutomaticZipName = value;
                    OnPropertyChanged();

                    // CORREÇÃO: Quando marcado como automático, limpa o campo. 
                    // O nome real será gerado apenas na hora de iniciar o processo.
                    if (_useAutomaticZipName)
                    {
                        ZipName = string.Empty;
                    }
                }
            }
        }

        private string GenerateAutomaticZipName()
        {
            // Format: Deploy_yyyyMMdd_HHmmss.zip
            return $"Deploy_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
        }

        public DateTime? StartDate
        {
            get => _startDate;
            set
            {
                if (_startDate != value)
                {
                    _startDate = value;
                    OnPropertyChanged();
                }
            }
        }

        public string IgnoredFiles
        {
            get => _ignoredFiles;
            set
            {
                if (_ignoredFiles != value)
                {
                    _ignoredFiles = value;
                    OnPropertyChanged();
                }
            }
        }

        public string IgnoredFolders
        {
            get => _ignoredFolders;
            set
            {
                if (_ignoredFolders != value)
                {
                    _ignoredFolders = value;
                    OnPropertyChanged();
                }
            }
        }

        public string IgnoredExtensions
        {
            get => _ignoredExtensions;
            set
            {
                if (_ignoredExtensions != value)
                {
                    _ignoredExtensions = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsProcessing
        {
            get => _isProcessing;
            set
            {
                if (_isProcessing != value)
                {
                    _isProcessing = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsNotProcessing));
                }
            }
        }

        public bool IsNotProcessing => !IsProcessing;

        /// <summary>
        /// Compression progress (0-100)
        /// </summary>
        public double Progress
        {
            get => _progress;
            set
            {
                if (Math.Abs(_progress - value) > 0.001)
                {
                    _progress = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ProgressPercentage));
                }
            }
        }

        public string ProgressPercentage => $"{(int)Progress}%";

        /// <summary>
        /// Status text for the UI
        /// </summary>
        public string Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Relative path of the file currently being packaged (or last reported), for progress UI.
        /// </summary>
        public string CurrentZipEntry
        {
            get => _currentZipEntry;
            set
            {
                if (_currentZipEntry != value)
                {
                    _currentZipEntry = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Whether to filter by start date
        /// </summary>
        public bool UseDateFilter
        {
            get => _useDateFilter;
            set
            {
                if (_useDateFilter != value)
                {
                    _useDateFilter = value;
                    if (!value)
                        StartDate = null;
                    else if (StartDate == null)
                        StartDate = DateTime.Now;

                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Adapter property for DatePicker (DateTimeOffset)
        /// </summary>
        public DateTimeOffset? StartDateOffset
        {
            get => StartDate.HasValue ? new DateTimeOffset(StartDate.Value) : (DateTimeOffset?)null;
            set
            {
                var newVal = value?.DateTime;
                if (StartDate != newVal)
                {
                    StartDate = newVal;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StartDateOffset));
                }
            }
        }

        public ObservableCollection<string> RecentSourceDirectories { get; }

        public ObservableCollection<string> RecentDestinationDirectories { get; }

        public ObservableCollection<string> IgnoredFilesList { get; }

        public ObservableCollection<string> IgnoredFoldersList { get; }

        public ObservableCollection<string> IgnoredExtensionsList { get; }

        #endregion

        #region Methods

        /// <summary>
        /// Loads settings from disk and populates UI.
        /// </summary>
        public async Task LoadSettingsAsync()
        {
            try
            {
                IsLoading = true;
                _settings = await _settingsService.LoadSettingsAsync();
                ApplySettingsToUI();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading settings: {ex}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Loads settings from disk synchronously and populates UI.
        /// </summary>
        public void LoadSettings()
        {
            try
            {
                IsLoading = true;
                _settings = _settingsService.LoadSettings();
                ApplySettingsToUI();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading settings: {ex}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Saves current UI values to settings and persists to disk.
        /// </summary>
        public async Task SaveSettingsAsync()
        {
            try
            {
                IsLoading = true;
                ApplyUIToSettings();
                await _settingsService.SaveSettingsAsync(_settings);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving settings: {ex}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Saves current UI values to settings and persists to disk synchronously.
        /// </summary>
        public void SaveSettings()
        {
            try
            {
                IsLoading = true;
                ApplyUIToSettings();
                _settingsService.SaveSettings(_settings);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving settings: {ex}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Adds a source directory to recents and updates UI.
        /// </summary>
        public void AddRecentSourceDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            _settings.AddRecentSourceDirectory(path);
            UpdateRecentSourceDirectoriesUI();
        }

        /// <summary>
        /// Adds a destination directory to recents and updates UI.
        /// </summary>
        public void AddRecentDestinationDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            _settings.AddRecentDestinationDirectory(path);
            UpdateRecentDestinationDirectoriesUI();
        }

        /// <summary>
        /// Adds an ignored file to the list.
        /// </summary>
        public void AddIgnoredFile(string file)
        {
            if (string.IsNullOrWhiteSpace(file))
                return;

            if (!IgnoredFilesList.Contains(file))
            {
                IgnoredFilesList.Add(file);
                UpdateIgnoredFilesFromList();
            }
        }

        /// <summary>
        /// Removes an ignored file from the list.
        /// </summary>
        public void RemoveIgnoredFile(string file)
        {
            if (IgnoredFilesList.Contains(file))
            {
                IgnoredFilesList.Remove(file);
                UpdateIgnoredFilesFromList();
            }
        }

        /// <summary>
        /// Clears all ignored files.
        /// </summary>
        public void ClearIgnoredFiles()
        {
            IgnoredFilesList.Clear();
            UpdateIgnoredFilesFromList();
        }

        /// <summary>
        /// Adds an ignored folder to the list.
        /// </summary>
        public void AddIgnoredFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
                return;

            if (!IgnoredFoldersList.Contains(folder))
            {
                IgnoredFoldersList.Add(folder);
                UpdateIgnoredFoldersFromList();
            }
        }

        /// <summary>
        /// Removes an ignored folder from the list.
        /// </summary>
        public void RemoveIgnoredFolder(string folder)
        {
            if (IgnoredFoldersList.Contains(folder))
            {
                IgnoredFoldersList.Remove(folder);
                UpdateIgnoredFoldersFromList();
            }
        }

        /// <summary>
        /// Clears all ignored folders.
        /// </summary>
        public void ClearIgnoredFolders()
        {
            IgnoredFoldersList.Clear();
            UpdateIgnoredFoldersFromList();
        }

        /// <summary>
        /// Adds an ignored extension to the list.
        /// </summary>
        public void AddIgnoredExtension(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
                return;

            // Normalize extension to include dot
            var normalized = extension.StartsWith(".") ? extension : "." + extension;

            if (!IgnoredExtensionsList.Contains(normalized))
            {
                IgnoredExtensionsList.Add(normalized);
                UpdateIgnoredExtensionsFromList();
            }
        }

        /// <summary>
        /// Removes an ignored extension from the list.
        /// </summary>
        public void RemoveIgnoredExtension(string extension)
        {
            if (IgnoredExtensionsList.Contains(extension))
            {
                IgnoredExtensionsList.Remove(extension);
                UpdateIgnoredExtensionsFromList();
            }
        }

        /// <summary>
        /// Clears all ignored extensions.
        /// </summary>
        public void ClearIgnoredExtensions()
        {
            IgnoredExtensionsList.Clear();
            UpdateIgnoredExtensionsFromList();
        }

        /// <summary>
        /// Starts the compression process.
        /// </summary>
        public async Task StartCompressionAsync()
        {
            // Validate inputs
            if (string.IsNullOrWhiteSpace(SourceDirectory))
            {
                Status = "Erro: Diretório de origem não informado.";
                return;
            }

            if (string.IsNullOrWhiteSpace(DestinationDirectory))
            {
                Status = "Erro: Diretório de destino não informado.";
                return;
            }

            if (UseAutomaticZipName)
            {
                ZipName = GenerateAutomaticZipName();
            }
            else if (string.IsNullOrWhiteSpace(ZipName))
            {
                Status = "Erro: Nome do arquivo ZIP não informado.";
                return;
            }

            var finalZipName = ZipName;
            if (!finalZipName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                finalZipName += ".zip";
            }

            string finalZipPath = System.IO.Path.Combine(DestinationDirectory, finalZipName);

            // Check for collision
            if (System.IO.File.Exists(finalZipPath))
            {
                if (ConfirmOverwriteRequested != null)
                {
                    var choice = await ConfirmOverwriteRequested.Invoke(finalZipName);
                    if (choice == AppZipOverwriteChoice.Cancel)
                    {
                        Status = "Operação cancelada pelo usuário.";
                        return;
                    }
                    else if (choice == AppZipOverwriteChoice.GenerateRandom)
                    {
                        do
                        {
                            await Task.Delay(1000); // Ensures at least a 1-second difference for HHmmss format
                            finalZipName = GenerateAutomaticZipName();
                            finalZipPath = System.IO.Path.Combine(DestinationDirectory, finalZipName);
                        } while (System.IO.File.Exists(finalZipPath));

                        // Update the ViewModel property so UI reflects the new name used
                        ZipName = finalZipName;
                    }
                    // If Overwrite, we proceed and overwrite the file
                }
            }

            try
            {
                IsProcessing = true;
                _cancellationTokenSource = new CancellationTokenSource();

                Progress = 0;
                CurrentZipEntry = string.Empty;
                Status = "Preparando compactação...";

                var ignoredFilesStr = string.Join(";", IgnoredFilesList);
                var ignoredFoldersStr = string.Join(";", IgnoredFoldersList);
                var ignoredExtensionsStr = string.Join(";", IgnoredExtensionsList);

                var combinedProgress = new Progress<AppZipCompressionProgress>(report =>
                {
                    Status = report.Status;
                    CurrentZipEntry = report.CurrentEntryRelativePath ?? string.Empty;
                    Progress = Math.Clamp(report.Percent, 0, 100);
                });

                string zipPath = await _compressionService.CompressAsync(
                    SourceDirectory,
                    DestinationDirectory,
                    finalZipName,
                    UseDateFilter ? StartDate : null,
                    ignoredFilesStr,
                    ignoredFoldersStr,
                    ignoredExtensionsStr,
                    combinedProgress,
                    _cancellationTokenSource.Token
                ).ConfigureAwait(true);

                // Se zipPath for vazio, significa que o usuário cancelou ou ocorreu um erro
                // (O texto de Status já foi atualizado pelo serviço).
                if (string.IsNullOrEmpty(zipPath))
                {
                    return;
                }

                // Ocorreu tudo bem!
                await SaveSettingsAsync();
                OnCompressionCompleted(zipPath);
            }
            catch (OperationCanceledException)
            {
                Status = "Operação cancelada pelo usuário.";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Compression error: {ex}");
                Status = $"Erro: {ex.Message}";
            }
            finally
            {
                IsProcessing = false;
                CurrentZipEntry = string.Empty;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        /// <summary>
        /// Cancels the ongoing compression process.
        /// </summary>
        public void CancelCompression()
        {
            _cancellationTokenSource?.Cancel();
        }

        /// <summary>
        /// Clears all cached data.
        /// </summary>
        public void Clear()
        {
            _settingsService.ClearCache();
            _settings = new AppZipSettings();
            ClearUI();
        }

        #endregion

        #region Private Helpers

        private void ApplySettingsToUI()
        {
            SourceDirectory = _settings.SourceDirectory;
            DestinationDirectory = _settings.DestinationDirectory;
            UseAutomaticZipName = _settings.UseAutomaticZipName;
            if (UseAutomaticZipName)
            {
                ZipName = string.Empty;
            }
            else
            {
                ZipName = _settings.ZipName;
            }
            StartDate = _settings.StartDate;
            UseDateFilter = _settings.UseDateFilter;

            // Load ignore lists
            IgnoredFilesList.Clear();
            if (!string.IsNullOrEmpty(_settings.IgnoredFiles))
            {
                foreach (var item in _settings.IgnoredFiles.Split(';'))
                {
                    var trimmed = item.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        IgnoredFilesList.Add(trimmed);
                }
            }

            IgnoredFoldersList.Clear();
            if (!string.IsNullOrEmpty(_settings.IgnoredFolders))
            {
                foreach (var item in _settings.IgnoredFolders.Split(';'))
                {
                    var trimmed = item.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        IgnoredFoldersList.Add(trimmed);
                }
            }

            IgnoredExtensionsList.Clear();
            if (!string.IsNullOrEmpty(_settings.IgnoredExtensions))
            {
                foreach (var item in _settings.IgnoredExtensions.Split(';'))
                {
                    var trimmed = item.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        IgnoredExtensionsList.Add(trimmed);
                }
            }

            UpdateRecentSourceDirectoriesUI();
            UpdateRecentDestinationDirectoriesUI();
        }

        private void ApplyUIToSettings()
        {
            _settings.SourceDirectory = SourceDirectory;
            _settings.DestinationDirectory = DestinationDirectory;
            _settings.ZipName = ZipName;
            _settings.UseAutomaticZipName = UseAutomaticZipName;
            _settings.StartDate = StartDate;
            _settings.UseDateFilter = UseDateFilter;
            _settings.IgnoredFiles = string.Join(";", IgnoredFilesList);
            _settings.IgnoredFolders = string.Join(";", IgnoredFoldersList);
            _settings.IgnoredExtensions = string.Join(";", IgnoredExtensionsList);
        }

        private void UpdateRecentSourceDirectoriesUI()
        {
            RecentSourceDirectories.Clear();
            foreach (var dir in _settings.RecentSourceDirectories)
                RecentSourceDirectories.Add(dir);
        }

        private void UpdateRecentDestinationDirectoriesUI()
        {
            RecentDestinationDirectories.Clear();
            foreach (var dir in _settings.RecentDestinationDirectories)
                RecentDestinationDirectories.Add(dir);
        }

        private void UpdateIgnoredFilesFromList()
        {
            IgnoredFiles = string.Join(";", IgnoredFilesList);
            PersistSettingsToDiskQuietly();
        }

        private void UpdateIgnoredFoldersFromList()
        {
            IgnoredFolders = string.Join(";", IgnoredFoldersList);
            PersistSettingsToDiskQuietly();
        }

        private void UpdateIgnoredExtensionsFromList()
        {
            IgnoredExtensions = string.Join(";", IgnoredExtensionsList);
            PersistSettingsToDiskQuietly();
        }

        /// <summary>
        /// Writes current UI state to settings and saves to disk without toggling <see cref="IsLoading"/>.
        /// Used when ignore lists change so navigation away does not lose data.
        /// </summary>
        private void PersistSettingsToDiskQuietly()
        {
            try
            {
                ApplyUIToSettings();
                _settingsService.SaveSettings(_settings);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error persisting AppZip settings: {ex}");
            }
        }

        private void ClearUI()
        {
            SourceDirectory = string.Empty;
            DestinationDirectory = string.Empty;
            ZipName = string.Empty; // Antes estava ZipName = GenerateAutomaticZipName();
            UseAutomaticZipName = true;
            StartDate = null;
            UseDateFilter = false;
            IgnoredFilesList.Clear();
            IgnoredFoldersList.Clear();
            IgnoredExtensionsList.Clear();
            RecentSourceDirectories.Clear();
            RecentDestinationDirectories.Clear();
            Progress = 0;
            CurrentZipEntry = string.Empty;
            Status = "Pronto para iniciar";
        }

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }

        protected virtual void OnCompressionCompleted(string zipPath)
        {
            CompressionCompleted?.Invoke(this, zipPath);
        }

        #endregion
    }
}
