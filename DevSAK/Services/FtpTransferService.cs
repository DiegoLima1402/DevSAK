using DevSAK.Models;
using FluentFTP;
using FluentFTP.Exceptions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace DevSAK.Services;

public sealed class FtpTransferService
{
    private static AsyncFtpClient Create(FtpDestination destination, string password)
    {
        FtpDestinationValidation.RejectControls(password);
        var client = new AsyncFtpClient(destination.Host, destination.UserName, password, destination.Port);
        client.Config.EncryptionMode = destination.Security switch
        {
            FtpSecurity.ExplicitTls => FtpEncryptionMode.Explicit,
            FtpSecurity.ImplicitTls => FtpEncryptionMode.Implicit,
            _ => FtpEncryptionMode.None
        };
        client.Config.DataConnectionType = destination.Passive ? FtpDataConnectionType.AutoPassive : FtpDataConnectionType.AutoActive;
        client.Config.ConnectTimeout = 15000;
        client.Config.ReadTimeout = 30000;
        client.Config.DataConnectionConnectTimeout = 15000;
        client.Config.DataConnectionReadTimeout = 30000;
        client.Config.RetryAttempts = 1;
        // Use the platform's certificate validation for FTPS.
        return client;
    }

    public async Task<FtpDirectoryListing> ListDirectoriesAsync(FtpDestination destination, string password,
        string path, CancellationToken ct)
    {
        destination = FtpDestinationValidation.Normalize(destination);
        path = FtpDestinationValidation.NormalizePath(path);
        try
        {
            await using var client = Create(destination, password);
            await client.Connect(ct);
            await client.SetWorkingDirectory(path, ct);
            var current = FtpDestinationValidation.NormalizePath(await client.GetWorkingDirectory(ct));
            var entries = await client.GetListing(current, ct);
            return new FtpDirectoryListing(current, entries
                .Where(entry => entry.Type == FtpObjectType.Directory && entry.Name is not "." and not "..")
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .Select(entry => new FtpDirectoryEntry(entry.Name, FtpDestinationValidation.NormalizePath(entry.FullName))).ToArray());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { throw new IOException(DescribeError(ex, $"Falha ao listar '{path}' em {destination.Host}:{destination.Port}.", password)); }
    }

    public async Task<string> UploadAsync(FtpDestination destination, string password, string localPath,
        IProgress<double>? progress, CancellationToken ct, Func<string, CancellationToken, Task<bool>>? confirmOverwrite = null)
    {
        destination = FtpDestinationValidation.Normalize(destination);
        var filename = Path.GetFileName(localPath);
        FtpDestinationValidation.RejectControls(filename);
        var remotePath = FtpDestinationValidation.NormalizePath(destination.RemoteDirectory + "/" + filename);
        try
        {
            progress?.Report(0);
            await using var client = Create(destination, password);
            await client.Connect(ct);
            await client.SetWorkingDirectory(destination.RemoteDirectory, ct);
            if (await client.FileExists(remotePath, ct))
            {
                if (confirmOverwrite == null) throw new IOException($"O arquivo '{remotePath}' já existe no FTP. A substituição não foi autorizada.");
                if (!await confirmOverwrite(remotePath, ct)) throw new OperationCanceledException("Substituição do arquivo FTP cancelada.", ct);
            }
            var forwarding = new TransferProgress(progress);
            var result = await client.UploadFile(localPath, remotePath, FtpRemoteExists.Overwrite, false, FtpVerify.None, forwarding, ct);
            if (result != FtpStatus.Success)
                throw new IOException($"O servidor não confirmou o upload. Resultado: {result}. Resposta FTP: {client.LastReply.Code} {client.LastReply.Message}");
            progress?.Report(100);
            return $"{destination.Host}:{destination.Port}{remotePath}";
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            throw new IOException(DescribeError(ex, $"Falha ao enviar '{remotePath}' para {destination.Host}:{destination.Port}. O ZIP local foi preservado; pode haver um arquivo parcial no servidor.", password));
        }
    }

    public static string DescribeError(Exception exception, string context, string? password = null)
    {
        var lines = new List<string> { context, "Detalhes técnicos:" };
        for (Exception? error = exception; error != null; error = error.InnerException)
        {
            string code = error is FtpCommandException ftp ? $" [FTP {ftp.CompletionCode}]" : error is SocketException socket ? $" [{socket.SocketErrorCode}]" : string.Empty;
            lines.Add($"{error.GetType().Name}{code}: {error.Message}");
        }
        var text = string.Join(Environment.NewLine, lines);
        return string.IsNullOrEmpty(password) ? text : text.Replace(password, "[senha omitida]", StringComparison.Ordinal);
    }

    private sealed class TransferProgress(IProgress<double>? progress) : IProgress<FtpProgress>
    {
        private long _lastReport;
        public void Report(FtpProgress value)
        {
            if (Environment.TickCount64 - _lastReport < 100 && value.Progress < 100) return;
            _lastReport = Environment.TickCount64;
            progress?.Report(Math.Clamp(value.Progress, 0, 99));
        }
    }
}
