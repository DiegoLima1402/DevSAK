using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevSAK.Models;
using DevSAK.Services;

namespace DevSAK.ViewModels
{
    public sealed class ExportPemViewModel : INotifyPropertyChanged
    {
        private readonly CertificateExportSettings _settings = new();
        private readonly CertificatePemExportService _exportService = new();

        private string _inputCertificatePath = string.Empty;
        private string _password = string.Empty;
        private string _outputFolder = string.Empty;
        private string _outputBaseFileName = "certificado";
        private ExportPemMode _exportMode = ExportPemMode.CombinedPemSingleFile;
        private string _publicPemFileName = string.Empty;
        private string _privatePemFileName = string.Empty;
        private string _lastGeneratedBase = "certificado";

        private bool _isProcessing;
        private double _progress;
        private string _status = "Pronto para iniciar";
        private string _logText = string.Empty;
        private bool _autoScrollLog = true;
        private string _warningText = string.Empty;
        private string _errorText = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;
        public Func<Task<bool>>? ConfirmOverwriteRequested { get; set; }

        public ExportPemViewModel()
        {
            RecentInputFiles = new ObservableCollection<string>();
            RecentOutputFolders = new ObservableCollection<string>();
            EnsureOutputNamesInitialized();
        }

        /// <summary>
        /// Settings snapshot suitable for persistence (no secret-safe storage yet).
        /// </summary>
        public CertificateExportSettings Settings
        {
            get
            {
                _settings.InputCertificatePath = InputCertificatePath;
                _settings.Password = Password;
                _settings.OutputFolder = OutputFolder;
                _settings.OutputBaseFileName = OutputBaseFileName;
                _settings.PublicPemFileName = PublicPemFileName;
                _settings.PrivatePemFileName = PrivatePemFileName;
                _settings.ExportMode = ExportMode;
                _settings.AutoScrollEnabled = AutoScrollEnabled;
                _settings.RecentInputFiles = RecentInputFiles.ToList();
                _settings.RecentOutputFolders = RecentOutputFolders.ToList();
                return _settings;
            }
        }

