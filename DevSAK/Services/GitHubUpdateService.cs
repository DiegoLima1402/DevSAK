using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevSAK.Models;

namespace DevSAK.Services;

public sealed class GitHubUpdateService
{
    public const string Repository = "DiegoLima1402/DevSAK";
    private static readonly HttpClient SharedClient = CreateClient();
    private readonly HttpClient _client;
    private readonly string _downloadRoot;

    public GitHubUpdateService() : this(SharedClient, Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevSAK", "Updates")) { }

    internal GitHubUpdateService(HttpClient client, string downloadRoot)
    {
        _client = client;
        _downloadRoot = downloadRoot;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DevSAK/1.3.3");
        return client;
    }

    public async Task<AppUpdate?> CheckAsync(Version currentVersion, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.github.com/repos/{Repository}/releases/latest");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await _client.SendAsync(request, timeout.Token).ConfigureAwait(false);
        // A public repository without a published release returns 404.
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            throw new HttpRequestException("O GitHub limitou as consultas. Tente novamente mais tarde.");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
        return ParseRelease(json.RootElement, currentVersion);
    }

    internal static AppUpdate? ParseRelease(JsonElement release, Version currentVersion)
    {
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) return null;
        var tag = release.GetProperty("tag_name").GetString() ?? "";
        if (!TryParseStableVersion(tag, out var version)) return null;
        var current = Normalize(currentVersion);
        if (version <= current) return null;
        var name = $"DevSAK_Installer_v{version.ToString(3)}.exe";
        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != name) continue;
            var url = asset.GetProperty("browser_download_url").GetString();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
                uri.Host != "github.com" ||
                uri.AbsolutePath != $"/{Repository}/releases/download/{tag}/{name}")
                throw new InvalidDataException("A Release contém um endereço de instalador inválido.");
            var digest = asset.TryGetProperty("digest", out var d) ? d.GetString() : null;
            if (digest is null || !digest.StartsWith("sha256:", StringComparison.Ordinal) ||
                digest.Length != 71 || !IsHex(digest[7..]))
                throw new InvalidDataException("O instalador não possui um SHA-256 válido no GitHub. Publique novamente o arquivo da Release.");
            var size = asset.GetProperty("size").GetInt64();
            if (size <= 0) throw new InvalidDataException("O instalador publicado está vazio.");
            var notes = release.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
            return new AppUpdate(version, notes, uri, size, digest[7..]);
        }
        throw new InvalidDataException($"A versão {version.ToString(3)} ainda não possui o arquivo {name}.");
    }

    internal static bool TryParseStableVersion(string text, out Version version)
    {
        version = new Version(0, 0, 0);
        if (text.StartsWith('v')) text = text[1..];
        var parts = text.Split('.');
        if (parts.Length != 3) return false;
        foreach (var part in parts)
            if (part.Length == 0 || !int.TryParse(part, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out _)) return false;
        return Version.TryParse(text, out version!);
    }

    private static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(0, version.Build));
    private static bool IsHex(string value)
    {
        foreach (var c in value) if (!Uri.IsHexDigit(c)) return false;
        return true;
    }

    public async Task<string> DownloadAsync(AppUpdate update, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(_downloadRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, $"DevSAK_Installer_v{update.Version.ToString(3)}.exe");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(30));
            using var response = await _client.GetAsync(update.DownloadUri, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long total = 0;
            int count;
            var lastProgress = -1;
            while ((count = await source.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
            {
                total += count;
                if (total > update.Size) throw new InvalidDataException("O tamanho do instalador difere do publicado.");
                await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token).ConfigureAwait(false);
                hash.AppendData(buffer, 0, count);
                var percent = (int)(total * 100 / update.Size);
                if (percent != lastProgress) { progress?.Report(percent); lastProgress = percent; }
            }
            if (total != update.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("A verificação SHA-256 falhou. O instalador foi descartado; tente baixar novamente.");
            await output.FlushAsync(timeout.Token).ConfigureAwait(false);
            return destination;
        }
        catch
        {
            try { File.Delete(destination); Directory.Delete(directory); } catch { }
            throw;
        }
    }
}
