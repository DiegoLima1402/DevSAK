using System.Collections.Generic;

namespace DevSAK.Models
{
    public class MySqlBackupRestoreSettings
    {
        public string? LastSelectedConnectionId { get; set; }

        public string? LastSelectedDatabase { get; set; }

        public MySqlBackupRestoreMode LastMode { get; set; } = MySqlBackupRestoreMode.Backup;

        public string? LastSourceSqlFile { get; set; }

        public string? LastDestinationSqlFile { get; set; }

        public bool DisableForeignKeyChecks { get; set; }

        public bool RecreateDatabaseBeforeRestore { get; set; }

        public bool ClearLogEachOperation { get; set; }

        public bool CompressBackupToZip { get; set; }

        public List<string> RecentConnectionIds { get; set; } = new();

        public void RegisterRecentConnection(string? connectionId)
        {
            if (string.IsNullOrWhiteSpace(connectionId))
            {
                return;
            }

            RecentConnectionIds.RemoveAll(id => id == connectionId);
            RecentConnectionIds.Insert(0, connectionId);

            if (RecentConnectionIds.Count > 5)
            {
                RecentConnectionIds.RemoveAt(RecentConnectionIds.Count - 1);
            }
        }
    }
}
