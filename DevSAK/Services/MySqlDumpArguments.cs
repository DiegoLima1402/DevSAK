using System;
using System.Collections.Generic;
using DevSAK.Models;

namespace DevSAK.Services
{
    public static class MySqlDumpArguments
    {
        public static string Build(string defaultsFile, string database, string outputFile, MySqlBackupOptions options, IReadOnlyList<string>? excludedTables = null)
        {
            if (options.ContentIndex is < 0 or > 2 || options.GtidIndex is < 0 or > 2)
                throw new ArgumentException("Opção de backup inválida.");
            var args = new List<string>
            {
                "--defaults-extra-file=" + Quote(defaultsFile),
                "--single-transaction", "--hex-blob", "--verbose",
                "--result-file=" + Quote(outputFile),
                options.IncludeRoutines ? "--routines" : "--skip-routines",
                options.IncludeEvents ? "--events" : "--skip-events",
                options.IncludeTriggers ? "--triggers" : "--skip-triggers",
                options.DropTables && options.ContentIndex != 2 ? "--add-drop-table" : "--skip-add-drop-table",
                options.AddLocks ? "--add-locks" : "--skip-add-locks",
                options.CompleteInsert ? "--complete-insert" : "--skip-complete-insert",
                options.ExtendedInsert ? "--extended-insert" : "--skip-extended-insert",
                "--set-gtid-purged=" + new[] { "AUTO", "ON", "OFF" }[options.GtidIndex]
            };
            if (!options.IncludeTablespaces) args.Add("--no-tablespaces");
            if (options.ContentIndex == 1) args.Add("--no-data");
            if (options.ContentIndex == 2) args.Add("--no-create-info");
            foreach (var table in excludedTables ?? Array.Empty<string>())
                args.Add("--ignore-table=" + Quote(database + "." + table));
            if (options.IncludeDatabase) args.Add("--databases");
            args.Add(Quote(database));
            return string.Join(" ", args);
        }

        private static string Quote(string value)
        {
            var result = new System.Text.StringBuilder("\"");
            var slashes = 0;
            foreach (var character in value)
            {
                if (character == '\\') { slashes++; continue; }
                result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
                result.Append(character);
                slashes = 0;
            }
            result.Append('\\', slashes * 2);
            return result.Append('"').ToString();
        }
    }
}
