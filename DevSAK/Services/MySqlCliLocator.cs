using System;
using System.IO;

namespace DevSAK.Services
{
    public class MySqlCliLocator
    {
        private static readonly string BundledDirectory = Path.Combine(AppContext.BaseDirectory, "Tools", "MySql");

        public MySqlCliPaths Locate()
        {
            var mysql = Path.Combine(BundledDirectory, "mysql.exe");
            var mysqldump = Path.Combine(BundledDirectory, "mysqldump.exe");
            var mysqlpump = Path.Combine(BundledDirectory, "mysqlpump.exe");

            return new MySqlCliPaths(
                File.Exists(mysql) ? mysql : null,
                File.Exists(mysqldump) ? mysqldump : null,
                File.Exists(mysqlpump) ? mysqlpump : null);
        }

        public bool HasRequiredFiles()
        {
            var paths = Locate();
            return paths.MysqlPath is not null && paths.MysqldumpPath is not null;
        }

        public string GetMissingFilesWarning()
        {
            return @"Arquivos do MySQL não encontrados em Tools\MySql";
        }
    }

    public sealed record MySqlCliPaths(string? MysqlPath, string? MysqldumpPath, string? MysqlPumpPath)
    {
        public string? PreferredBackupPath => MysqldumpPath ?? MysqlPumpPath;

        public string PreferredBackupToolName => MysqldumpPath is not null ? "mysqldump.exe" : "mysqlpump.exe";
    }
}
