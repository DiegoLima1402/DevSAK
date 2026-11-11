namespace DevSAK.Models
{
    public class MySqlBackupOptions
    {
        public bool IncludeDatabase { get; set; }
        public int ContentIndex { get; set; }
        public bool IncludeRoutines { get; set; } = true;
        public bool IncludeEvents { get; set; } = true;
        public bool IncludeTriggers { get; set; } = true;
        public bool DropTables { get; set; } = true;
        public bool AddLocks { get; set; } = true;
        public bool CompleteInsert { get; set; }
        public bool ExtendedInsert { get; set; } = true;
        public bool IncludeTablespaces { get; set; } = true;
        public int GtidIndex { get; set; }
    }
    public class MySqlBackupTable
    {
        public string Name { get; set; } = string.Empty;
        public bool IsIncluded { get; set; } = true;
    }
}
