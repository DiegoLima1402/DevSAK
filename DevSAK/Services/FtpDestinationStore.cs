using DevSAK.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DevSAK.Services;

// Shared across tools. AppZipSettings contains only the selected destination ID.
public sealed class FtpDestinationStore
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly string _path;
    private readonly ProtectedSettingsService _secrets = new();

    public FtpDestinationStore(string? directory = null)
    {
        directory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DevSAK");
        _path = Path.Combine(directory, "ftp-destinations.json");
    }

    public IReadOnlyList<FtpDestination> Load()
    {
        lock (Gate)
        {
            if (!File.Exists(_path)) return Array.Empty<FtpDestination>();
            var entries = JsonSerializer.Deserialize<List<FtpDestination>>(File.ReadAllText(_path), Options)
                ?? throw new InvalidDataException("O cadastro global de destinos FTP está vazio ou inválido.");
            if (entries.Any(entry => entry == null) || entries.Select(entry => entry.Id).Distinct().Count() != entries.Count)
                throw new InvalidDataException("O cadastro de destinos FTP contém registros inválidos ou duplicados.");
            return entries.Select(FtpDestinationValidation.Normalize).ToArray();
        }
    }

    public FtpDestination Save(FtpDestination destination, string password)
    {
        destination = FtpDestinationValidation.Normalize(destination);
        FtpDestinationValidation.RejectControls(password);
        destination = destination with { ProtectedPassword = _secrets.Protect(password) };
        lock (Gate)
        {
            var entries = Load().ToList();
            int index = entries.FindIndex(entry => entry.Id == destination.Id);
            if (index < 0) entries.Add(destination);
            else entries[index] = destination;
            Write(entries);
        }
        return destination;
    }

    public void Delete(string id)
    {
        lock (Gate)
        {
            var entries = Load().Where(entry => entry.Id != id).ToList();
            Write(entries);
        }
    }

    public string GetPassword(FtpDestination destination)
    {
        var password = _secrets.Unprotect(destination.ProtectedPassword);
        if (destination.ProtectedPassword.Length > 0 && password.Length == 0)
            throw new InvalidOperationException("Não foi possível descriptografar a senha deste destino para o usuário atual do Windows. Edite o destino e informe a senha novamente.");
        return password;
    }

    private void Write(List<FtpDestination> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(entries, Options));
            File.Move(temporary, _path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
