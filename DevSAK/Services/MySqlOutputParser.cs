using System;
using System.Text.RegularExpressions;
using DevSAK.Models;

namespace DevSAK.Services
{
    public class MySqlOutputParser
    {
        private static readonly Regex DumpingTableRegex = new(@"table\s+[`'""]?(?<table>[^`'""]+)[`'""]?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public MySqlParsedOutput Parse(string line, MySqlBackupRestoreMode mode)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return new MySqlParsedOutput(null, false, false, false, null);
            }

            var normalized = line.Trim();
            var lower = normalized.ToLowerInvariant();
            var tableName = TryExtractTableName(normalized);

            if (lower.Contains("error") || lower.Contains("fatal"))
            {
                return new MySqlParsedOutput(normalized, true, false, false, tableName);
            }

            if (lower.Contains("warning"))
            {
                return new MySqlParsedOutput(normalized, false, true, false, tableName);
            }

            if (mode == MySqlBackupRestoreMode.Backup)
            {
                if (lower.Contains("dumping") || lower.Contains("retrieving table structure") || lower.Contains("writing row"))
                {
                    return new MySqlParsedOutput($"Exportando: {normalized}", false, false, true, tableName);
                }
            }
            else
            {
                if (lower.Contains("source") || lower.Contains("query ok") || lower.Contains("warnings"))
                {
                    return new MySqlParsedOutput($"Importando: {normalized}", false, false, false, tableName);
                }
            }

            return new MySqlParsedOutput(normalized, false, false, false, tableName);
        }

        private static string? TryExtractTableName(string line)
        {
            var match = DumpingTableRegex.Match(line);
            return match.Success ? match.Groups["table"].Value.Trim() : null;
        }
    }

    public sealed record MySqlParsedOutput(string? Status, bool IsError, bool IsWarning, bool CountsAsProgressUnit, string? TableName);
}
