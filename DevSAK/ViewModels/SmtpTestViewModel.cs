using System;
using System.ComponentModel;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevSAK.Models;
using DevSAK.Services;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace DevSAK.ViewModels
{
    public sealed class SmtpTestViewModel : INotifyPropertyChanged
    {
        private readonly StringBuilder _logBuilder = new();
        private readonly SmtpTestSettingsStore _settingsStore = new();
        private readonly SmtpTestService _smtpTestService = new();

        private string _smtpServer = string.Empty;
        private double _port = 587;
        private string _userName = string.Empty;
        private string _password = string.Empty;
        private string _senderEmail = string.Empty;
        private string _recipientEmail = string.Empty;
        private string _subject = "Teste SMTP - DevSAK";
        private string _message = "Mensagem de teste enviada pelo DevSAK.";
        private bool _useSsl;
        private bool _useStartTls = true;
        private bool _authenticationEnabled = true;
        private bool _useUserCredentials = true;
        private bool _autoScrollEnabled = true;
        private bool _isBusy;
        private double _progress;
        private string _statusText = "Pronto para testar";
        private string _logText = string.Empty;
        private bool _isLoadingSettings;
        private bool _isInitialized;
        private CancellationTokenSource? _saveDebounceCts;
        private CancellationTokenSource? _testCts;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string SmtpServer
        {
            get => _smtpServer;
            set => SetProperty(ref _smtpServer, value);
        }

        public double Port
        {
            get => _port;
            set => SetProperty(ref _port, value);
        }

        public string UserName
        {
            get => _userName;
            set => SetProperty(ref _userName, value);
        }

        public string Password
        {
            get => _password;
            set => SetProperty(ref _password, value);
        }

        public string SenderEmail
        {
            get => _senderEmail;
            set => SetProperty(ref _senderEmail, value);
        }

        public string RecipientEmail
        {
            get => _recipientEmail;
            set => SetProperty(ref _recipientEmail, value);
        }

        public string Subject
        {
            get => _subject;
            set => SetProperty(ref _subject, value);
        }

        public string Message
        {
            get => _message;
            set => SetProperty(ref _message, value);
        }

        public bool UseSsl
        {
            get => _useSsl;
            set => SetProperty(ref _useSsl, value);
        }

        public bool UseStartTls
        {
            get => _useStartTls;
            set => SetProperty(ref _useStartTls, value);
        }

        public bool AuthenticationEnabled
        {
            get => _authenticationEnabled;
            set => SetProperty(ref _authenticationEnabled, value);
        }

        public bool UseUserCredentials
        {
            get => _useUserCredentials;
            set => SetProperty(ref _useUserCredentials, value);
        }

        public bool AutoScrollEnabled
        {
            get => _autoScrollEnabled;
            set => SetProperty(ref _autoScrollEnabled, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    OnPropertyChanged(nameof(CanTest));
                    OnPropertyChanged(nameof(CanCancel));
                }
            }
        }

        public bool CanTest => !IsBusy;

        public bool CanCancel => IsBusy;

        public double Progress
        {
            get => _progress;
            private set
            {
                if (SetProperty(ref _progress, value))
                {
                    OnPropertyChanged(nameof(ProgressText));
                }
            }
        }

        public string ProgressText => $"{Math.Round(Progress)}%";

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public string LogText
        {
            get => _logText;
            private set => SetProperty(ref _logText, value);
        }

        public async Task InitializeAsync()
        {
            if (_isInitialized)
            {
                return;
            }

            _isInitialized = true;
            _isLoadingSettings = true;

            try
            {
                var settings = await _settingsStore.LoadAsync();
                ApplySettings(settings);
                StatusText = "Configurações carregadas";
                AppendLog("Últimos valores SMTP carregados.");
            }
            finally
            {
                _isLoadingSettings = false;
            }
        }

        public async Task TestSmtpAsync()
        {
            if (IsBusy)
            {
                return;
            }

            try
            {
                ValidateBeforeTest();
                await SaveNowAsync();

                _testCts?.Dispose();
                _testCts = new CancellationTokenSource();

                IsBusy = true;
                Progress = 0;
                StatusText = "Preparando teste SMTP...";
                AppendLog("Teste SMTP solicitado.");
                AppendLog($"Servidor: {DisplayValue(SmtpServer)} | Porta: {Port:N0}");
                AppendLog($"Remetente: {DisplayValue(SenderEmail)} | Destinatário: {DisplayValue(RecipientEmail)}");
                AppendLog($"SSL: {FormatBool(UseSsl)} | STARTTLS: {FormatBool(UseStartTls)} | Autenticação: {FormatBool(AuthenticationEnabled)}");

                var progress = new Progress<SmtpTestProgress>(HandleSmtpProgress);
                await _smtpTestService.SendTestEmailAsync(CreateSettingsSnapshot(), progress, _testCts.Token);

                Progress = 100;
                StatusText = "Teste SMTP concluído com sucesso.";
                AppendLog("Teste SMTP concluído com sucesso.");
                await SaveNowAsync();
            }
            catch (OperationCanceledException)
            {
                Progress = 0;
                StatusText = "Teste SMTP cancelado.";
                AppendLog("Operação cancelada pelo usuário.");
            }
            catch (Exception ex)
            {
                Progress = 0;
                StatusText = GetFriendlyErrorMessage(ex);
                AppendLog($"Falha no teste SMTP: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
                _testCts?.Dispose();
                _testCts = null;
            }
        }

        public void CancelTest()
        {
            _testCts?.Cancel();
            StatusText = "Cancelando teste SMTP...";
            AppendLog("Cancelamento solicitado.");
        }

        public void ClearFields()
        {
            SmtpServer = string.Empty;
            Port = 587;
            UserName = string.Empty;
            Password = string.Empty;
            SenderEmail = string.Empty;
            RecipientEmail = string.Empty;
            Subject = "Teste SMTP - DevSAK";
            Message = "Mensagem de teste enviada pelo DevSAK.";
            UseSsl = false;
            UseStartTls = true;
            AuthenticationEnabled = true;
            UseUserCredentials = true;
            Progress = 0;
            StatusText = "Campos limpos";
            AppendLog("Campos do formulário limpos.");
        }

        public void AppendLog(string message)
        {
            _logBuilder
                .Append('[')
                .Append(DateTime.Now.ToString("HH:mm:ss"))
                .Append("] ")
                .AppendLine(message);

            LogText = _logBuilder.ToString();
        }

        public void ClearLog()
        {
            _logBuilder.Clear();
            LogText = string.Empty;
            StatusText = "Log limpo";
        }

        public async Task SaveNowAsync()
        {
            _saveDebounceCts?.Cancel();
            _saveDebounceCts?.Dispose();
            _saveDebounceCts = null;

            if (!_isLoadingSettings)
            {
                await _settingsStore.SaveAsync(CreateSettingsSnapshot());
            }
        }

        private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);

            if (IsPersistedProperty(propertyName))
            {
                QueueSave();
            }

            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private static string DisplayValue(string value)
            => string.IsNullOrWhiteSpace(value) ? "(não informado)" : value.Trim();

        private static string FormatBool(bool value)
            => value ? "Sim" : "Não";

        private void ValidateBeforeTest()
        {
            if (string.IsNullOrWhiteSpace(SmtpServer))
            {
                throw new InvalidOperationException("Informe o servidor SMTP.");
            }

            if (Port is < 1 or > 65535)
            {
                throw new InvalidOperationException("Informe uma porta SMTP válida entre 1 e 65535.");
            }

            if (string.IsNullOrWhiteSpace(SenderEmail))
            {
                throw new InvalidOperationException("Informe o email do remetente.");
            }

            if (string.IsNullOrWhiteSpace(RecipientEmail))
            {
                throw new InvalidOperationException("Informe o email do destinatário.");
            }

            if (AuthenticationEnabled && UseUserCredentials)
            {
                if (string.IsNullOrWhiteSpace(UserName))
                {
                    throw new InvalidOperationException("Informe o usuário para autenticação SMTP.");
                }

                if (string.IsNullOrWhiteSpace(Password))
                {
                    throw new InvalidOperationException("Informe a senha para autenticação SMTP.");
                }
            }
        }

        private void HandleSmtpProgress(SmtpTestProgress progress)
        {
            if (!string.IsNullOrWhiteSpace(progress.LogLine))
            {
                AppendLog(progress.LogLine);
            }

            if (!string.IsNullOrWhiteSpace(progress.Status))
            {
                StatusText = progress.Status;
            }

            if (progress.Progress.HasValue)
            {
                Progress = Math.Max(0, Math.Min(100, progress.Progress.Value));
            }
        }

        private static string GetFriendlyErrorMessage(Exception exception)
        {
            return exception switch
            {
                InvalidOperationException => exception.Message,
                MimeKit.ParseException => "Email do remetente ou destinatário inválido.",
                FormatException => "Email do remetente ou destinatário inválido.",
                System.Security.Authentication.AuthenticationException => "Falha de TLS/SSL ao conectar no servidor SMTP.",
                SslHandshakeException => "Falha no handshake TLS/SSL com o servidor SMTP.",
                MailKit.Security.AuthenticationException => "Falha na autenticação SMTP. Verifique usuário e senha.",
                SmtpCommandException smtpCommandException => $"Servidor SMTP recusou a operação: {smtpCommandException.Message}",
                SmtpProtocolException => "Falha no protocolo SMTP durante a comunicação com o servidor.",
                SocketException => "Não foi possível conectar ao servidor SMTP. Verifique host, porta e firewall.",
                TimeoutException => "Tempo limite excedido ao comunicar com o servidor SMTP.",
                OperationCanceledException => "Teste SMTP cancelado.",
                _ => $"Falha no teste SMTP: {exception.Message}"
            };
        }

        private void ApplySettings(SmtpTestSettings settings)
        {
            SmtpServer = settings.SmtpServer;
            Port = settings.Port <= 0 ? 587 : settings.Port;
            UserName = settings.UserName;
            Password = settings.Password;
            SenderEmail = settings.SenderEmail;
            RecipientEmail = settings.RecipientEmail;
            Subject = string.IsNullOrWhiteSpace(settings.Subject) ? "Teste SMTP - DevSAK" : settings.Subject;
            Message = string.IsNullOrWhiteSpace(settings.Message) ? "Mensagem de teste enviada pelo DevSAK." : settings.Message;
            UseSsl = settings.UseSsl;
            UseStartTls = settings.UseStartTls;
            AuthenticationEnabled = settings.AuthenticationEnabled;
            UseUserCredentials = settings.UseUserCredentials;
        }

        private SmtpTestSettings CreateSettingsSnapshot()
        {
            return new SmtpTestSettings
            {
                SmtpServer = SmtpServer,
                Port = Port,
                UserName = UserName,
                Password = Password,
                SenderEmail = SenderEmail,
                RecipientEmail = RecipientEmail,
                Subject = Subject,
                Message = Message,
                UseSsl = UseSsl,
                UseStartTls = UseStartTls,
                AuthenticationEnabled = AuthenticationEnabled,
                UseUserCredentials = UseUserCredentials
            };
        }

        private void QueueSave()
        {
            if (_isLoadingSettings)
            {
                return;
            }

            _saveDebounceCts?.Cancel();
            _saveDebounceCts?.Dispose();
            _saveDebounceCts = new CancellationTokenSource();
            var token = _saveDebounceCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(350, token);
                    await _settingsStore.SaveAsync(CreateSettingsSnapshot());
                }
                catch (OperationCanceledException)
                {
                }
                catch
                {
                }
            }, token);
        }

        private static bool IsPersistedProperty(string? propertyName)
        {
            return propertyName is
                nameof(SmtpServer) or
                nameof(Port) or
                nameof(UserName) or
                nameof(Password) or
                nameof(SenderEmail) or
                nameof(RecipientEmail) or
                nameof(Subject) or
                nameof(Message) or
                nameof(UseSsl) or
                nameof(UseStartTls) or
                nameof(AuthenticationEnabled) or
                nameof(UseUserCredentials);
        }
    }
}
