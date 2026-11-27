using System;
using System.IO;

namespace DevSAK.Services;

public sealed class StorageToSettingsStore
{
    private readonly string _directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DevSAK");
    private readonly ProtectedSettingsService _protection = new();

    public string LoadApiKey()
    {
        var path = Path.Combine(_directory, "storageto.key");
        return File.Exists(path) ? _protection.Unprotect(File.ReadAllText(path)) : string.Empty;
    }

    public void SaveApiKey(string key)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "storageto.key");
        var encrypted = _protection.Protect(key.Trim());
        File.WriteAllText(path + ".tmp", encrypted);
        File.Move(path + ".tmp", path, overwrite: true);
    }

    public string GetVisitorToken()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "storageto.visitor");
        if (File.Exists(path)) return File.ReadAllText(path).Trim();
        var token = Guid.NewGuid().ToString("N");
        File.WriteAllText(path, token);
        return token;
    }
}
