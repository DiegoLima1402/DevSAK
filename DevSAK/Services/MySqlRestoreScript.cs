using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using DevSAK.Models;
namespace DevSAK.Services
{
    public static class MySqlRestoreScript
    {
        public static Encoding GetEncoding(int index) => index switch { 0 => new UTF8Encoding(false, true), 1 => Encoding.Latin1, 2 => Encoding.Unicode, _ => throw new InvalidOperationException("Codificação inválida.") };
        public static void Process(string source, string destination, MySqlRestoreOptions options, string target, bool disableFk, Action<string> report, CancellationToken token)
        {
            using var reader = new StreamReader(source, GetEncoding(options.EncodingIndex), true);
            using var writer = new StreamWriter(destination, false, new UTF8Encoding(false));
            var sql = new StringBuilder();
            string delimiter = ";";
            char quote = '\0'; bool block = false;
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                token.ThrowIfCancellationRequested();
                if (quote == '\0' && !block && string.IsNullOrWhiteSpace(sql.ToString()) && line.TrimStart().StartsWith("DELIMITER ", StringComparison.OrdinalIgnoreCase))
                { delimiter = line.Trim()[10..].Trim(); if (delimiter.Length == 0) throw new InvalidOperationException("DELIMITER vazio."); writer.WriteLine(line); continue; }
                for (int i = 0; i < line.Length; i++)
                {
                    char c = line[i];
                    if (quote != '\0')
                    {
                        sql.Append(c);
                        if (c == '\\' && quote != '`' && i + 1 < line.Length) sql.Append(line[++i]);
                        else if (c == quote) { if (i + 1 < line.Length && line[i + 1] == quote) sql.Append(line[++i]); else quote = '\0'; }
                        continue;
                    }
                    if (block) { sql.Append(c); if (c == '*' && i + 1 < line.Length && line[i + 1] == '/') { sql.Append(line[++i]); block = false; } continue; }
                    if (c == '#' || (c == '-' && i + 2 < line.Length && line[i + 1] == '-' && char.IsWhiteSpace(line[i + 2]))) { sql.Append(line[i..]); break; }
                    if (c == '/' && i + 1 < line.Length && line[i + 1] == '*') { sql.Append("/*"); i++; block = true; continue; }
                    if (c is '\'' or '"' or '`') { quote = c; sql.Append(c); continue; }
                    if (line.AsSpan(i).StartsWith(delimiter)) { writer.Write(Transform(sql.ToString(), options, target, disableFk, report)); writer.Write(delimiter); sql.Clear(); i += delimiter.Length - 1; }
                    else sql.Append(c);
                }
                sql.Append('\n');
                if (quote == '\0' && !block && Regex.IsMatch(sql.ToString(), @"^\s*(?:--[^\n]*|#[^\n]*|/\*(?!\!)[\s\S]*?\*/)?\s*$")) { writer.Write(sql); sql.Clear(); }
            }
            if (quote != '\0' || block) throw new InvalidOperationException("String ou comentário não finalizado no SQL.");
            writer.Write(Transform(sql.ToString(), options, target, disableFk, report));
        }
        private static string Transform(string sql, MySqlRestoreOptions o, string target, bool disableFk, Action<string> report)
        {
            var tokens = Regex.Matches(sql, @"/\*!\d*|\*/|/\*(?!\!)[\s\S]*?\*/|--(?=\s)[^\n]*|#[^\n]*|'(?:\\.|''|[^'\\])*'|""(?:\\.|""""|[^""\\])*""|`(?:``|[^`])*`|[A-Za-z_$][A-Za-z0-9_$]*|[^\s]").Cast<Match>().Where(m => !m.Value.StartsWith("/*") && m.Value != "*/" && !m.Value.StartsWith("--") && !m.Value.StartsWith("#")).ToList();
            var words = tokens.Select(t => t.Value.ToUpperInvariant()).ToArray();
            if (words.Length == 0) return sql;
            bool Starts(params string[] p) => words.Take(p.Length).SequenceEqual(p);
            if (Starts("USE")) { report("USE encontrado: " + (tokens.Count > 1 ? tokens[1].Value : "?")); if (o.IgnoreUse) return "\n"; }
            if (Starts("CREATE", "DATABASE") || Starts("DROP", "DATABASE") || Starts("CREATE", "SCHEMA") || Starts("DROP", "SCHEMA")) { report("DDL de banco encontrado"); if (o.IgnoreDatabaseDdl) return "\n"; }
            if (Starts("LOCK", "TABLES") || Starts("UNLOCK", "TABLES")) { report("Bloqueio encontrado"); if (o.IgnoreLocks) return "\n"; }
            if (Starts("DROP", "TABLE") || Starts("TRUNCATE")) report("Exclusão de tabela/dados encontrada");
            if (words[0] == "SET" && words.Any(w => w.Contains("GTID_PURGED"))) { report("GTID encontrado"); if (o.IgnoreGtid) return "\n"; }
            if (disableFk && words[0] == "SET" && words.Any(w => w.Contains("FOREIGN_KEY_CHECKS"))) return "\n";
            if (words[0] == "SET" && words.Any(w => w.Contains("NO_BACKSLASH_ESCAPES")))
                throw new InvalidOperationException("O modo NO_BACKSLASH_ESCAPES exige revisão manual antes do tratamento do script.");
            var changes = new List<(int Start, int Length, string Text)>();
            for (int i = 0; i < tokens.Count; i++)
            {
                if (words[i] == "DEFINER" && i + 4 < tokens.Count && words[i + 1] == "=" && words[i + 3] == "@") { report("DEFINER encontrado"); if (o.RemoveDefiner) changes.Add((tokens[i].Index, tokens[i + 4].Index + tokens[i + 4].Length - tokens[i].Index, "")); }
                var name = tokens[i].Value.Trim('`').Replace("``", "`");
                if (i + 1 < tokens.Count && words[i + 1] == "." && !tokens[i].Value.StartsWith("'") && !tokens[i].Value.StartsWith("\""))
                { report("Referência qualificada: " + name); if (o.RemapDatabase && name == o.SourceDatabase) changes.Add((tokens[i].Index, tokens[i].Length, "`" + target.Replace("`", "``") + "`")); }
            }
            foreach (var r in changes.OrderByDescending(r => r.Start)) sql = sql.Remove(r.Start, r.Length).Insert(r.Start, r.Text);
            return sql;
        }
    }
}
