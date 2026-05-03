namespace DevSAK.Models
{
    /// <summary>
    /// A file selected for ZIP packaging with its uncompressed size (for progress).
    /// </summary>
    public readonly record struct ZipCandidateFile(string FullPath, long SizeBytes);

}
