using DevSAK.Models;
using System;
using System.Collections.Generic;

namespace DevSAK.Services;

public static class FtpDestinationValidation
{
    public static FtpDestination Normalize(FtpDestination destination)
    {
        var host = destination.Host.Trim().Trim('[', ']');
        if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
            throw new ArgumentException("Informe um host válido, sem ftp://, porta ou caminho. A porta tem seu próprio campo.");
        if (destination.Port is < 1 or > 65535) throw new ArgumentException("A porta deve estar entre 1 e 65535.");
        if (string.IsNullOrWhiteSpace(destination.UserName)) throw new ArgumentException("Informe o usuário do FTP (ou anonymous para acesso anônimo).");
        if (!Enum.IsDefined(destination.Security)) throw new ArgumentException("Selecione um tipo de conexão FTP válido.");
        RejectControls(destination.UserName);
        RejectControls(destination.Description);
        if (!Guid.TryParseExact(destination.Id, "N", out _)) throw new ArgumentException("O identificador do destino FTP é inválido.");
        return destination with
        {
            Host = host, Description = destination.Description.Trim(), UserName = destination.UserName.Trim(),
            RemoteDirectory = NormalizePath(destination.RemoteDirectory)
        };
    }

    public static string NormalizePath(string path)
    {
        RejectControls(path);
        var parts = new List<string>();
        foreach (var part in path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            if (part == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
            else parts.Add(part);
        }
        return "/" + string.Join("/", parts);
    }

    public static string ParentPath(string path) => NormalizePath(NormalizePath(path) + "/..");

    public static void RejectControls(string value)
    {
        foreach (char character in value)
            if (char.IsControl(character)) throw new ArgumentException("Os campos FTP não podem conter caracteres de controle ou quebras de linha.");
    }
}
