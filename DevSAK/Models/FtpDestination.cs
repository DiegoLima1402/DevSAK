using System;
using System.Text.Json.Serialization;

namespace DevSAK.Models;

public enum FtpSecurity { None, ExplicitTls, ImplicitTls }

public sealed record FtpDestination
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Description { get; init; } = string.Empty;
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 21;
    public string UserName { get; init; } = string.Empty;
    public string ProtectedPassword { get; init; } = string.Empty;
    public FtpSecurity Security { get; init; }
    public bool Passive { get; init; } = true;
    public string RemoteDirectory { get; init; } = "/";
    [JsonIgnore] public string DisplayName => string.IsNullOrWhiteSpace(Description) ? Host : Description;
}

public sealed record FtpDirectoryEntry(string Name, string Path);
public sealed record FtpDirectoryListing(string Path, FtpDirectoryEntry[] Directories);
