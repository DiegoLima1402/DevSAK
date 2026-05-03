using System;
using System.Collections.Generic;

namespace DevSAK.Models
{
    public sealed class CertificateValidationResult
    {
        public string FileName { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Issuer { get; set; } = string.Empty;
        public DateTime NotBefore { get; set; }
        public DateTime NotAfter { get; set; }
        public int DaysRemaining { get; set; }
        public bool IsExpired { get; set; }
        public string Thumbprint { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public string SignatureAlgorithm { get; set; } = string.Empty;
        public bool HasPrivateKey { get; set; }
        public string KeyAlgorithm { get; set; } = string.Empty;
        public int KeySize { get; set; }
        public string FriendlyName { get; set; } = string.Empty;
        public List<string> SubjectAlternativeNames { get; set; } = new();
    }
}
