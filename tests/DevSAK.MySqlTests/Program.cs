using DevSAK.Models;
using DevSAK.Services;
using System.Diagnostics;
using System.Text.Json;

void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
var cli = Path.GetFullPath("DevSAK/Tools/MySql/mysqldump.exe");
for (int content = 0; content < 3; content++)
for (int gtid = 0; gtid < 3; gtid++)
foreach (bool includeDb in new[] { false, true })
foreach (bool enabled in new[] { false, true })
{
    var options = new MySqlBackupOptions {
        ContentIndex = content, GtidIndex = gtid, IncludeDatabase = includeDb,
        IncludeRoutines = enabled, IncludeEvents = enabled, IncludeTriggers = enabled,
        DropTables = enabled, AddLocks = enabled, CompleteInsert = enabled,
        ExtendedInsert = enabled, IncludeTablespaces = enabled
    };
    var arguments = MySqlDumpArguments.Build("C:/temp/config.cnf", "banco A", "C:/temp/backup.sql", options, new[] { "tabela ignorada" });
    Check(arguments.Contains("--databases") == includeDb, "Seleção do banco incorreta");
    Check(arguments.Contains("--no-data") == (content == 1), "Modo estrutura incorreto");
    Check(arguments.Contains("--no-create-info") == (content == 2), "Modo dados incorreto");
    Check(arguments.Contains("--ignore-table="), "Filtro de tabelas ausente");
    Check(arguments.Contains("--skip-add-drop-table") == (!enabled || content == 2), "DROP TABLE indevido");
    var copy = JsonSerializer.Deserialize<MySqlBackupOptions>(JsonSerializer.Serialize(options))!;
    Check(copy.IncludeDatabase == includeDb && copy.ContentIndex == content && copy.GtidIndex == gtid, "Persistência incorreta");
    // --help valida as opções no executável embarcado, sem conectar a um banco.
    var start = new ProcessStartInfo(cli, arguments.Replace("--result-file=\"C:/temp/backup.sql\"", "").Replace("--defaults-extra-file=\"C:/temp/config.cnf\"", "--no-defaults") + " --help") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
    using var process = Process.Start(start)!;
    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    await stdout;
    Check(process.ExitCode == 0, await stderr);
}
var defaults = MySqlDumpArguments.Build("config.cnf", "origem", "backup.sql", new());
Check(!defaults.Contains("--databases"), "Backup portátil deve ser o padrão");
Console.WriteLine("36 combinações validadas no mysqldump embarcado; persistência e padrão portátil OK.");
var tempRoot = Path.Combine(Path.GetTempPath(), "devsak-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);
try
{
    var source = Path.Combine(tempRoot, "source.sql");
    var output = Path.Combine(tempRoot, "output.sql");
    var script = """
-- USE never_touch;
CREATE DATABASE `origem`;
USE `origem`;
INSERT INTO `t` VALUES ('USE origem; DROP DATABASE origem;', 'origem.t');
/*!50003 CREATE*/ /*!50017 DEFINER=`admin`@`localhost`*/ /*!50003 VIEW `v` AS SELECT * FROM `origem`.`t` */;
DELIMITER $$
CREATE PROCEDURE p()
BEGIN
 SELECT 'a; USE x;';
 SELECT 1;
END$$
DELIMITER ;
SET @@GLOBAL.GTID_PURGED='abc';
LOCK TABLES t WRITE;
UNLOCK TABLES;
""";
    File.WriteAllText(source, script);
    MySqlRestoreScript.Process(source, output, new MySqlRestoreOptions { RemoveDefiner = true, IgnoreGtid = true, IgnoreLocks = true, RemapDatabase = true, SourceDatabase = "origem" }, "destino", false, _ => {}, default);
    var result = File.ReadAllText(output);
    Check(!result.Contains("CREATE DATABASE"), "CREATE DATABASE não filtrado");
    Check(!result.Contains("USE `origem`"), "USE não filtrado");
    Check(result.Contains("'USE origem; DROP DATABASE origem;'"), "Literal SQL foi alterado");
    Check(result.Contains("'origem.t'"), "String de dados foi remapeada");
    Check(result.Contains("`destino`.`t`"), "Referência qualificada não remapeada");
    Check(!result.Contains("DEFINER="), "DEFINER não removido");
    Check(!result.Contains("GTID_PURGED"), "GTID não removido");
    Check(!result.Contains("LOCK TABLES"), "Bloqueio não removido");
    Check(result.Contains("END$$") && result.Contains("SELECT 'a; USE x;'"), "Procedure danificada");
    File.WriteAllText(source, "/*!40014 SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0 */;\nSET FOREIGN_KEY_CHECKS=1;\nINSERT INTO t VALUES ('FOREIGN_KEY_CHECKS');");
    MySqlRestoreScript.Process(source, output, new(), "destino", true, _ => {}, default);
    result = File.ReadAllText(output);
    Check(!result.Contains("SET ") && result.Contains("'FOREIGN_KEY_CHECKS'"), "Controle de FK incorreto");
    Console.WriteLine("Filtros de restore, comentários executáveis, literais, DELIMITER, remapeamento e FK OK.");
}
finally { Directory.Delete(tempRoot, true); }
