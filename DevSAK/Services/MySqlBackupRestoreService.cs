using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevSAK.Models;

namespace DevSAK.Services
{
    public class MySqlBackupRestoreService
    {
        private readonly MySqlCliLocator _cliLocator;
        private readonly MySqlOutputParser _outputParser;
        private readonly MySqlServerService _serverService;

        public MySqlBackupRestoreService(
            MySqlCliLocator cliLocator,
            MySqlOutputParser outputParser,
            MySqlServerService serverService)
        {
            _cliLocator = cliLocator;
            _outputParser = outputParser;
            _serverService = serverService;
        }

        public async Task BackupAsync(
            MySqlConnectionProfile profile,
            string database,
            string outputFilePath,
            MySqlBackupRestoreSettings settings,
            IProgress<MySqlOperationUpdate>? progress,
            CancellationToken cancellationToken)
        {
            var cliPaths = _cliLocator.Locate();
            if (string.IsNullOrWhiteSpace(cliPaths.PreferredBackupPath))
            {
                throw new InvalidOperationException("Não foi possível localizar mysqldump.exe. Para testes manuais, mysqlpump.exe também pode ser usado como fallback opcional.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath)!);

            var backupToolPath = cliPaths.PreferredBackupPath!;
            var usesMysqlPump = string.Equals(Path.GetFileName(backupToolPath), "mysqlpump.exe", StringComparison.OrdinalIgnoreCase);
            var tableEstimates = await _serverService.GetTableBackupEstimatesAsync(profile, database, cancellationToken).ConfigureAwait(false);
            var progressTracker = new BackupProgressTracker(progress, tableEstimates);

            progress?.Report(MySqlOperationUpdate.Log($"Executável selecionado: {backupToolPath}"));
            progressTracker.ReportStage("Preparando backup...", 0);
            progress?.Report(MySqlOperationUpdate.Log("Validando parâmetros do backup..."));
            progressTracker.ReportStage("Validando conexão...", 8);
            progress?.Report(MySqlOperationUpdate.Log($"{tableEstimates.Count} tabelas analisadas para estimativa de progresso."));

            var tempDefaultsFile = CreateDefaultsFile(profile);
            try
            {
                var arguments = usesMysqlPump
                    ? BuildMysqlPumpArguments(tempDefaultsFile, database)
                    : BuildMysqldumpArguments(tempDefaultsFile, database, outputFilePath);

                progress?.Report(MySqlOperationUpdate.Log($"Comando: {Path.GetFileName(backupToolPath)} {arguments}"));
                progressTracker.ReportStage("Iniciando mysqldump...", 15);

                var startInfo = new ProcessStartInfo
                {
                    FileName = backupToolPath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                if (!process.Start())
                {
                    throw new InvalidOperationException("Falha ao iniciar o processo de backup do MySQL.");
                }

                using var registration = cancellationToken.Register(() => TryTerminateProcess(process));
                var stdoutCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var stderrCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                process.OutputDataReceived += (_, args) =>
                {
                    if (args.Data is null)
                    {
                        stdoutCompletion.TrySetResult();
                        return;
                    }

                    progressTracker.HandleOutputLine(args.Data, MySqlBackupRestoreMode.Backup);
                };

                process.ErrorDataReceived += (_, args) =>
                {
                    if (args.Data is null)
                    {
                        stderrCompletion.TrySetResult();
                        return;
                    }

                    progressTracker.HandleErrorLine(args.Data);
                };

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var heartbeatTask = RunBackupHeartbeatAsync(process, progressTracker, outputFilePath, cancellationToken);

                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                await stdoutCompletion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                await stderrCompletion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                await heartbeatTask.ConfigureAwait(false);

                if (process.ExitCode != 0)
                {
                    progress?.Report(MySqlOperationUpdate.Log($"Backup encerrado com falha. Código de saída: {process.ExitCode}."));
                    DeleteEmptySqlFile(outputFilePath, progress);
                    throw new InvalidOperationException($"O backup falhou com código de saída {process.ExitCode}.");
                }

                progressTracker.ReportStage("Finalizando...", 95);
                progress?.Report(MySqlOperationUpdate.CreateStatus("Concluído com sucesso", 100, false));
                progress?.Report(MySqlOperationUpdate.Log($"Arquivo SQL gerado em: {outputFilePath}"));
            }
            catch
            {
                DeleteEmptySqlFile(outputFilePath, progress);

                throw;
            }
            finally
            {
                TryDeleteFile(tempDefaultsFile);
            }
        }

        public async Task RestoreAsync(
            MySqlConnectionProfile profile,
            string database,
            string sourceFilePath,
            bool disableForeignKeyChecks,
            bool recreateDatabaseBeforeRestore,
            MySqlBackupRestoreSettings settings,
            IProgress<MySqlOperationUpdate>? progress,
            CancellationToken cancellationToken)
        {
            var cliPaths = _cliLocator.Locate();
            if (string.IsNullOrWhiteSpace(cliPaths.MysqlPath))
            {
                throw new InvalidOperationException("Não foi possível localizar mysql.exe em Tools\\MySql.");
            }

            var sourceFileInfo = new FileInfo(sourceFilePath);
            if (!sourceFileInfo.Exists)
            {
                throw new FileNotFoundException("O arquivo SQL informado não foi encontrado.", sourceFilePath);
            }

            progress?.Report(MySqlOperationUpdate.Log($"Executável selecionado: {cliPaths.MysqlPath}"));
            progress?.Report(MySqlOperationUpdate.Log("Iniciando restauração..."));
            progress?.Report(MySqlOperationUpdate.CreateStatus("Preparando restauração...", 0, false));

            string? tempExtractionFolder = null;
            var tempDefaultsFile = CreateDefaultsFile(profile);
            try
            {
                var sqlFiles = await ResolveRestoreSqlFilesAsync(sourceFileInfo, progress, cancellationToken).ConfigureAwait(false);
                tempExtractionFolder = sqlFiles.TempFolder;

                if (sqlFiles.Files.Count == 0)
                {
                    throw new InvalidOperationException("Nenhum arquivo .sql foi encontrado para restauração.");
                }

                progress?.Report(MySqlOperationUpdate.Log($"{sqlFiles.Files.Count} arquivo(s) SQL encontrado(s) para restauração."));
                var totalBytes = Math.Max(1L, sqlFiles.Files.Sum(file => file.Length));
                long completedBytes = 0;

                for (var index = 0; index < sqlFiles.Files.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var currentFile = sqlFiles.Files[index];
                    var restoreIndex = index + 1;
                    var shouldRecreateDatabase = recreateDatabaseBeforeRestore && index == 0;
                    var displayName = GetRestoreDisplayName(currentFile, tempExtractionFolder);

                    progress?.Report(MySqlOperationUpdate.Log($"Restaurando arquivo {restoreIndex} de {sqlFiles.Files.Count}: {displayName}"));
                    progress?.Report(MySqlOperationUpdate.CreateStatus($"Restaurando arquivo {restoreIndex} de {sqlFiles.Files.Count}...", CalculateRestoreOverallPercentage(completedBytes, totalBytes), false));

                    await RestoreSingleSqlFileAsync(
                        cliPaths.MysqlPath!,
                        tempDefaultsFile,
                        profile,
                        database,
                        currentFile,
                        disableForeignKeyChecks,
                        shouldRecreateDatabase,
                        restoreIndex,
                        sqlFiles.Files.Count,
                        completedBytes,
                        totalBytes,
                        progress,
                        cancellationToken).ConfigureAwait(false);

                    completedBytes += currentFile.Length;
                }

                progress?.Report(MySqlOperationUpdate.CreateStatus("Concluído com sucesso", 100, false));
                progress?.Report(MySqlOperationUpdate.Log("Importação finalizada com sucesso."));
            }
            finally
            {
                TryDeleteFile(tempDefaultsFile);
                TryDeleteDirectory(tempExtractionFolder);
            }
        }

        private async Task RestoreSingleSqlFileAsync(
            string mysqlPath,
            string tempDefaultsFile,
            MySqlConnectionProfile profile,
            string database,
            FileInfo sourceFileInfo,
            bool disableForeignKeyChecks,
            bool recreateDatabaseBeforeRestore,
            int currentFileIndex,
            int totalFiles,
            long completedBytesBeforeFile,
            long totalBytes,
            IProgress<MySqlOperationUpdate>? progress,
            CancellationToken cancellationToken)
        {
            progress?.Report(MySqlOperationUpdate.Log("Abrindo arquivo SQL..."));
            progress?.Report(MySqlOperationUpdate.CreateStatus("Abrindo arquivo SQL...", CalculateRestoreOverallPercentage(completedBytesBeforeFile, totalBytes), false));

            if (recreateDatabaseBeforeRestore)
            {
                progress?.Report(MySqlOperationUpdate.Log($"A base \"{database}\" será excluída e recriada antes da restauração."));
                progress?.Report(MySqlOperationUpdate.CreateStatus("Excluindo base de dados...", Math.Max(5, CalculateRestoreOverallPercentage(completedBytesBeforeFile, totalBytes)), false));
                await _serverService.DropDatabaseIfExistsAsync(profile, database, cancellationToken).ConfigureAwait(false);

                progress?.Report(MySqlOperationUpdate.CreateStatus("Recriando base de dados...", Math.Max(8, CalculateRestoreOverallPercentage(completedBytesBeforeFile, totalBytes)), false));
                await _serverService.CreateDatabaseAsync(profile, database, cancellationToken).ConfigureAwait(false);
                progress?.Report(MySqlOperationUpdate.Log($"Base \"{database}\" pronta para restauração."));
                progress?.Report(MySqlOperationUpdate.CreateStatus("Preparando restauração...", Math.Max(10, CalculateRestoreOverallPercentage(completedBytesBeforeFile, totalBytes)), false));
            }

            var arguments = BuildMysqlRestoreArguments(tempDefaultsFile, database);
            progress?.Report(MySqlOperationUpdate.Log($"Comando: mysql.exe {arguments}"));
            progress?.Report(MySqlOperationUpdate.Log("Iniciando processo mysql.exe..."));
            progress?.Report(MySqlOperationUpdate.CreateStatus("Iniciando processo mysql.exe...", Math.Max(12, CalculateRestoreOverallPercentage(completedBytesBeforeFile, totalBytes)), false));

            var startInfo = new ProcessStartInfo
            {
                FileName = mysqlPath,
                Arguments = arguments,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var stderrBuffer = new StringBuilder();
            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            if (!process.Start())
            {
                throw new InvalidOperationException("Falha ao iniciar o processo de restauração do MySQL.");
            }

            using var registration = cancellationToken.Register(() => TryTerminateProcess(process));

            var stdoutCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var stderrCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data is null)
                {
                    stdoutCompletion.TrySetResult();
                    return;
                }

                HandleRestoreOutputLine(args.Data, progress, stderrBuffer: null);
            };

            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data is null)
                {
                    stderrCompletion.TrySetResult();
                    return;
                }

                HandleRestoreOutputLine(args.Data, progress, stderrBuffer);
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await WriteRestoreInputAsync(
                process,
                sourceFileInfo,
                disableForeignKeyChecks,
                currentFileIndex,
                totalFiles,
                completedBytesBeforeFile,
                totalBytes,
                progress,
                cancellationToken).ConfigureAwait(false);

            progress?.Report(MySqlOperationUpdate.Log("Finalizando restauração..."));
            progress?.Report(MySqlOperationUpdate.CreateStatus("Finalizando...", Math.Min(95, CalculateRestoreOverallPercentage(completedBytesBeforeFile + sourceFileInfo.Length, totalBytes)), false));

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await stdoutCompletion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            await stderrCompletion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                progress?.Report(MySqlOperationUpdate.Log($"Restauração encerrada com falha. Código de saída: {process.ExitCode}."));
                throw CreateRestoreFailureException(process.ExitCode, stderrBuffer);
            }
        }

