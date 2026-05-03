using System.Text.Json.Serialization;

namespace DevSAK.Models
{
    public sealed class SmtpTestSettings
    {
        public string SmtpServer { get; set; } = string.Empty;

        public double Port { get; set; } = 587;

        public string UserName { get; set; } = string.Empty;

        public string EncryptedPassword { get; set; } = string.Empty;

        [JsonIgnore]
        public string Password { get; set; } = string.Empty;

        public string SenderEmail { get; set; } = string.Empty;

        public string RecipientEmail { get; set; } = string.Empty;

        public string Subject { get; set; } = "Teste SMTP - DevSAK";

        public string Message { get; set; } = "Mensagem de teste enviada pelo DevSAK.";

        public bool UseSsl { get; set; }

        public bool UseStartTls { get; set; } = true;

        public bool AuthenticationEnabled { get; set; } = true;

        public bool UseUserCredentials { get; set; } = true;
    }
}
