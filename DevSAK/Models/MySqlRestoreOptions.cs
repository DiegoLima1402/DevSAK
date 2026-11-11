namespace DevSAK.Models
{
    public class MySqlRestoreOptions
    {
        [System.Text.Json.Serialization.JsonIgnore]
        public System.Collections.Generic.List<string>? SelectedFiles { get; set; }
        public bool IgnoreUse { get; set; } = true;
        public bool IgnoreDatabaseDdl { get; set; } = true;
        public bool CreateDatabase { get; set; }
        public bool RemoveDefiner { get; set; }
        public bool IgnoreGtid { get; set; }
        public bool IgnoreLocks { get; set; }
        public bool ContinueOnError { get; set; }
        public bool SaveReport { get; set; }
        public int EncodingIndex { get; set; }
        public string SourceDatabase { get; set; } = string.Empty;
        public bool RemapDatabase { get; set; }
    }
    public class MySqlRestoreFile
    {
        public string Path { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsIncluded { get; set; } = true;
    }
}