        private static async Task RunBackupHeartbeatAsync(
            Process process,
            BackupProgressTracker progressTracker,
            string outputFilePath,
            CancellationToken cancellationToken)
        {
            while (!process.HasExited)
            {
                try
                {
                    await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                long? currentFileSize = null;
                try
                {
                    if (File.Exists(outputFilePath))
                    {
                        currentFileSize = new FileInfo(outputFilePath).Length;
                    }
                }
                catch
                {
                }

                progressTracker.ReportHeartbeat(currentFileSize);
            }
        }

        private void HandleRestoreOutputLine(
            string line,
            IProgress<MySqlOperationUpdate>? progress,
            StringBuilder? stderrBuffer)
        {
            progress?.Report(MySqlOperationUpdate.Log(line));

            if (stderrBuffer is not null)
            {
                lock (stderrBuffer)
                {
                    if (stderrBuffer.Length > 0)
                    {
                        stderrBuffer.AppendLine();
                    }

                    stderrBuffer.Append(line);
                }
            }

            var parsed = _outputParser.Parse(line, MySqlBackupRestoreMode.Restore);
            if (parsed.Status is not null)
            {
                progress?.Report(MySqlOperationUpdate.CreateStatus(parsed.Status, double.NaN, true));
            }
        }

        private static async Task WriteRestoreInputAsync(
            Process process,
            FileInfo sourceFileInfo,
            bool disableForeignKeyChecks,
            int currentFileIndex,
            int totalFiles,
            long completedBytesBeforeFile,
            long totalBytes,
            IProgress<MySqlOperationUpdate>? progress,
            CancellationToken cancellationToken)
        {
            long bytesCopied = 0;
            var lastReportedProgress = -1d;
            var lastLogBucket = -1;
            var lastProgressReportAt = DateTime.UtcNow;

            await using var sourceStream = new FileStream(sourceFileInfo.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);

            // Access the BaseStream directly without an await-using so we can close the StreamWriter
            // (process.StandardInput) at the end — that is the correct single close that signals EOF to mysql.exe.
            // Wrapping BaseStream in its own await-using caused a double-dispose crash.
            var inputStream = process.StandardInput.BaseStream;

            progress?.Report(MySqlOperationUpdate.Log("Enviando script SQL para o mysql.exe..."));
            progress?.Report(MySqlOperationUpdate.CreateStatus($"Enviando script SQL ({currentFileIndex}/{totalFiles})...", CalculateRestoreOverallPercentage(completedBytesBeforeFile, totalBytes), false));

            if (disableForeignKeyChecks)
            {
                var prefix = Encoding.UTF8.GetBytes("SET FOREIGN_KEY_CHECKS = 0;\n");
                await inputStream.WriteAsync(prefix, cancellationToken).ConfigureAwait(false);
                progress?.Report(MySqlOperationUpdate.Log("FOREIGN_KEY_CHECKS desabilitado temporariamente."));
            }

            var buffer = new byte[65536];
            int read;
            while ((read = await sourceStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await inputStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                bytesCopied += read;

                if (ShouldReportRestoreProgress(totalBytes, bytesCopied, ref lastReportedProgress, ref lastProgressReportAt))
                {
                    var filePercentage = sourceFileInfo.Length <= 0 ? 100 : (bytesCopied * 100.0 / sourceFileInfo.Length);
                    var overallPercentage = CalculateRestoreOverallPercentage(completedBytesBeforeFile + bytesCopied, totalBytes);
                    progress?.Report(MySqlOperationUpdate.CreateStatus(
                        $"Restaurando arquivo {currentFileIndex} de {totalFiles}... {filePercentage:N0}% concluído",
                        overallPercentage,
                        false));

                    var currentLogBucket = (int)(filePercentage / 10);
                    if (currentLogBucket > lastLogBucket && currentLogBucket is >= 0 and <= 10)
                    {
                        lastLogBucket = currentLogBucket;
                        progress?.Report(MySqlOperationUpdate.Log($"{Math.Min(100, currentLogBucket * 10)}% do arquivo SQL enviado."));
                    }
                }
            }

            if (disableForeignKeyChecks)
            {
                var suffix = Encoding.UTF8.GetBytes("\nSET FOREIGN_KEY_CHECKS = 1;\n");
                await inputStream.WriteAsync(suffix, cancellationToken).ConfigureAwait(false);
                progress?.Report(MySqlOperationUpdate.Log("FOREIGN_KEY_CHECKS reabilitado ao final da importação."));
            }

            await inputStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            // Close the StreamWriter — this signals EOF to mysql.exe and is the single correct close point.
            process.StandardInput.Close();
        }

        private static string BuildMysqldumpArguments(string defaultsFile, string database, string outputFilePath)
        {
            return $"--defaults-extra-file={Quote(defaultsFile)} --single-transaction --routines --events --triggers --hex-blob --verbose --result-file={Quote(outputFilePath)} --databases {Quote(database)}";
        }

        private static string BuildMysqlPumpArguments(string defaultsFile, string database)
        {
            return $"--defaults-extra-file={Quote(defaultsFile)} --routines --events --triggers --default-parallelism=2 --databases {Quote(database)}";
        }

        private static string BuildMysqlRestoreArguments(string defaultsFile, string database)
        {
            return $"--defaults-extra-file={Quote(defaultsFile)} --database={Quote(database)} --default-character-set=utf8mb4 --comments --show-warnings";
        }

        private static bool ShouldReportRestoreProgress(
            long totalBytes,
            long bytesCopied,
            ref double lastReportedProgress,
            ref DateTime lastProgressReportAt)
        {
            if (totalBytes <= 0)
            {
                return false;
            }

            var stagePercentage = Math.Min(90, 30 + (bytesCopied * 60.0 / totalBytes));
            var now = DateTime.UtcNow;
            var shouldReport =
                lastReportedProgress < 0 ||
                stagePercentage >= 90 ||
                stagePercentage - lastReportedProgress >= 1 ||
                (now - lastProgressReportAt) >= TimeSpan.FromMilliseconds(350);

            if (shouldReport)
            {
                lastReportedProgress = stagePercentage;
                lastProgressReportAt = now;
            }

            return shouldReport;
        }

        private async Task<RestoreSqlFileSet> ResolveRestoreSqlFilesAsync(
            FileInfo sourceFileInfo,
            IProgress<MySqlOperationUpdate>? progress,
            CancellationToken cancellationToken)
        {
            if (IsSqlFile(sourceFileInfo.FullName))
            {
                progress?.Report(MySqlOperationUpdate.Log("Arquivo SQL selecionado diretamente."));
                return new RestoreSqlFileSet(new List<FileInfo> { sourceFileInfo }, null);
            }

            if (!IsArchiveCandidateFile(sourceFileInfo.FullName))
            {
                throw new InvalidOperationException("Formato não suportado. Selecione um arquivo .sql, .zip ou outro compactado suportado pelo 7-Zip.");
            }

            var tempFolder = Path.Combine(Path.GetTempPath(), $"devsak-mysql-restore-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempFolder);

            progress?.Report(MySqlOperationUpdate.Log($"Extraindo arquivo compactado para: {tempFolder}"));
            progress?.Report(MySqlOperationUpdate.CreateStatus("Extraindo arquivo compactado...", 5, false));

            try
            {
                if (IsZipFile(sourceFileInfo.FullName))
                {
                    await Task.Run(() => ZipFile.ExtractToDirectory(sourceFileInfo.FullName, tempFolder), cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await ExtractArchiveWith7ZipAsync(sourceFileInfo.FullName, tempFolder, progress, cancellationToken).ConfigureAwait(false);
                }

                var sqlFiles = Directory
                    .EnumerateFiles(tempFolder, "*.sql", SearchOption.AllDirectories)
                    .Select(path => new FileInfo(path))
                    .OrderBy(file => Path.GetRelativePath(tempFolder, file.FullName), StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (sqlFiles.Count == 0)
                {
                    throw new InvalidOperationException("O arquivo compactado não contém arquivos .sql.");
                }

                progress?.Report(MySqlOperationUpdate.Log($"Extração concluída. {sqlFiles.Count} arquivo(s) .sql localizado(s)."));
                return new RestoreSqlFileSet(sqlFiles, tempFolder);
            }
            catch
            {
                TryDeleteDirectory(tempFolder);
                throw;
            }
        }

        private static async Task ExtractArchiveWith7ZipAsync(
            string archivePath,
            string destinationFolder,
            IProgress<MySqlOperationUpdate>? progress,
            CancellationToken cancellationToken)
        {
            var sevenZipPath = LocateSevenZipExecutable();
            if (string.IsNullOrWhiteSpace(sevenZipPath))
            {
                throw new InvalidOperationException("Arquivos do 7-Zip não encontrados em Tools\\7Zip\\7z.exe. Arquivos .zip são suportados nativamente.");
            }

            progress?.Report(MySqlOperationUpdate.Log($"Extrator selecionado: {sevenZipPath}"));

            var startInfo = new ProcessStartInfo
            {
                FileName = sevenZipPath,
                Arguments = $"x -y -o{Quote(destinationFolder)} {Quote(archivePath)}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var stderrBuffer = new StringBuilder();
            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            if (!process.Start())
            {
                throw new InvalidOperationException("Falha ao iniciar a extração do arquivo compactado.");
            }

            using var registration = cancellationToken.Register(() => TryTerminateProcess(process));
            var stdoutCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var stderrCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data is null)
                {
                    stdoutCompletion.TrySetResult();
                    return;
                }

                progress?.Report(MySqlOperationUpdate.Log(args.Data));
            };

            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data is null)
                {
                    stderrCompletion.TrySetResult();
                    return;
                }

                lock (stderrBuffer)
                {
                    stderrBuffer.AppendLine(args.Data);
                }

                progress?.Report(MySqlOperationUpdate.Log(args.Data));
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await stdoutCompletion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            await stderrCompletion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                string details;
                lock (stderrBuffer)
                {
                    details = stderrBuffer.ToString().Trim();
                }

                throw new InvalidOperationException(string.IsNullOrWhiteSpace(details)
                    ? $"A extração do arquivo compactado falhou com código de saída {process.ExitCode}."
                    : $"A extração do arquivo compactado falhou com código de saída {process.ExitCode}. Detalhes: {details}");
            }
        }

        private static string? LocateSevenZipExecutable()
        {
            var bundledPath = Path.Combine(AppContext.BaseDirectory, "Tools", "7Zip", "7z.exe");
            return File.Exists(bundledPath) ? bundledPath : null;
        }

        private static bool IsSqlFile(string path)
            => string.Equals(Path.GetExtension(path), ".sql", StringComparison.OrdinalIgnoreCase);

        private static bool IsZipFile(string path)
            => string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase);

        private static bool IsArchiveCandidateFile(string path)
        {
            var extension = Path.GetExtension(path);
            return !string.IsNullOrWhiteSpace(extension) &&
                   !string.Equals(extension, ".sql", StringComparison.OrdinalIgnoreCase);
        }

        private static double CalculateRestoreOverallPercentage(long completedBytes, long totalBytes)
        {
            if (totalBytes <= 0)
            {
                return 90;
            }

            var normalized = Math.Max(0, Math.Min(1d, completedBytes / (double)totalBytes));
            return Math.Min(92, 15 + (normalized * 77));
        }

        private static InvalidOperationException CreateRestoreFailureException(int exitCode, StringBuilder stderrBuffer)
        {
            string details;
            lock (stderrBuffer)
            {
                details = stderrBuffer.ToString().Trim();
            }

            if (string.IsNullOrWhiteSpace(details))
            {
                return new InvalidOperationException($"A restauração falhou com código de saída {exitCode}.");
            }

            var compactDetails = details.Length > 600
                ? details[^600..]
                : details;

            compactDetails = compactDetails.Replace(Environment.NewLine, " | ");
            return new InvalidOperationException($"A restauração falhou com código de saída {exitCode}. Detalhes: {compactDetails}");
        }

        private static string CreateDefaultsFile(MySqlConnectionProfile profile)
        {
            var filePath = Path.Combine(Path.GetTempPath(), $"devsak-mysql-{Guid.NewGuid():N}.cnf");
            var sslMode = profile.UseSsl ? "REQUIRED" : "DISABLED";
            var contents = new StringBuilder()
                .AppendLine("[client]")
                .AppendLine($"host={profile.Host}")
                .AppendLine($"port={profile.Port}")
                .AppendLine($"user={profile.UserName}")
                .AppendLine($"password=\"{EscapeForOptionFile(profile.Password)}\"")
                .AppendLine($"ssl-mode={sslMode}")
                .ToString();

            File.WriteAllText(filePath, contents, new UTF8Encoding(false));
            return filePath;
        }

        private static string EscapeForOptionFile(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string Quote(string value)
        {
            return $"\"{value.Replace("\"", "\\\"")}\"";
        }

