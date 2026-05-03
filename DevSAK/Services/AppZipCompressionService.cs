using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DevSAK.Models;

namespace DevSAK.Services
{
    public class AppZipCompressionService
    {
        private const double ScanPhaseWeightPercent = 10.0;
        private const double ZipPhaseWeightPercent = 90.0;

        private const int CopyBufferSize = 262144; // 256 KiB
        private const int ProgressMinIntervalMs = 100;

        /// <summary>
        /// Scans the tree and returns candidate files with sizes. Progress is mapped to 0–10%.
        /// </summary>
        public async Task<List<ZipCandidateFile>> ScanFilesAsync(
            string sourceDirectory,
            DateTime? startDate = null,
            string? ignoredFiles = null,
            string? ignoredFolders = null,
            string? ignoredExtensions = null,
            IProgress<AppZipCompressionProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sourceDirectory))
                throw new ArgumentException("sourceDirectory is required", nameof(sourceDirectory));

            if (!Directory.Exists(sourceDirectory))
                throw new DirectoryNotFoundException(sourceDirectory);

            return await Task.Run(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                    return new List<ZipCandidateFile>();

                var throttle = new ProgressThrottle(progress);
                throttle.Report(0, "Contando arquivos...", force: true);

                var filePatterns = CompilePatterns(ignoredFiles);
                var folderPatterns = CompilePatterns(ignoredFolders);
                var extensionPatterns = CompilePatterns(ignoredExtensions);
                var filterDate = startDate?.Date;

                var total = CountFiles(sourceDirectory, filterDate, filePatterns, folderPatterns, extensionPatterns);
                var results = new List<ZipCandidateFile>(Math.Max(0, total));
                long processed = 0;

                throttle.Report(0, "Escaneando arquivos...", force: true);

                var dirsStack = new Stack<string>();
                dirsStack.Push(sourceDirectory);

                while (dirsStack.Count > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var dir = dirsStack.Pop();

                    if (IsFolderIgnored(dir, folderPatterns))
                        continue;

                    IEnumerable<string> files = Array.Empty<string>();
                    try
                    {
                        files = Directory.EnumerateFiles(dir);
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (var file in files)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (IsFileIgnored(file, filePatterns) || IsExtensionIgnored(file, extensionPatterns))
                        {
                            ReportScanProgress(showPath: false);
                            continue;
                        }

                        try
                        {
                            var lastWriteDate = File.GetLastWriteTime(file).Date;
                            if (filterDate.HasValue && lastWriteDate < filterDate.Value)
                            {
                                ReportScanProgress(showPath: false);
                                continue;
                            }
                        }
                        catch
                        {
                            ReportScanProgress(showPath: false);
                            continue;
                        }

                        long length = 0;
                        try
                        {
                            length = new FileInfo(file).Length;
                        }
                        catch
                        {
                            length = 0;
                        }

                        results.Add(new ZipCandidateFile(file, length));
                        ReportScanProgress(showPath: true);

                        void ReportScanProgress(bool showPath)
                        {
                            processed++;
                            var scanPortion = total > 0 ? processed / (double)Math.Max(total, 1) : 1.0;
                            var pct = ScanPhaseWeightPercent * Math.Min(1.0, scanPortion);
                            if (showPath)
                                throttle.SetLatestPath(GetRelativePathSafe(sourceDirectory, file));
                            throttle.Report(pct, "Escaneando arquivos...");
                        }
                    }

                    IEnumerable<string> subdirs = Array.Empty<string>();
                    try
                    {
                        subdirs = Directory.EnumerateDirectories(dir);
                    }
                    catch
                    {
                        /* skip */
                    }

                    foreach (var sub in subdirs)
                    {
                        if (IsFolderIgnored(sub, folderPatterns))
                            continue;
                        dirsStack.Push(sub);
                    }
                }

                if (!cancellationToken.IsCancellationRequested)
                {
                    throttle.SetLatestPath(null);
                    throttle.Report(ScanPhaseWeightPercent, $"Escaneamento concluído. {results.Count} arquivo(s) encontrado(s).", force: true);
                }

                return results;
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task<string> CompressAsync(
            string sourceDirectory,
            string destinationDirectory,
            string zipName,
            DateTime? startDate = null,
            string? ignoredFiles = null,
            string? ignoredFolders = null,
            string? ignoredExtensions = null,
            IProgress<AppZipCompressionProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(destinationDirectory))
                throw new ArgumentException("destinationDirectory is required", nameof(destinationDirectory));

            if (string.IsNullOrWhiteSpace(zipName))
                throw new ArgumentException("zipName is required", nameof(zipName));

            Directory.CreateDirectory(destinationDirectory);
            var throttle = new ProgressThrottle(progress);
            throttle.Report(0, "Preparando compactação...", force: true);

            List<ZipCandidateFile> files;
            try
            {
                files = await ScanFilesAsync(
                    sourceDirectory,
                    startDate,
                    ignoredFiles,
                    ignoredFolders,
                    ignoredExtensions,
                    progress,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throttle.SetLatestPath(null);
                throttle.Report(0, "Operação cancelada pelo usuário.", force: true);
                return string.Empty;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throttle.SetLatestPath(null);
                throttle.Report(0, "Operação cancelada pelo usuário.", force: true);
                return string.Empty;
            }

            var zipPath = Path.Combine(destinationDirectory, zipName);
            if (File.Exists(zipPath))
            {
                try
                {
                    File.Delete(zipPath);
                }
                catch
                {
                    /* ignore */
                }
            }

            throttle.SetLatestPath(null);
            throttle.Report(ScanPhaseWeightPercent, $"Criando arquivo ZIP: {zipName}", force: true);

            var totalBytes = files.Sum(f => f.SizeBytes);
            var useByteWeights = totalBytes > 0;
            var totalWeight = useByteWeights ? totalBytes : Math.Max(1, files.Count);
            long completedWeight = 0;

            return await Task.Run(() =>
            {
                try
                {
                    using var zipArchive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
                    var totalFiles = files.Count;
                    for (var index = 0; index < files.Count; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var candidate = files[index];
                        var relativePath = GetRelativePath(sourceDirectory, candidate.FullPath);
                        var entryName = relativePath.Replace('\\', '/');

                        throttle.SetLatestPath(entryName);
                        throttle.Report(
                            ZipPercent(completedWeight, totalWeight),
                            $"Compactando ({index + 1}/{totalFiles})...");

                        try
                        {
                            var entry = zipArchive.CreateEntry(entryName, CompressionLevel.Optimal);
                            entry.LastWriteTime = new DateTimeOffset(File.GetLastWriteTime(candidate.FullPath));

                            using var entryStream = entry.Open();
                            using var sourceStream = new FileStream(
                                candidate.FullPath,
                                FileMode.Open,
                                FileAccess.Read,
                                FileShare.Read,
                                bufferSize: CopyBufferSize,
                                options: FileOptions.SequentialScan);

                            var buffer = new byte[CopyBufferSize];
                            long writtenFromThisFile = 0;
                            var fileSize = candidate.SizeBytes > 0 ? candidate.SizeBytes : Math.Max(1, sourceStream.Length);

                            while (true)
                            {
                                cancellationToken.ThrowIfCancellationRequested();

                                var read = sourceStream.Read(buffer.AsSpan(0, buffer.Length));
                                if (read == 0)
                                    break;

                                entryStream.Write(buffer, 0, read);
                                writtenFromThisFile += read;

                                if (useByteWeights)
                                {
                                    var inFileRatio = Math.Min(1.0, writtenFromThisFile / (double)Math.Max(fileSize, 1));
                                    var weightSoFar = completedWeight + fileSize * inFileRatio;
                                    throttle.Report(
                                        ZipPercent(weightSoFar, totalWeight),
                                        $"Compactando ({index + 1}/{totalFiles})...");
                                }
                                else
                                {
                                    var inFileRatio = sourceStream.Length > 0
                                        ? Math.Min(1.0, writtenFromThisFile / (double)sourceStream.Length)
                                        : 1.0;
                                    var weightSoFar = completedWeight + inFileRatio;
                                    throttle.Report(
                                        ZipPercent(weightSoFar, totalWeight),
                                        $"Compactando ({index + 1}/{totalFiles})...");
                                }
                            }

                            if (useByteWeights)
                                completedWeight += candidate.SizeBytes > 0 ? candidate.SizeBytes : writtenFromThisFile;
                            else
                                completedWeight += 1;
                        }
                        catch (Exception ex) when (ex is OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error adding file {candidate.FullPath}: {ex.Message}");
                        }
                    }

                    throttle.SetLatestPath(null);
                    throttle.Report(100, "Compactação concluída com sucesso!", force: true);
                    return zipPath;
                }
                catch (OperationCanceledException)
                {
                    try
                    {
                        if (File.Exists(zipPath))
                            File.Delete(zipPath);
                    }
                    catch
                    {
                        /* ignore */
                    }

                    throttle.SetLatestPath(null);
                    throttle.Report(0, "Operação cancelada pelo usuário.", force: true);
                    return string.Empty;
                }
                catch (Exception ex)
                {
                    try
                    {
                        if (File.Exists(zipPath))
                            File.Delete(zipPath);
                    }
                    catch
                    {
                        /* ignore */
                    }

                    throttle.SetLatestPath(null);
                    throttle.Report(0, $"Erro durante compactação: {ex.Message}", force: true);
                    return string.Empty;
                }
            }, cancellationToken).ConfigureAwait(false);
        }

        private static double ZipPercent(double completedWeight, double totalWeight)
        {
            if (totalWeight <= 0)
                return ScanPhaseWeightPercent + ZipPhaseWeightPercent;

            var ratio = Math.Min(1.0, completedWeight / totalWeight);
            return ScanPhaseWeightPercent + ZipPhaseWeightPercent * ratio;
        }

        private sealed class ProgressThrottle
        {
            private readonly IProgress<AppZipCompressionProgress>? _target;
            private long _lastReportTick;
            private string? _latestPath;

            public ProgressThrottle(IProgress<AppZipCompressionProgress>? target) => _target = target;

            public void SetLatestPath(string? path) => _latestPath = path;

            public void Report(double percent, string status, bool force = false)
            {
                var now = Environment.TickCount64;
                if (!force && now - _lastReportTick < ProgressMinIntervalMs && percent < 99.999)
                    return;

                _lastReportTick = now;
                _target?.Report(new AppZipCompressionProgress(
                    Math.Clamp(percent, 0, 100),
                    status,
                    _latestPath));
            }
        }

        #region Helpers

        private static List<Regex> CompilePatterns(string? patterns)
        {
            var list = new List<Regex>();
            if (string.IsNullOrWhiteSpace(patterns))
                return list;

            var parts = patterns.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts.Select(x => x.Trim()))
            {
                if (string.IsNullOrEmpty(p))
                    continue;
                var escaped = Regex.Escape(p).Replace("\\*", ".*").Replace("\\?", ".");
                try
                {
                    list.Add(new Regex($"^{escaped}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
                }
                catch
                {
                    /* skip bad pattern */
                }
            }

            return list;
        }

        private static bool IsFileIgnored(string filePath, List<Regex> filePatterns)
        {
            if (filePatterns == null || filePatterns.Count == 0)
                return false;
            return filePatterns.Any(rx => rx.IsMatch(Path.GetFileName(filePath) ?? ""));
        }

        private static bool IsExtensionIgnored(string filePath, List<Regex> extensionPatterns)
        {
            if (extensionPatterns == null || extensionPatterns.Count == 0)
                return false;
            return extensionPatterns.Any(rx => rx.IsMatch(Path.GetExtension(filePath) ?? ""));
        }

        private static bool IsFolderIgnored(string folderPath, List<Regex> folderPatterns)
        {
            if (folderPatterns == null || folderPatterns.Count == 0)
                return false;
            return folderPatterns.Any(rx => rx.IsMatch(Path.GetFileName(folderPath) ?? folderPath));
        }

        private static int CountFiles(string root, DateTime? filterDate, List<Regex> filePatterns, List<Regex> folderPatterns, List<Regex> extensionPatterns)
        {
            var count = 0;
            var stack = new Stack<string>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                var dir = stack.Pop();
                if (IsFolderIgnored(dir, folderPatterns))
                    continue;

                try
                {
                    foreach (var f in Directory.EnumerateFiles(dir))
                    {
                        if (IsFileIgnored(f, filePatterns) || IsExtensionIgnored(f, extensionPatterns))
                            continue;
                        if (filterDate.HasValue)
                        {
                            try
                            {
                                if (File.GetLastWriteTime(f).Date < filterDate.Value)
                                    continue;
                            }
                            catch
                            {
                                continue;
                            }
                        }

                        count++;
                    }
                }
                catch
                {
                    /* skip */
                }

                try
                {
                    foreach (var sd in Directory.EnumerateDirectories(dir))
                        stack.Push(sd);
                }
                catch
                {
                    /* skip */
                }
            }

            return count;
        }

        private static string GetRelativePath(string basePath, string fullPath)
        {
            basePath = Path.GetFullPath(basePath).TrimEnd(Path.DirectorySeparatorChar);
            fullPath = Path.GetFullPath(fullPath);
            if (fullPath.StartsWith(basePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return fullPath.Substring(basePath.Length + 1);
            return fullPath;
        }

        private static string GetRelativePathSafe(string basePath, string fullPath)
        {
            try
            {
                return GetRelativePath(basePath, fullPath);
            }
            catch
            {
                return Path.GetFileName(fullPath) ?? fullPath;
            }
        }

        #endregion
    }
}
