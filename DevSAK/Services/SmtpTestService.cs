using System;
using System.Threading;
using System.Threading.Tasks;
using DevSAK.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace DevSAK.Services
{
    public sealed class SmtpTestService
    {
        public async Task SendTestEmailAsync(
            SmtpTestSettings settings,
            IProgress<SmtpTestProgress>? progress,
            CancellationToken cancellationToken)
        {
            var message = BuildMessage(settings);
            var socketOptions = ResolveSocketOptions(settings);
            var port = checked((int)Math.Round(settings.Port));

            progress?.Report(SmtpTestProgress.CreateStatus("Preparando cliente SMTP...", 10));
            progress?.Report(SmtpTestProgress.Log($"Modo de segurança: {GetSecurityModeLabel(socketOptions)}"));

            using var client = new SmtpClient
            {
                Timeout = 30000
            };

            client.ServerCertificateValidationCallback = (_, certificate, chain, errors) =>
            {
                if (errors == System.Net.Security.SslPolicyErrors.None)
                {
                    progress?.Report(SmtpTestProgress.Log("Certificado TLS validado com sucesso."));
                    return true;
                }

                progress?.Report(SmtpTestProgress.Log($"Falha na validação TLS: {errors}"));
                return false;
            };

            progress?.Report(SmtpTestProgress.CreateStatus("Conectando ao servidor SMTP...", 20));
            progress?.Report(SmtpTestProgress.Log($"Conectando em {settings.SmtpServer}:{port}..."));
            await client.ConnectAsync(settings.SmtpServer, port, socketOptions, cancellationToken).ConfigureAwait(false);

            progress?.Report(SmtpTestProgress.CreateStatus("Conectado ao servidor SMTP.", 45));
            progress?.Report(SmtpTestProgress.Log($"Conexão estabelecida. TLS ativo: {FormatBool(client.IsSecure)}"));

            if (settings.AuthenticationEnabled && settings.UseUserCredentials)
            {
                progress?.Report(SmtpTestProgress.CreateStatus("Autenticando no servidor SMTP...", 55));
                progress?.Report(SmtpTestProgress.Log($"Autenticando usuário: {settings.UserName}"));
                await client.AuthenticateAsync(settings.UserName, settings.Password, cancellationToken).ConfigureAwait(false);
                progress?.Report(SmtpTestProgress.Log("Autenticação concluída com sucesso."));
            }
            else if (settings.AuthenticationEnabled)
            {
                progress?.Report(SmtpTestProgress.Log("Autenticação habilitada, mas a opção de usar credenciais do usuário está desmarcada. Nenhuma credencial foi enviada."));
            }
            else
            {
                progress?.Report(SmtpTestProgress.Log("Autenticação desabilitada. Enviando sem credenciais."));
            }

            progress?.Report(SmtpTestProgress.CreateStatus("Enviando email de teste...", 75));
            await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
            progress?.Report(SmtpTestProgress.Log("Email de teste enviado com sucesso."));

            progress?.Report(SmtpTestProgress.CreateStatus("Encerrando conexão SMTP...", 90));
            await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);
            progress?.Report(SmtpTestProgress.Log("Conexão SMTP encerrada."));

            progress?.Report(SmtpTestProgress.CreateStatus("Teste SMTP concluído com sucesso.", 100));
        }

        private static MimeMessage BuildMessage(SmtpTestSettings settings)
        {
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(settings.SenderEmail));
            message.To.Add(MailboxAddress.Parse(settings.RecipientEmail));
            message.Subject = settings.Subject;

            message.Body = new BodyBuilder
            {
                TextBody = settings.Message
            }.ToMessageBody();

            return message;
        }

        private static SecureSocketOptions ResolveSocketOptions(SmtpTestSettings settings)
        {
            if (settings.UseSsl)
            {
                return SecureSocketOptions.SslOnConnect;
            }

            if (settings.UseStartTls)
            {
                return SecureSocketOptions.StartTls;
            }

            return SecureSocketOptions.None;
        }

        private static string GetSecurityModeLabel(SecureSocketOptions options)
        {
            return options switch
            {
                SecureSocketOptions.SslOnConnect => "SSL/TLS direto",
                SecureSocketOptions.StartTls => "STARTTLS obrigatório",
                SecureSocketOptions.None => "Sem TLS",
                _ => options.ToString()
            };
        }

        private static string FormatBool(bool value)
            => value ? "Sim" : "Não";
    }

    public sealed record SmtpTestProgress(string? Status, double? Progress, string? LogLine)
    {
        public static SmtpTestProgress CreateStatus(string status, double progress)
            => new(status, progress, null);

        public static SmtpTestProgress Log(string line)
            => new(null, null, line);
    }
}
