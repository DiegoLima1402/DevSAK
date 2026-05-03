namespace DevSAK.Models
{
    public enum ExportPemMode
    {
        PublicCertificateOnly = 0,
        PrivateKeyOnly = 1,
        SeparatePublicAndPrivatePem = 2,
        CombinedPemSingleFile = 3
    }
}

