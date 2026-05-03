namespace DevSAK.Models
{
    public readonly record struct CertificateExportProgress(
        double Percent,
        string Status);
}