        private static void TryTerminateProcess(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch
            {
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private static void TryDeleteDirectory(string? path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
            }
        }

        private static string GetRestoreDisplayName(FileInfo file, string? rootFolder)
        {
            if (!string.IsNullOrWhiteSpace(rootFolder))
            {
                return Path.GetRelativePath(rootFolder, file.FullName);
            }

            return file.Name;
        }

        private static void DeleteEmptySqlFile(string path, IProgress<MySqlOperationUpdate>? progress)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return;
                }

                var fileInfo = new FileInfo(path);
                if (fileInfo.Length == 0)
                {
                    File.Delete(path);
                    progress?.Report(MySqlOperationUpdate.Log("Arquivo SQL vazio removido após falha no backup."));
                }
            }
            catch
            {
            }
        }

        private sealed class BackupProgressTracker
        {
            private readonly object _sync = new();
            private readonly IProgress<MySqlOperationUpdate>? _progress;
            private readonly Dictionary<string, long> _tableWeights;
            private readonly HashSet<string> _completedTables = new(StringComparer.OrdinalIgnoreCase);
            private readonly long _totalWeight;
            private readonly double _estimatedOutputBytes;
            private string? _currentTable;
            private double _currentTableProgress;
            private double _currentPercentage = 15;
            private DateTime _lastStatusReportUtc = DateTime.MinValue;
            private DateTime _lastLogReportUtc = DateTime.MinValue;
            private long _lastReportedFileSize;

            public BackupProgressTracker(IProgress<MySqlOperationUpdate>? progress, IReadOnlyList<MySqlTableBackupEstimate> tableEstimates)
            {
                _progress = progress;
                _tableWeights = tableEstimates.ToDictionary(
                    item => item.TableName,
                    item => Math.Max(1L, item.EstimatedBytes),
                    StringComparer.OrdinalIgnoreCase);
                _totalWeight = Math.Max(1L, _tableWeights.Values.Sum());
                _estimatedOutputBytes = Math.Max(1d, _totalWeight * 1.10d);
            }

            public void ReportStage(string status, double percentage)
            {
                lock (_sync)
                {
                    _currentPercentage = Math.Max(_currentPercentage, percentage);
                    _lastStatusReportUtc = DateTime.UtcNow;
                }

                _progress?.Report(MySqlOperationUpdate.CreateStatus(status, percentage, false));
            }

            public void HandleOutputLine(string line, MySqlBackupRestoreMode mode)
            {
                _progress?.Report(MySqlOperationUpdate.Log(line));

                var parsed = new MySqlOutputParser().Parse(line, mode);
                if (parsed.Status is not null)
                {
                    double percentage;
                    lock (_sync)
                    {
                        percentage = _currentPercentage;
                    }

                    _progress?.Report(MySqlOperationUpdate.CreateStatus(parsed.Status, percentage, false));
                }
            }

            public void HandleErrorLine(string line)
            {
                _progress?.Report(MySqlOperationUpdate.Log(line));

                var parsed = new MySqlOutputParser().Parse(line, MySqlBackupRestoreMode.Backup);
                if (!string.IsNullOrWhiteSpace(parsed.TableName))
                {
                    TrackTableProgress(parsed.TableName);
                }

                if (parsed.Status is null)
                {
                    return;
                }

                double percentage;

                lock (_sync)
                {
                    if (parsed.CountsAsProgressUnit)
                    {
                        _currentPercentage = Math.Max(_currentPercentage, CalculateOverallPercentageUnsafe());
                        _lastStatusReportUtc = DateTime.UtcNow;
                    }

                    percentage = _currentPercentage;
                }

                _progress?.Report(MySqlOperationUpdate.CreateStatus(parsed.Status, percentage, false));
            }

            public void ReportHeartbeat(long? currentFileSize)
            {
                string? statusToReport = null;
                string? logToReport = null;
                double percentageToReport;

                lock (_sync)
                {
                    var now = DateTime.UtcNow;
                    if ((now - _lastStatusReportUtc) < TimeSpan.FromMilliseconds(600))
                    {
                        return;
                    }

                    if (!string.IsNullOrWhiteSpace(_currentTable))
                    {
                        _currentTableProgress = Math.Min(0.95, _currentTableProgress + 0.08);
                    }

                    _currentPercentage = Math.Max(_currentPercentage, CalculateOverallPercentageUnsafe());
                    if (currentFileSize.HasValue && currentFileSize.Value > 0)
                    {
                        _currentPercentage = Math.Max(_currentPercentage, CalculateFileProgressPercentageUnsafe(currentFileSize.Value));
                    }

                    percentageToReport = _currentPercentage;
                    statusToReport = string.IsNullOrWhiteSpace(_currentTable)
                        ? "Exportando esquema e dados..."
                        : $"Exportando tabela: {_currentTable}";
                    _lastStatusReportUtc = now;

                    if (currentFileSize.HasValue &&
                        currentFileSize.Value > 0 &&
                        currentFileSize.Value - _lastReportedFileSize >= 25L * 1024L * 1024L &&
                        (now - _lastLogReportUtc) >= TimeSpan.FromSeconds(1))
                    {
                        _lastReportedFileSize = currentFileSize.Value;
                        _lastLogReportUtc = now;
                        logToReport = $"Arquivo SQL em geração: {currentFileSize.Value / (1024d * 1024d):N1} MB";
                    }
                }

                if (logToReport is not null)
                {
                    _progress?.Report(MySqlOperationUpdate.Log(logToReport));
                }

                _progress?.Report(MySqlOperationUpdate.CreateStatus(statusToReport!, percentageToReport, false));
            }

            private void TrackTableProgress(string tableName)
            {
                lock (_sync)
                {
                    if (string.Equals(_currentTable, tableName, StringComparison.OrdinalIgnoreCase))
                    {
                        _currentTableProgress = Math.Max(_currentTableProgress, 0.15);
                        return;
                    }

                    if (!string.IsNullOrWhiteSpace(_currentTable))
                    {
                        _completedTables.Add(_currentTable);
                    }

                    _currentTable = tableName;
                    _currentTableProgress = 0.15;
                    _currentPercentage = Math.Max(_currentPercentage, CalculateOverallPercentageUnsafe());
                }
            }

            private double CalculateOverallPercentageUnsafe()
            {
                long completedWeight = 0;
                foreach (var completedTable in _completedTables)
                {
                    completedWeight += GetWeightForTable(completedTable);
                }

                long currentWeight = string.IsNullOrWhiteSpace(_currentTable) ? 0 : GetWeightForTable(_currentTable);
                var progressedWeight = completedWeight + (currentWeight * _currentTableProgress);

                var normalized = _totalWeight <= 0
                    ? 0
                    : progressedWeight / _totalWeight;

                return Math.Min(94, Math.Max(20, 20 + (normalized * 72)));
            }

            private double CalculateFileProgressPercentageUnsafe(long currentFileSize)
            {
                var normalized = Math.Min(1d, currentFileSize / _estimatedOutputBytes);
                return Math.Min(94, Math.Max(20, 20 + (normalized * 72)));
            }

            private long GetWeightForTable(string tableName)
            {
                return _tableWeights.TryGetValue(tableName, out var weight) ? weight : 1L;
            }
        }
    }

    public sealed record MySqlOperationUpdate(string? Status, double Percentage, bool IsIndeterminate, string? LogLine)
    {
        public static MySqlOperationUpdate CreateStatus(string status, double percentage, bool isIndeterminate)
            => new(status, percentage, isIndeterminate, null);

        public static MySqlOperationUpdate Log(string line)
            => new(null, double.NaN, true, line);
    }

    internal sealed record RestoreSqlFileSet(IReadOnlyList<FileInfo> Files, string? TempFolder);
}
