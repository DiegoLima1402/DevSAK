namespace DevSAK.Models
{
    /// <summary>
    /// Combined progress payload for AppZip (percent, status line, current entry path).
    /// </summary>
    public readonly record struct AppZipCompressionProgress(
        double Percent,
        string Status,
        string? CurrentEntryRelativePath);

}
