using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevSAK.Models;
using DevSAK.Services;
using Microsoft.UI.Xaml.Media;

namespace DevSAK.ViewModels
{
    public sealed class ValidateCertificateViewModel : INotifyPropertyChanged
    {
        private readonly CertificateValidationService _validationService = new();
        
        private string _inputCertificatePath = string.Empty;
        private string _password = string.Empty;
        private bool _isProcessing;
        
        private CertificateValidationResult? _result;
        
        private string _errorText = string.Empty;
        private string _statusText = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string InputCertificatePath
        {
            get => _inputCertificatePath;
            set
            {
                if (_inputCertificatePath != value)
                {
                    _inputCertificatePath = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsPfxOrP12));
                    OnPropertyChanged(nameof(IsPasswordRequired));
                    OnPropertyChanged(nameof(CanRun));
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
                    OnPropertyChanged(nameof(CanRun));
                    RefreshValidationMessages();
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
                    OnPropertyChanged(nameof(CanRun));
                }
            }
        }

        public bool IsNotProcessing => !IsProcessing;

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

        public string StatusText
        {
            get => _statusText;
            set
            {
                if (_statusText != value)
                {
                    _statusText = value;
                    OnPropertyChanged();
                }
            }
        }

        public CertificateValidationResult? Result
        {
            get => _result;
            private set
            {
                _result = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasResult));
                
                // Trigger property updates for all bindings
                OnPropertyChanged(nameof(FileName));
                OnPropertyChanged(nameof(Subject));
                OnPropertyChanged(nameof(Issuer));
                OnPropertyChanged(nameof(NotBefore));
                OnPropertyChanged(nameof(NotAfter));
                OnPropertyChanged(nameof(DaysRemaining));
                OnPropertyChanged(nameof(IsExpiredText));
                OnPropertyChanged(nameof(Thumbprint));
                OnPropertyChanged(nameof(SerialNumber));
                OnPropertyChanged(nameof(SignatureAlgorithm));
                OnPropertyChanged(nameof(HasPrivateKeyText));
                OnPropertyChanged(nameof(KeyAlgorithm));
                OnPropertyChanged(nameof(KeySize));
                OnPropertyChanged(nameof(FriendlyName));
                OnPropertyChanged(nameof(SubjectAlternativeNames));
                OnPropertyChanged(nameof(BadgeColorBrush));
            }
        }

        public bool HasResult => Result != null;

        public string FileName => Result?.FileName ?? "-";
        public string Subject => Result?.Subject ?? "-";
        public string Issuer => Result?.Issuer ?? "-";
        public string NotBefore => Result?.NotBefore.ToString("dd/MM/yyyy HH:mm:ss") ?? "-";
        public string NotAfter => Result?.NotAfter.ToString("dd/MM/yyyy HH:mm:ss") ?? "-";
        public string DaysRemaining => Result != null ? $"{Result.DaysRemaining} dia(s)" : "-";
        public string IsExpiredText => Result == null ? "-" : (Result.IsExpired ? "Sim" : "Não");
        public string Thumbprint => Result?.Thumbprint ?? "-";
        public string SerialNumber => Result?.SerialNumber ?? "-";
        public string SignatureAlgorithm => Result?.SignatureAlgorithm ?? "-";
        public string HasPrivateKeyText => Result == null ? "-" : (Result.HasPrivateKey ? "Sim" : "Não");
        public string KeyAlgorithm => Result?.KeyAlgorithm ?? "-";
        public string KeySize => Result != null ? $"{Result.KeySize} bits" : "-";
        public string FriendlyName => Result?.FriendlyName ?? "-";
        public string SubjectAlternativeNames => Result != null && Result.SubjectAlternativeNames.Count > 0 
            ? string.Join(", ", Result.SubjectAlternativeNames) 
            : "-";

        public string BadgeColorBrush
        {
            get
            {
                if (Result == null) return "Transparent";
                if (Result.IsExpired) return "#C81D25"; // Red
                if (Result.DaysRemaining < 30) return "#E0A800"; // Yellow
                return "#009628"; // Green
            }
        }

        public bool IsPfxOrP12
        {
            get
            {
                var ext = Path.GetExtension(InputCertificatePath)?.Trim().ToLowerInvariant() ?? string.Empty;
                return ext is ".pfx" or ".p12";
            }
        }

        public bool IsSupportedInputType
        {
            get
            {
                var ext = Path.GetExtension(InputCertificatePath)?.Trim().ToLowerInvariant() ?? string.Empty;
                return ext is ".pfx" or ".p12" or ".cer" or ".crt";
            }
        }

        public bool IsPasswordRequired => IsPfxOrP12;
        public bool IsPasswordValid => !IsPasswordRequired || !string.IsNullOrWhiteSpace(Password);

        public bool CanRun => IsNotProcessing && !string.IsNullOrWhiteSpace(InputCertificatePath) && IsSupportedInputType && IsPasswordValid;

        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            if (IsProcessing) return;

            try
            {
                RefreshValidationMessages();
                if (!CanRun) return;

                IsProcessing = true;
                ErrorText = string.Empty;
                StatusText = "Validando certificado...";
                Result = null;

                Result = await _validationService.ValidateAsync(InputCertificatePath, Password, cancellationToken);
                
                StatusText = "Validação concluída com sucesso.";
            }
            catch (OperationCanceledException)
            {
                StatusText = "Operação cancelada.";
            }
            catch (InvalidPasswordException)
            {
                StatusText = "Senha inválida.";
                ErrorText = "Senha inválida.";
            }
            catch (Exception ex)
            {
                StatusText = "Erro na validação.";
                ErrorText = ex.Message;
            }
            finally
            {
                IsProcessing = false;
            }
        }

        public void Clear()
        {
            InputCertificatePath = string.Empty;
            Password = string.Empty;
            Result = null;
            ErrorText = string.Empty;
            StatusText = string.Empty;
        }

        public string GetSummary()
        {
            if (Result == null) return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine("=== RESUMO DO CERTIFICADO ===");
            sb.AppendLine($"Arquivo: {FileName}");
            sb.AppendLine($"Subject (Emitido para): {Subject}");
            sb.AppendLine($"Issuer (Emitido por): {Issuer}");
            sb.AppendLine($"Válido de: {NotBefore}");
            sb.AppendLine($"Válido até: {NotAfter}");
            sb.AppendLine($"Dias restantes: {DaysRemaining}");
            sb.AppendLine($"Expirado: {IsExpiredText}");
            sb.AppendLine($"Thumbprint SHA1: {Thumbprint}");
            sb.AppendLine($"Serial Number: {SerialNumber}");
            sb.AppendLine($"Algoritmo da assinatura: {SignatureAlgorithm}");
            sb.AppendLine($"Possui chave privada: {HasPrivateKeyText}");
            sb.AppendLine($"Tipo da chave: {KeyAlgorithm}");
            sb.AppendLine($"Tamanho da chave: {KeySize}");
            sb.AppendLine($"Friendly Name: {FriendlyName}");
            sb.AppendLine($"SAN / DNS Names: {SubjectAlternativeNames}");
            sb.AppendLine("=============================");

            return sb.ToString();
        }

        private void RefreshValidationMessages()
        {
            if (string.IsNullOrWhiteSpace(InputCertificatePath))
            {
                ErrorText = string.Empty;
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

            ErrorText = string.Empty;
        }

        private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
