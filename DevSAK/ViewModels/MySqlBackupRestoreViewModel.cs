using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using DevSAK.Models;
using DevSAK.Services;

namespace DevSAK.ViewModels
{
    public class MySqlBackupRestoreViewModel : INotifyPropertyChanged
    {
        private readonly ProtectedSettingsService _protectedSettingsService;
        private readonly MySqlConnectionProfileStore _profileStore;
        private readonly MySqlServerService _serverService;
        private readonly MySqlCliLocator _cliLocator;
        private readonly MySqlBackupRestoreService _backupRestoreService;
        private readonly string _settingsFilePath;
        private DispatcherQueue? _dispatcherQueue;

        private readonly StringBuilder _logBuilder = new();
        private MySqlBackupRestoreSettings _settings = new();
        private CancellationTokenSource? _operationCts;
        private MySqlConnectionProfile? _selectedConnection;
        private string? _selectedDatabase;
        private string _sourceSqlFile = string.Empty;
        private string _outputFolder = string.Empty;
        private string _outputFileNameBase = string.Empty;
        private bool _appendTimestampToFileName = true;
        private bool _disableForeignKeyChecks;
        private bool _recreateDatabaseBeforeRestore;
        private bool _isBusy;
        private bool _isCreatingDatabase;
        private bool _isProgressIndeterminate;
        private double _progressValue;
        private string _statusText = "Pronto para iniciar";
        private string _progressText = "0%";
        private Brush _progressTextBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 200, 29, 37));
        private string _outputLog = string.Empty;
        private MySqlBackupRestoreMode _mode = MySqlBackupRestoreMode.Backup;
        private string _connectionStatusText = "Conexão não testada";
        private Brush _connectionStatusBrush = new SolidColorBrush(Colors.Gray);
        private string _connectionStatusGlyph = "\uE9CE";
        private bool _clearLogEachOperation;
        private bool _isSelectedConnectionValidated;
        private ElementTheme _currentTheme = ElementTheme.Default;

        public MySqlBackupRestoreViewModel()
        {
            _protectedSettingsService = new ProtectedSettingsService();
            _profileStore = new MySqlConnectionProfileStore(_protectedSettingsService);
            _serverService = new MySqlServerService();
            _cliLocator = new MySqlCliLocator();
            _backupRestoreService = new MySqlBackupRestoreService(_cliLocator, new MySqlOutputParser(), _serverService);

            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _settingsFilePath = System.IO.Path.Combine(appDataPath, "DevSAK", "MySqlBackupRestoreSettings.json");

            Connections = new ObservableCollection<MySqlConnectionProfile>();
            Databases = new ObservableCollection<string>();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Must be called once from the View (on the UI thread) so that log/property
        /// updates originating from background threads are safely marshalled back to the UI.
        /// </summary>
        public void SetDispatcherQueue(DispatcherQueue dispatcherQueue)
        {
            _dispatcherQueue = dispatcherQueue;
        }

        public void SetCurrentTheme(ElementTheme theme)
        {
            _currentTheme = theme;
            ProgressTextBrush = CreateInProgressBrush();
        }

        public ObservableCollection<MySqlConnectionProfile> Connections { get; }

        public ObservableCollection<string> Databases { get; }

        public MySqlConnectionProfile? SelectedConnection
        {
            get => _selectedConnection;
            set
            {
                if (_selectedConnection == value)
                {
                    return;
                }

                _selectedConnection = value;
                ResetConnectionStatus();
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanEditSelectedConnection));
                OnPropertyChanged(nameof(CanDeleteSelectedConnection));
                OnPropertyChanged(nameof(CanTestSelectedConnection));
                OnPropertyChanged(nameof(CanRefreshDatabases));
            }
        }

        public string? SelectedDatabase
        {
            get => _selectedDatabase;
            set
            {
                var normalizedValue = NormalizeDatabaseName(value);
                if (string.Equals(_selectedDatabase, normalizedValue, StringComparison.Ordinal))
                {
                    return;
                }

                _selectedDatabase = normalizedValue;
                if (IsBackupMode && string.IsNullOrWhiteSpace(OutputFileNameBase) && !string.IsNullOrWhiteSpace(normalizedValue))
                {
                    OutputFileNameBase = normalizedValue;
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(RestoreDatabaseName));
                OnPropertyChanged(nameof(ShowCreateDatabaseButton));
                OnPropertyChanged(nameof(CanCreateDatabase));
            }
        }

        public string RestoreDatabaseName
        {
            get => SelectedDatabase ?? string.Empty;
            set => SelectedDatabase = value;
        }

        /// <summary>
        /// True when the user typed a non-empty database name that is not yet in the list.
        /// Controls the visibility of the "+" create-database button.
        /// </summary>
        public bool ShowCreateDatabaseButton =>
            IsSelectedConnectionValidated &&
            !string.IsNullOrWhiteSpace(RestoreDatabaseName) &&
            !Databases.Contains(RestoreDatabaseName.Trim(), StringComparer.OrdinalIgnoreCase);

        public bool IsCreatingDatabase
        {
            get => _isCreatingDatabase;
            private set
            {
                if (_isCreatingDatabase == value)
                {
                    return;
                }

                _isCreatingDatabase = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanCreateDatabase));
            }
        }

        public bool CanCreateDatabase => !IsCreatingDatabase && !IsBusy && ShowCreateDatabaseButton;

        public string SourceSqlFile
        {
            get => _sourceSqlFile;
            set
            {
                if (_sourceSqlFile == value)
                {
                    return;
                }

                _sourceSqlFile = value;
                OnPropertyChanged();
            }
        }

        public string OutputFolder
        {
            get => _outputFolder;
            set
            {
                var normalizedValue = value?.Trim() ?? string.Empty;
                if (_outputFolder == normalizedValue)
                {
                    return;
                }

                _outputFolder = normalizedValue;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DestinationSqlFile));
                OnPropertyChanged(nameof(ComputedOutputPath));
            }
        }

        public string OutputFileNameBase
        {
            get => _outputFileNameBase;
            set
            {
                var normalizedValue = NormalizeOutputFileNameBase(value);
                if (_outputFileNameBase == normalizedValue)
                {
                    return;
                }

                _outputFileNameBase = normalizedValue;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DestinationSqlFile));
                OnPropertyChanged(nameof(ComputedOutputFileName));
                OnPropertyChanged(nameof(ComputedOutputPath));
                OnPropertyChanged(nameof(TimestampSuffixPreview));
            }
        }

        public bool AppendTimestampToFileName
        {
            get => _appendTimestampToFileName;
            set
            {
                if (_appendTimestampToFileName == value)
                {
                    return;
                }

                _appendTimestampToFileName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DestinationSqlFile));
                OnPropertyChanged(nameof(ComputedOutputFileName));
                OnPropertyChanged(nameof(ComputedOutputPath));
                OnPropertyChanged(nameof(TimestampSuffixPreview));
            }
        }

        public string DestinationSqlFile
        {
            get => ComputeOutputPath();
            set => ApplyDestinationPath(value);
        }

        public string ComputedOutputFileName => ComputeOutputFileName();

        public string ComputedOutputPath => ComputeOutputPath();

        public string TimestampSuffixPreview => AppendTimestampToFileName
            ? $"_{GetTimestampToken()}.sql"
            : ".sql";

        public bool DisableForeignKeyChecks
        {
            get => _disableForeignKeyChecks;
            set
            {
                if (_disableForeignKeyChecks == value)
                {
                    return;
                }

                _disableForeignKeyChecks = value;
                OnPropertyChanged();
            }
        }

        public bool RecreateDatabaseBeforeRestore
        {
            get => _recreateDatabaseBeforeRestore;
            set
            {
                if (_recreateDatabaseBeforeRestore == value)
                {
                    return;
                }

                _recreateDatabaseBeforeRestore = value;
                OnPropertyChanged();
            }
        }

        public MySqlBackupRestoreMode Mode
        {
            get => _mode;
            set
            {
                if (_mode == value)
                {
                    return;
                }

                _mode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsBackupMode));
                OnPropertyChanged(nameof(IsRestoreMode));
                OnPropertyChanged(nameof(BackupFileVisibility));
                OnPropertyChanged(nameof(RestoreFileVisibility));
                OnPropertyChanged(nameof(ForeignKeyOptionVisibility));
                OnPropertyChanged(nameof(StartButtonText));
            }
        }

        public bool IsBackupMode => Mode == MySqlBackupRestoreMode.Backup;

        public bool IsRestoreMode => Mode == MySqlBackupRestoreMode.Restore;

        public Visibility BackupFileVisibility => IsBackupMode ? Visibility.Visible : Visibility.Collapsed;

        public Visibility RestoreFileVisibility => IsRestoreMode ? Visibility.Visible : Visibility.Collapsed;

        public Visibility ForeignKeyOptionVisibility => IsRestoreMode ? Visibility.Visible : Visibility.Collapsed;

        public string StartButtonText => IsBackupMode ? "Iniciar Backup" : "Iniciar Restauração";

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (_isBusy == value)
                {
                    return;
                }

                _isBusy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsIdle));
            }
        }

        public bool IsIdle => !IsBusy;

        public bool IsProgressIndeterminate
        {
            get => _isProgressIndeterminate;
            private set
            {
                if (_isProgressIndeterminate == value)
                {
                    return;
                }

                _isProgressIndeterminate = value;
                OnPropertyChanged();
            }
        }

        public double ProgressValue
        {
            get => _progressValue;
            private set
            {
                if (Math.Abs(_progressValue - value) < 0.01)
                {
                    return;
                }

                _progressValue = value;
                OnPropertyChanged();
            }
        }

        public string StatusText
        {
            get => _statusText;
            private set
            {
                if (_statusText == value)
                {
                    return;
                }

                _statusText = value;
                OnPropertyChanged();
            }
        }

        public string ProgressText
        {
            get => _progressText;
            private set
            {
                if (_progressText == value)
                {
                    return;
                }

                _progressText = value;
                OnPropertyChanged();
            }
        }

        public Brush ProgressTextBrush
        {
            get => _progressTextBrush;
            private set
            {
                _progressTextBrush = value;
                OnPropertyChanged();
            }
        }

        public string OutputLog
        {
            get => _outputLog;
            private set
            {
                if (_outputLog == value)
                {
                    return;
                }

                _outputLog = value;
                OnPropertyChanged();
            }
        }

        public bool ClearLogEachOperation
        {
            get => _clearLogEachOperation;
            set
            {
                if (_clearLogEachOperation == value)
                {
                    return;
                }

                _clearLogEachOperation = value;
                OnPropertyChanged();
            }
        }

        public string ConnectionStatusText
        {
            get => _connectionStatusText;
            private set
            {
                if (_connectionStatusText == value)
                {
                    return;
                }

                _connectionStatusText = value;
                OnPropertyChanged();
            }
        }

        public Brush ConnectionStatusBrush
        {
            get => _connectionStatusBrush;
            private set
            {
                _connectionStatusBrush = value;
                OnPropertyChanged();
            }
        }

        public string ConnectionStatusGlyph
        {
            get => _connectionStatusGlyph;
            private set
            {
                if (_connectionStatusGlyph == value)
                {
                    return;
                }

                _connectionStatusGlyph = value;
                OnPropertyChanged();
            }
        }

        public bool CanEditSelectedConnection => SelectedConnection is not null && IsIdle;

        public bool CanDeleteSelectedConnection => SelectedConnection is not null && IsIdle;

        public bool CanTestSelectedConnection => SelectedConnection is not null && IsIdle;

        public bool CanRefreshDatabases => SelectedConnection is not null && IsIdle;

        public bool IsSelectedConnectionValidated
        {
            get => _isSelectedConnectionValidated;
            private set
            {
                if (_isSelectedConnectionValidated == value)
                {
                    return;
                }

                _isSelectedConnectionValidated = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowCreateDatabaseButton));
                OnPropertyChanged(nameof(CanCreateDatabase));
            }
        }

        public async Task InitializeAsync()
        {
            Connections.Clear();
            foreach (var profile in await _profileStore.LoadAsync())
            {
                Connections.Add(profile);
            }

            _settings = await LoadSettingsAsync();
            Mode = _settings.LastMode;
            SourceSqlFile = _settings.LastSourceSqlFile ?? string.Empty;
            ApplyDestinationPath(_settings.LastDestinationSqlFile);
            DisableForeignKeyChecks = _settings.DisableForeignKeyChecks;
            RecreateDatabaseBeforeRestore = _settings.RecreateDatabaseBeforeRestore;
            ClearLogEachOperation = _settings.ClearLogEachOperation;

            // Do NOT auto-select a connection on load — the user must choose one explicitly.
            // This prevents hidden preloads and keeps the server ComboBox empty at startup.
            SelectedConnection = null;
            SelectedDatabase = null;

            AppendLog("Perfis e configurações carregados.");
            RefreshResolvedCliPaths();
        }

        public async Task AddOrUpdateConnectionAsync(MySqlConnectionProfile profile)
        {
            var existing = Connections.FirstOrDefault(item => item.Id == profile.Id);
            if (existing is null)
            {
                Connections.Add(profile);
            }
            else
            {
                var index = Connections.IndexOf(existing);
                Connections[index] = profile;
            }

            SelectedConnection = profile;
            await PersistConnectionsAsync();
            AppendLog($"Conexão salva: {profile.FriendlyName}");
        }

        public async Task DeleteSelectedConnectionAsync()
        {
            if (SelectedConnection is null)
            {
                return;
            }

            var removedId = SelectedConnection.Id;
            Connections.Remove(SelectedConnection);
            SelectedConnection = Connections.FirstOrDefault();

            if (_settings.LastSelectedConnectionId == removedId)
            {
                _settings.LastSelectedConnectionId = SelectedConnection?.Id;
            }

            await PersistConnectionsAsync();
            await SaveSettingsAsync();
            Databases.Clear();
            SelectedDatabase = null;
            AppendLog("Conexão removida.");
        }

        public async Task TestSelectedConnectionAsync()
        {
            if (SelectedConnection is null)
            {
                return;
            }

            try
            {
                IsBusy = true;
                StatusText = "Conectando...";
                IsProgressIndeterminate = true;
                ProgressText = "Validando";
                AppendLog($"Testando conexão em {SelectedConnection.Host}:{SelectedConnection.Port}...");

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var result = await _serverService.TestConnectionAsync(SelectedConnection, cts.Token);

                SelectedConnection.LastSuccessfulTestUtc = DateTimeOffset.UtcNow;
                SetConnectionSuccess($"Conectado a {result.ServerHost} ({result.ServerVersion})");
                StatusText = "Listando bancos...";
                AppendLog($"Conexão OK. Servidor: {result.ServerHost} | Versão: {result.ServerVersion}");

                await RefreshDatabasesAsync(cts.Token);
                await PersistConnectionsAsync();
            }
            catch (Exception ex)
            {
                SetConnectionFailure($"Falha ao conectar: {ex.Message}");
                StatusText = "Falha na operação";
                AppendLog($"Erro de conexão: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
                SetProgressState(0, false, "0%", false);
            }
        }

        public async Task RefreshDatabasesAsync(CancellationToken cancellationToken, bool keepCurrentValueWhenMissing = false)
        {
            if (SelectedConnection is null)
            {
                return;
            }

            var currentDatabase = SelectedDatabase;

            Databases.Clear();
            var databases = await _serverService.ListDatabasesAsync(SelectedConnection, cancellationToken);
            foreach (var database in databases)
            {
                Databases.Add(database);
            }

            if (!string.IsNullOrWhiteSpace(currentDatabase) && databases.Contains(currentDatabase, StringComparer.OrdinalIgnoreCase))
            {
                SelectedDatabase = databases.First(item => string.Equals(item, currentDatabase, StringComparison.OrdinalIgnoreCase));
            }
            else if (keepCurrentValueWhenMissing && !string.IsNullOrWhiteSpace(currentDatabase))
            {
                SelectedDatabase = currentDatabase;
            }
            else
            {
                SelectedDatabase = null;
            }

            AppendLog($"{Databases.Count} bancos encontrados.");
            StatusText = "Conexão pronta";
            OnPropertyChanged(nameof(ShowCreateDatabaseButton));
        }

        public async Task RefreshDatabasesForBackupAsync()
        {
            if (SelectedConnection is null)
            {
                throw new InvalidOperationException("Selecione uma conexão antes de atualizar a lista de bancos.");
            }

            try
            {
                IsBusy = true;
                StatusText = "Listando bancos...";
                IsProgressIndeterminate = true;
                ProgressText = "Atualizando";
                AppendLog("Atualizando lista de bancos para backup...");

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                await RefreshDatabasesAsync(cts.Token, keepCurrentValueWhenMissing: false);
            }
            finally
            {
                IsBusy = false;
                SetProgressState(0, false, "0%", false);
            }
        }

        public async Task RefreshDatabasesForRestoreAsync()
        {
            if (SelectedConnection is null)
            {
                throw new InvalidOperationException("Selecione uma conexão antes de atualizar a lista de bancos.");
            }

            try
            {
                IsBusy = true;
                StatusText = "Listando bancos...";
                IsProgressIndeterminate = true;
                ProgressText = "Atualizando";
                AppendLog("Atualizando lista de bancos para restauração...");

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                await RefreshDatabasesAsync(cts.Token, keepCurrentValueWhenMissing: true);
            }
            finally
            {
                IsBusy = false;
                SetProgressState(0, false, "0%", false);
            }
        }

        public async Task ExecuteAsync()
        {
            ValidateBeforeExecution();

            var targetDatabase = IsBackupMode ? SelectedDatabase : RestoreDatabaseName.Trim();

            if (SelectedConnection is null || string.IsNullOrWhiteSpace(targetDatabase))
            {
                throw new InvalidOperationException("Selecione uma conexão válida e um banco de dados.");
            }

            _operationCts = new CancellationTokenSource();

            // Progress<T> is created here, on the UI thread, so it captures the UI
            // SynchronizationContext. All Report() callbacks will marshal to the UI thread
            // automatically — even though the operation runs on the thread pool below.
            var progress = new Progress<MySqlOperationUpdate>(HandleOperationUpdate);

            try
            {
                IsBusy = true;
                if (ClearLogEachOperation)
                {
                    ClearLog();
                }

                SetProgressState(0, false, "0%", false);
                AppendLog(IsBackupMode ? "Iniciando rotina de backup..." : "Iniciando rotina de restauração...");

                UpdateSettingsFromUi();
                await SaveSettingsAsync();

                // Capture locals before entering Task.Run to avoid cross-thread ViewModel access.
                var connection = SelectedConnection;
                var database = targetDatabase;
                var sourceFile = SourceSqlFile;
                var destFile = DestinationSqlFile;
                var settings = _settings;
                var disableFk = DisableForeignKeyChecks;
                var recreateDatabaseBeforeRestore = RecreateDatabaseBeforeRestore;
                var token = _operationCts.Token;

                if (IsBackupMode)
                {
                    await _backupRestoreService.BackupAsync(
                        connection,
                        database,
                        destFile,
                        settings,
                        progress,
                        token).ConfigureAwait(true);
                }
                else
                {
                    await _backupRestoreService.RestoreAsync(
                        connection,
                        database,
                        sourceFile,
                        disableFk,
                        recreateDatabaseBeforeRestore,
                        settings,
                        progress,
                        token).ConfigureAwait(true);
                }

                // UI thread from here.
                StatusText = "Concluído com sucesso";
                SetProgressState(100, false, "Concluído", true);
                AppendLog(IsBackupMode ? "Backup concluído com sucesso." : "Restauração concluída com sucesso.");
            }
            catch (OperationCanceledException)
            {
                StatusText = "Operação cancelada";
                SetProgressState(0, false, "Cancelado", false);
                AppendLog("Operação cancelada pelo usuário.");
            }
            catch (Exception ex)
            {
                StatusText = "Falha na operação";
                SetProgressState(0, false, "Falha", false);
                AppendLog($"Falha: {ex.Message}");
                throw;
            }
            finally
            {
                IsBusy = false;
                _operationCts?.Dispose();
                _operationCts = null;
            }
        }

        public async Task CreateRestoreDatabaseAsync()
        {
            if (SelectedConnection is null)
            {
                throw new InvalidOperationException("Selecione e teste uma conexão antes de criar um banco de dados.");
            }

            if (!IsSelectedConnectionValidated)
            {
                throw new InvalidOperationException("A conexão selecionada ainda não foi testada. Clique em \"Testar Conexão\" primeiro.");
            }

            var dbName = RestoreDatabaseName.Trim();
            if (string.IsNullOrWhiteSpace(dbName))
            {
                throw new InvalidOperationException("Informe o nome do banco de dados a ser criado.");
            }

            if (Databases.Contains(dbName, StringComparer.OrdinalIgnoreCase))
            {
                // Already exists — just select it.
                RestoreDatabaseName = Databases.First(d => string.Equals(d, dbName, StringComparison.OrdinalIgnoreCase));
                AppendLog($"O banco \"{ dbName}\" já existe e foi selecionado.");
                return;
            }

            try
            {
                IsCreatingDatabase = true;
                AppendLog($"Criando banco de dados \"{dbName}\"...");

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await _serverService.CreateDatabaseAsync(SelectedConnection, dbName, cts.Token);

                AppendLog($"Banco \"{ dbName}\" criado com sucesso.");

                // Refresh the list and auto-select.
                await RefreshDatabasesAsync(cts.Token);
                RestoreDatabaseName = Databases.FirstOrDefault(d => string.Equals(d, dbName, StringComparison.OrdinalIgnoreCase)) ?? dbName;
                OnPropertyChanged(nameof(ShowCreateDatabaseButton));
            }
            catch
            {
                AppendLog($"Falha ao criar o banco \"{dbName}\".");
                throw;
            }
            finally
            {
                IsCreatingDatabase = false;
            }
        }

        public void CancelOperation()
        {
            _operationCts?.Cancel();
        }

        public void SetCancelledState()
        {
            StatusText = "Operação cancelada";
            SetProgressState(0, false, "Cancelado", false);
            AppendLog("Operação cancelada pelo usuário.");
        }

        public string BuildConfirmationMessage()
        {
            var action = IsBackupMode ? "backup" : "restauração";
            var filePath = IsBackupMode ? ComputedOutputPath : SourceSqlFile;
            var targetDatabase = IsBackupMode ? SelectedDatabase : RestoreDatabaseName.Trim();
            var recreateWarning = IsRestoreMode && RecreateDatabaseBeforeRestore
                ? "\n\nAtenção: a base de dados será excluída e recriada antes da restauração."
                : string.Empty;

            return $"Você está prestes a iniciar um {action} no banco \"{targetDatabase}\" usando a conexão \"{SelectedConnection}\".\n\nArquivo:\n{filePath}{recreateWarning}\n\nConfirme para continuar.";
        }

        public void AppendLog(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");

            void DoAppend()
            {
                _logBuilder.Append('[').Append(timestamp).Append("] ").AppendLine(message);
                OutputLog = _logBuilder.ToString();
            }

            // If we are already on the UI thread (or no dispatcher is wired up yet), update directly.
            // Otherwise post to the UI thread so PropertyChanged doesn't fire from a background thread.
            if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess)
            {
                DoAppend();
            }
            else
            {
                _dispatcherQueue.TryEnqueue(DoAppend);
            }
        }

        public void ClearLog()
        {
            _logBuilder.Clear();
            OutputLog = string.Empty;
        }

        private void HandleOperationUpdate(MySqlOperationUpdate update)
        {
            if (!string.IsNullOrWhiteSpace(update.LogLine))
            {
                AppendLog(update.LogLine);
            }

            if (!string.IsNullOrWhiteSpace(update.Status))
            {
                StatusText = update.Status;
            }

            if (!double.IsNaN(update.Percentage))
            {
                ProgressValue = Math.Max(0, Math.Min(100, update.Percentage));
                ProgressText = $"{Math.Round(ProgressValue)}%";
                ProgressTextBrush = CreateInProgressBrush();
            }
            else if (update.IsIndeterminate)
            {
                ProgressText = "Em andamento";
                ProgressTextBrush = CreateInProgressBrush();
            }

            if (update.Status is not null || !double.IsNaN(update.Percentage))
            {
                IsProgressIndeterminate = update.IsIndeterminate;
            }
        }

        public void ValidateBeforeExecution()
        {
            if (SelectedConnection is null)
            {
                throw new InvalidOperationException("Selecione uma conexão salva.");
            }

            if (!IsSelectedConnectionValidated)
            {
                throw new InvalidOperationException("Teste a conexão e carregue a lista de bancos antes de iniciar a operação.");
            }

            if (IsBackupMode)
            {
                if (string.IsNullOrWhiteSpace(SelectedDatabase) ||
                    !Databases.Contains(SelectedDatabase, StringComparer.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Selecione o banco de dados que será processado.");
                }

                if (string.IsNullOrWhiteSpace(OutputFolder))
                {
                    throw new InvalidOperationException("Informe a pasta de destino do backup.");
                }

                if (string.IsNullOrWhiteSpace(OutputFileNameBase))
                {
                    throw new InvalidOperationException("Informe o nome do arquivo SQL.");
                }

                var invalidChars = GetInvalidOutputNameCharacters(OutputFileNameBase);
                if (invalidChars.Length > 0)
                {
                    throw new InvalidOperationException($"O nome do arquivo contém caracteres inválidos: {string.Join(" ", invalidChars)}");
                }

                if (string.IsNullOrWhiteSpace(DestinationSqlFile))
                {
                    throw new InvalidOperationException("Não foi possível montar o caminho final do arquivo SQL.");
                }
            }
            else
            {
                var restoreTarget = NormalizeDatabaseName(RestoreDatabaseName);
                if (string.IsNullOrWhiteSpace(restoreTarget))
                {
                    throw new InvalidOperationException("Selecione um banco de dados de destino válido para a restauração.");
                }

                if (!RecreateDatabaseBeforeRestore &&
                    !Databases.Contains(restoreTarget, StringComparer.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Selecione um banco de dados existente ou marque a opção para excluir e recriar a base antes da restauração.");
                }

                if (string.IsNullOrWhiteSpace(SourceSqlFile) || !System.IO.File.Exists(SourceSqlFile))
                {
                    throw new InvalidOperationException("Selecione um arquivo .sql ou compactado válido para a restauração.");
                }
            }
        }

        private async Task PersistConnectionsAsync()
        {
            await _profileStore.SaveAsync(Connections);
        }

        private void UpdateSettingsFromUi()
        {
            _settings.LastSelectedConnectionId = SelectedConnection?.Id;
            _settings.LastSelectedDatabase = IsBackupMode ? SelectedDatabase : RestoreDatabaseName.Trim();
            _settings.LastMode = Mode;
            _settings.LastSourceSqlFile = NormalizeOptionalPath(SourceSqlFile);
            _settings.LastDestinationSqlFile = NormalizeOptionalPath(ComputedOutputPath);
            _settings.DisableForeignKeyChecks = DisableForeignKeyChecks;
            _settings.RecreateDatabaseBeforeRestore = RecreateDatabaseBeforeRestore;
            _settings.ClearLogEachOperation = ClearLogEachOperation;
            _settings.RegisterRecentConnection(SelectedConnection?.Id);
        }

        private async Task<MySqlBackupRestoreSettings> LoadSettingsAsync()
        {
            if (!System.IO.File.Exists(_settingsFilePath))
            {
                return new MySqlBackupRestoreSettings();
            }

            try
            {
                var json = await System.IO.File.ReadAllTextAsync(_settingsFilePath);
                return System.Text.Json.JsonSerializer.Deserialize<MySqlBackupRestoreSettings>(json)
                    ?? new MySqlBackupRestoreSettings();
            }
            catch
            {
                return new MySqlBackupRestoreSettings();
            }
        }

        private async Task SaveSettingsAsync()
        {
            UpdateSettingsFromUi();
            var directory = System.IO.Path.GetDirectoryName(_settingsFilePath)!;
            System.IO.Directory.CreateDirectory(directory);
            var json = System.Text.Json.JsonSerializer.Serialize(_settings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await System.IO.File.WriteAllTextAsync(_settingsFilePath, json);
        }

        private void RefreshResolvedCliPaths()
        {
            var resolved = _cliLocator.Locate();
            AppendLog($"mysql.exe: {(resolved.MysqlPath ?? "não localizado")}");
            AppendLog($"mysqldump.exe: {(resolved.MysqldumpPath ?? "não localizado")}");
            AppendLog($"mysqlpump.exe: {(resolved.MysqlPumpPath ?? "não localizado")}");
        }

        public string? GetCliWarningMessage()
        {
            return _cliLocator.HasRequiredFiles() ? null : _cliLocator.GetMissingFilesWarning();
        }

        private void ResetConnectionStatus()
        {
            ConnectionStatusText = "Conexão não testada";
            ConnectionStatusBrush = new SolidColorBrush(Colors.Gray);
            ConnectionStatusGlyph = "\uE9CE";
            IsSelectedConnectionValidated = false;
            Databases.Clear();
            SelectedDatabase = SelectedConnection?.DefaultDatabase;
        }

        private void SetConnectionSuccess(string message)
        {
            ConnectionStatusText = message;
            ConnectionStatusBrush = new SolidColorBrush(Colors.ForestGreen);
            ConnectionStatusGlyph = "\uE73E";
            IsSelectedConnectionValidated = true;
        }

        private void SetConnectionFailure(string message)
        {
            ConnectionStatusText = message;
            ConnectionStatusBrush = new SolidColorBrush(Colors.IndianRed);
            ConnectionStatusGlyph = "\uEA39";
            IsSelectedConnectionValidated = false;
        }

        private static string? NormalizeOptionalPath(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string NormalizeOutputFileNameBase(string? value)
        {
            var normalized = value?.Trim() ?? string.Empty;
            if (normalized.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[..^4];
            }

            return normalized.Trim();
        }

        private static string? NormalizeDatabaseName(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private void ApplyDestinationPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                OutputFolder = string.Empty;
                OutputFileNameBase = SelectedDatabase ?? string.Empty;
                AppendTimestampToFileName = true;
                return;
            }

            var trimmedPath = path.Trim();
            OutputFolder = System.IO.Path.GetDirectoryName(trimmedPath) ?? string.Empty;

            var fileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(trimmedPath) ?? string.Empty;
            var timestampMatch = System.Text.RegularExpressions.Regex.Match(fileNameWithoutExtension, @"^(.*)_(\d{12})$");
            if (timestampMatch.Success)
            {
                OutputFileNameBase = timestampMatch.Groups[1].Value;
                AppendTimestampToFileName = true;
            }
            else
            {
                OutputFileNameBase = fileNameWithoutExtension;
                AppendTimestampToFileName = false;
            }
        }

        private string ComputeOutputFileName()
        {
            if (string.IsNullOrWhiteSpace(OutputFileNameBase))
            {
                return string.Empty;
            }

            return AppendTimestampToFileName
                ? $"{OutputFileNameBase}_{GetTimestampToken()}.sql"
                : $"{OutputFileNameBase}.sql";
        }

        private string ComputeOutputPath()
        {
            if (string.IsNullOrWhiteSpace(OutputFolder) || string.IsNullOrWhiteSpace(OutputFileNameBase))
            {
                return string.Empty;
            }

            return System.IO.Path.Combine(OutputFolder, ComputeOutputFileName());
        }

        private static string GetTimestampToken()
        {
            return DateTime.Now.ToString("yyyyMMddHHmm");
        }

        private static char[] GetInvalidOutputNameCharacters(string value)
        {
            return value.Where(ch => System.IO.Path.GetInvalidFileNameChars().Contains(ch)).Distinct().ToArray();
        }

        private void SetProgressState(double value, bool isIndeterminate, string text, bool isSuccess)
        {
            ProgressValue = value;
            IsProgressIndeterminate = isIndeterminate;
            ProgressText = text;
            ProgressTextBrush = isSuccess
                ? new SolidColorBrush(ColorHelper.FromArgb(255, 0, 150, 40))
                : CreateInProgressBrush();
        }

        private Brush CreateInProgressBrush()
        {
            return _currentTheme == ElementTheme.Dark
                ? new SolidColorBrush(Colors.White)
                : new SolidColorBrush(ColorHelper.FromArgb(255, 200, 29, 37));
        }

        private void OnPropertyChanged(string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
