using System;
using System.Text.Json.Serialization;

namespace DevSAK.Models
{
    public class MySqlConnectionProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public string FriendlyName { get; set; } = string.Empty;

        public string Host { get; set; } = "localhost";

        public int Port { get; set; } = 3306;

        public string UserName { get; set; } = string.Empty;

        public string EncryptedPassword { get; set; } = string.Empty;

        public string? DefaultDatabase { get; set; }

        public bool UseSsl { get; set; }

        public DateTimeOffset? LastSuccessfulTestUtc { get; set; }

        [JsonIgnore]
        public string Password { get; set; } = string.Empty;

        public MySqlConnectionProfile Clone()
        {
            return new MySqlConnectionProfile
            {
                Id = Id,
                FriendlyName = FriendlyName,
                Host = Host,
                Port = Port,
                UserName = UserName,
                EncryptedPassword = EncryptedPassword,
                DefaultDatabase = DefaultDatabase,
                UseSsl = UseSsl,
                LastSuccessfulTestUtc = LastSuccessfulTestUtc,
                Password = Password
            };
        }

        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(FriendlyName)
                ? $"{UserName}@{Host}:{Port}"
                : FriendlyName;
        }
    }
}