        public string InputCertificatePath
        {
            get => _inputCertificatePath;
            set
            {
                if (_inputCertificatePath != value)
                {
                    _inputCertificatePath = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CanExportPrivateKey));
                    OnPropertyChanged(nameof(IsPublicOnlyCertificate));
                    OnPropertyChanged(nameof(PrivateKeyModesEnabled));
                    OnPropertyChanged(nameof(IsSupportedInputType));
                    OnPropertyChanged(nameof(IsPfxOrP12));
                    OnPropertyChanged(nameof(IsPasswordRequired));
                    OnPropertyChanged(nameof(CanRun));

                    if (!string.IsNullOrWhiteSpace(_inputCertificatePath))
                    {
                        AddRecentInputFile(_inputCertificatePath);
                    }

                    OnPropertyChanged(nameof(CanExportPrivateKey));
                    CoerceExportModeIfNeeded();
                    RefreshValidationMessages();
                }
            }
        }

        public string Password
        {
            get => _password;
            set
            {
                if (_password != value)
                {
                    _password = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsPasswordValid));
                    OnPropertyChanged(nameof(CanRun));
                    RefreshValidationMessages();
                }
            }
        }

        public string OutputFolder
        {
            get => _outputFolder;
            set
            {
                if (_outputFolder != value)
                {
                    _outputFolder = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsOutputFolderValid));
                    OnPropertyChanged(nameof(CanRun));
                    RefreshValidationMessages();

                    if (!string.IsNullOrWhiteSpace(_outputFolder))
                    {
                        AddRecentOutputFolder(_outputFolder);
                    }
                }
            }
        }

        public string OutputBaseFileName
        {
            get => _outputBaseFileName;
            set
            {
                if (_outputBaseFileName != value)
                {
                    var oldBase = _outputBaseFileName;
                    _outputBaseFileName = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsOutputBaseFileNameValid));
                    AutoGenerateOutputNamesIfNeeded(oldBase, _outputBaseFileName);
                    OnPropertyChanged(nameof(CombinedOutputFileNamePreview));
                    OnPropertyChanged(nameof(CanRun));
                    RefreshValidationMessages();
                }
            }
        }

        public string PublicPemFileName
        {
            get => _publicPemFileName;
            set
            {
                if (_publicPemFileName != value)
                {
                    _publicPemFileName = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CanRun));
                    RefreshValidationMessages();
                }
            }
        }

        public string PrivatePemFileName
        {
            get => _privatePemFileName;
            set
            {
                if (_privatePemFileName != value)
                {
                    _privatePemFileName = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CanRun));
                    RefreshValidationMessages();
                }
            }
        }

        public bool IsSplitOutputSelected => ExportMode == ExportPemMode.SeparatePublicAndPrivatePem;

        public string CombinedOutputFileNamePreview => $"{(string.IsNullOrWhiteSpace(OutputBaseFileName) ? "certificado" : OutputBaseFileName)}.pem";

        public ExportPemMode ExportMode
        {
            get => _exportMode;
            set
            {
                if (_exportMode != value)
                {
                    _exportMode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsPrivateKeyModeSelected));
                    OnPropertyChanged(nameof(IsSplitOutputSelected));
                    OnPropertyChanged(nameof(CombinedOutputFileNamePreview));
                    EnsureOutputNamesInitialized();
                    OnPropertyChanged(nameof(CanRun));
                    RefreshValidationMessages();
                }
            }
        }

        public ObservableCollection<string> RecentInputFiles { get; }
        public ObservableCollection<string> RecentOutputFolders { get; }

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

        public string ProgressPercentage => $"{(int)Math.Clamp(Progress, 0, 100)}%";

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

        public string LogText
        {
            get => _logText;
            set
            {
                if (_logText != value)
                {
                    _logText = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool AutoScrollEnabled
        {
            get => _autoScrollLog;
            set
            {
                if (_autoScrollLog != value)
                {
                    _autoScrollLog = value;
                    OnPropertyChanged();
                }
            }
        }

        public string WarningText
        {
            get => _warningText;
            set
            {
                if (_warningText != value)
                {
                    _warningText = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsWarningVisible));
                }
            }
        }

        public bool IsWarningVisible => !string.IsNullOrWhiteSpace(WarningText);

        public string ErrorText
        {
            get => _errorText;
            set
            {
                if (_errorText != value)
                {
                    _errorText = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsErrorVisible));
                }
            }
        }

        public bool IsErrorVisible => !string.IsNullOrWhiteSpace(ErrorText);

        public bool IsPublicOnlyCertificate => GetCertificateExtension() is ".cer" or ".crt";

        public bool IsPfxOrP12 => GetCertificateExtension() is ".pfx" or ".p12";

        public bool IsSupportedInputType => GetCertificateExtension() is ".pfx" or ".p12" or ".cer" or ".crt";

        public bool CanExportPrivateKey
        {
            get
            {
                var ext = GetCertificateExtension();
                // Private key requires PFX/P12 containers
                return ext is ".pfx" or ".p12";
            }
        }

        public bool PrivateKeyModesEnabled => CanExportPrivateKey;

        public bool IsPrivateKeyModeSelected =>
            ExportMode is ExportPemMode.PrivateKeyOnly
                or ExportPemMode.SeparatePublicAndPrivatePem
                or ExportPemMode.CombinedPemSingleFile;

        public bool IsPasswordRequired => IsPfxOrP12;

        public bool IsPasswordValid => !IsPasswordRequired || !string.IsNullOrWhiteSpace(Password);

        public bool IsOutputFolderValid => !string.IsNullOrWhiteSpace(OutputFolder);

        public bool IsOutputBaseFileNameValid
        {
            get
            {
                if (string.IsNullOrWhiteSpace(OutputBaseFileName))
                    return false;

                return OutputBaseFileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
            }
        }

        public bool AreSplitFileNamesValid
        {
            get
            {
                if (!IsSplitOutputSelected)
                    return true;

                var pub = EffectivePublicFileName();
                var priv = EffectivePrivateFileName();

                if (string.IsNullOrWhiteSpace(pub) || string.IsNullOrWhiteSpace(priv))
                    return false;

                if (!pub.EndsWith(".cert.pem", StringComparison.OrdinalIgnoreCase))
                    return false;
                if (!priv.EndsWith(".key.pem", StringComparison.OrdinalIgnoreCase))
                    return false;

                if (pub.Equals(priv, StringComparison.OrdinalIgnoreCase))
                    return false;

                if (pub.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    return false;
                if (priv.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    return false;

                if (pub.Contains(Path.DirectorySeparatorChar) || pub.Contains(Path.AltDirectorySeparatorChar))
                    return false;
                if (priv.Contains(Path.DirectorySeparatorChar) || priv.Contains(Path.AltDirectorySeparatorChar))
                    return false;

                return true;
            }
        }

        public bool CanRun =>
            IsNotProcessing
            && !string.IsNullOrWhiteSpace(InputCertificatePath)
            && IsSupportedInputType
            && (!IsPublicOnlyCertificate || ExportMode == ExportPemMode.PublicCertificateOnly)
            && IsPasswordValid
            && IsOutputFolderValid
            && IsOutputBaseFileNameValid
            && AreSplitFileNamesValid;

        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            if (IsProcessing)
                return;

            bool errorOccurred = false;
            try
            {
                RefreshValidationMessages();
                if (!CanStartExport())
                {
                    Status = "Não foi possível iniciar a exportação.";
                    if (!string.IsNullOrWhiteSpace(ErrorText))
                        AppendLog($"Falha de validação: {ErrorText}");
                    return;
                }

                IsProcessing = true;
                Progress = 0;
                Status = "Iniciando...";
                ErrorText = string.Empty;
                WarningText = string.Empty;

                var progress = new Progress<CertificateExportProgress>(p =>
                {
                    if (errorOccurred) return;
                    Progress = Math.Clamp(p.Percent, 0, 100);
                    Status = p.Status;
                });

                AppendLog($"Início: {GetModeLabel(ExportMode)}");
                AppendLog($"Entrada: {InputCertificatePath}");
                AppendLog($"Saída: {OutputFolder} (base: {OutputBaseFileName})");

                // Make sure we respect the extension rule in VM too.
                CoerceExportModeIfNeeded();

                if (DoesAnyTargetFileExist())
                {
                    if (ConfirmOverwriteRequested != null)
                    {
                        bool shouldOverwrite = await ConfirmOverwriteRequested.Invoke();
                        if (!shouldOverwrite)
                        {
                            Status = "Operação cancelada pelo usuário.";
                            Progress = 0;
                            AppendLog("Cancelado pelo usuário. Operação interrompida para evitar sobrescrever o arquivo.");
                            return; // finally block will run and set IsProcessing = false
                        }
                    }
                }

                await _exportService.ExportAsync(Settings, progress, cancellationToken).ConfigureAwait(true);

                Status = "Exportação concluída com sucesso.";
                AppendLog("Exportação concluída com sucesso.");
            }
            catch (OperationCanceledException)
            {
                errorOccurred = true;
                Status = "Operação cancelada.";
                Progress = 0;
                AppendLog("Cancelado pelo usuário.");
            }
            catch (InvalidPasswordException ex)
            {
                errorOccurred = true;
                Status = "Senha inválida.";
                ErrorText = "Senha inválida.";
                Progress = 0;
                AppendLog("--- DETALHES TÉCNICOS DO ERRO ---");
                AppendLog(ex.ToString());
                AppendLog("---------------------------------");
                AppendLog("Erro: Senha inválida.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                var detailed = ex.ToString();
                var msg = string.IsNullOrWhiteSpace(ex.Message) ? detailed : ex.Message;

                // Log the full exception details first for debugging
                AppendLog("--- DETALHES TÉCNICOS DO ERRO ---");
                AppendLog(ex.ToString());
                AppendLog("---------------------------------");

                Status = $"Erro: {msg}";
                ErrorText = msg;
                errorOccurred = true;
                Progress = 0;
                AppendLog($"Erro: {msg}");
            }
            finally
            {
                IsProcessing = false;
                if (Progress >= 99.9)
                    Progress = 100;
                OnPropertyChanged(nameof(CanRun));
            }
        }

        public void AppendLog(string message)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            LogText = string.IsNullOrEmpty(LogText) ? line : LogText + Environment.NewLine + line;
        }

        private void CoerceExportModeIfNeeded()
        {
            if (IsPublicOnlyCertificate)
            {
                if (ExportMode != ExportPemMode.PublicCertificateOnly)
                {
                    WarningText = "Arquivos .cer/.crt não possuem chave privada. Ajustamos o modo para exportar apenas o certificado público.";
                    ExportMode = ExportPemMode.PublicCertificateOnly;
                    AppendLog("Modo ajustado automaticamente para: Somente certificado público (PEM).");
                }
                return;
            }

            if (!CanExportPrivateKey && IsPrivateKeyModeSelected)
            {
                WarningText = "O arquivo selecionado não permite exportar chave privada. Ajustamos o modo para exportar apenas o certificado público.";
                ExportMode = ExportPemMode.PublicCertificateOnly;
                AppendLog("Modo ajustado automaticamente para: Somente certificado público (PEM).");
            }
        }

        private void EnsureOutputNamesInitialized()
        {
            if (string.IsNullOrWhiteSpace(_lastGeneratedBase))
                _lastGeneratedBase = "certificado";

            if (string.IsNullOrWhiteSpace(OutputBaseFileName))
                return;

            if (string.IsNullOrWhiteSpace(PublicPemFileName))
                PublicPemFileName = $"{OutputBaseFileName}.cert.pem";

            if (string.IsNullOrWhiteSpace(PrivatePemFileName))
                PrivatePemFileName = $"{OutputBaseFileName}.key.pem";
        }

        private void AutoGenerateOutputNamesIfNeeded(string oldBase, string newBase)
        {
            if (string.IsNullOrWhiteSpace(newBase))
                return;

            var oldGeneratedPub = $"{oldBase}.cert.pem";
            var oldGeneratedPriv = $"{oldBase}.key.pem";

            if (string.IsNullOrWhiteSpace(PublicPemFileName) || PublicPemFileName.Equals(oldGeneratedPub, StringComparison.OrdinalIgnoreCase))
                PublicPemFileName = $"{newBase}.cert.pem";

            if (string.IsNullOrWhiteSpace(PrivatePemFileName) || PrivatePemFileName.Equals(oldGeneratedPriv, StringComparison.OrdinalIgnoreCase))
                PrivatePemFileName = $"{newBase}.key.pem";

            _lastGeneratedBase = newBase;
            OnPropertyChanged(nameof(CombinedOutputFileNamePreview));
        }

        private string EffectivePublicFileName() => string.IsNullOrWhiteSpace(PublicPemFileName)
            ? $"{OutputBaseFileName}.cert.pem"
            : PublicPemFileName.Trim();

        private string EffectivePrivateFileName() => string.IsNullOrWhiteSpace(PrivatePemFileName)
            ? $"{OutputBaseFileName}.key.pem"
            : PrivatePemFileName.Trim();

        private bool DoesAnyTargetFileExist()
        {
            var folder = OutputFolder;
            if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(OutputBaseFileName))
                return false;

            switch (ExportMode)
            {
                case ExportPemMode.PublicCertificateOnly:
                    return File.Exists(Path.Combine(folder, EffectivePublicFileName()));
                case ExportPemMode.PrivateKeyOnly:
                    return File.Exists(Path.Combine(folder, EffectivePrivateFileName()));
                case ExportPemMode.SeparatePublicAndPrivatePem:
                    return File.Exists(Path.Combine(folder, EffectivePublicFileName())) ||
                           File.Exists(Path.Combine(folder, EffectivePrivateFileName()));
                case ExportPemMode.CombinedPemSingleFile:
                    return File.Exists(Path.Combine(folder, $"{OutputBaseFileName.Trim()}.pem"));
                default:
                    return false;
            }
        }

        public void ClearLog()
        {
            LogText = string.Empty;
            AppendLog("Log limpo.");
        }

        private bool CanStartExport()
        {
            return !string.IsNullOrWhiteSpace(InputCertificatePath)
                && IsSupportedInputType
                && (!IsPublicOnlyCertificate || ExportMode == ExportPemMode.PublicCertificateOnly)
                && IsPasswordValid
                && IsOutputFolderValid
                && IsOutputBaseFileNameValid
                && AreSplitFileNamesValid;
        }

        private void RefreshValidationMessages()
        {
            // Warning: .cer/.crt + private key mode choice
            if (IsPublicOnlyCertificate && ExportMode != ExportPemMode.PublicCertificateOnly)
            {
                WarningText = "Arquivos .cer/.crt permitem somente exportar o certificado público (sem chave privada).";
            }
            else
            {
                // Keep any existing warning from coercion unless it's purely validation noise
                if (WarningText == "Arquivos .cer/.crt permitem somente exportar o certificado público (sem chave privada).")
                    WarningText = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(InputCertificatePath))
            {
                ErrorText = "Selecione um arquivo de certificado.";
                return;
            }

            if (!IsSupportedInputType)
            {
                ErrorText = "Formato não suportado. Use .pfx, .p12, .cer ou .crt.";
                return;
            }

            if (IsPasswordRequired && string.IsNullOrWhiteSpace(Password))
            {
                ErrorText = "Informe a senha do certificado (.pfx/.p12).";
                return;
            }

            if (string.IsNullOrWhiteSpace(OutputFolder))
            {
                ErrorText = "Selecione a pasta de saída.";
                return;
            }

            if (!IsOutputBaseFileNameValid)
            {
                ErrorText = "Informe um nome base válido para os arquivos (sem caracteres inválidos).";
                return;
            }

            if (IsSplitOutputSelected && !AreSplitFileNamesValid)
            {
                ErrorText = "Revise os nomes dos arquivos .cert.pem e .key.pem (devem ser diferentes e válidos).";
                return;
            }

            ErrorText = string.Empty;
        }

        private string GetCertificateExtension()
        {
            try
            {
                return Path.GetExtension(InputCertificatePath)?.Trim().ToLowerInvariant() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetModeLabel(ExportPemMode mode) => mode switch
        {
            ExportPemMode.PrivateKeyOnly => "Somente chave privada (PEM)",
            ExportPemMode.PublicCertificateOnly => "Somente certificado público (PEM)",
            ExportPemMode.SeparatePublicAndPrivatePem => "Certificado + chave privada (2 arquivos PEM)",
            ExportPemMode.CombinedPemSingleFile => "Certificado + chave privada (1 arquivo PEM)",
            _ => "Desconhecido"
        };

        private void AddRecentInputFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            RemoveExisting(RecentInputFiles, path);
            RecentInputFiles.Insert(0, path);
            TrimToMax(RecentInputFiles, 10);
        }

        private void AddRecentOutputFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            RemoveExisting(RecentOutputFolders, path);
            RecentOutputFolders.Insert(0, path);
            TrimToMax(RecentOutputFolders, 10);
        }

        private static void RemoveExisting(ObservableCollection<string> list, string value)
        {
            for (var i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].Equals(value, StringComparison.OrdinalIgnoreCase))
                    list.RemoveAt(i);
            }
        }

        private static void TrimToMax(ObservableCollection<string> list, int maxItems)
        {
            if (maxItems <= 0)
                return;

            while (list.Count > maxItems)
                list.RemoveAt(list.Count - 1);
        }

        private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

