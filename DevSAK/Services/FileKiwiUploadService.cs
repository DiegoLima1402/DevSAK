using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DevSAK.Services;

public sealed class FileKiwiUploadService
{
    private static readonly HttpClient SharedClient = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly HttpClient _client;
    public FileKiwiUploadService(HttpClient? client = null) => _client = client ?? SharedClient;

    public async Task<string> UploadAsync(string path, IProgress<double>? progress, CancellationToken ct)
    {
        progress?.Report(0);
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true);
        long size = file.Length;
        if (size <= 0 || size > 999L * 1024 * 1024 * 1024)
            throw new InvalidOperationException("O File.Kiwi aceita arquivos de até 999 GiB e o ZIP não pode estar vazio.");
        var key = RandomNumberGenerator.GetBytes(16);
        try
        {
            var secret = Convert.ToBase64String(key).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var ske = Convert.ToBase64String(await FileKiwiEncryption.EncryptMetadataAsync(secret, key, ct));
            var name = Convert.ToBase64String(await FileKiwiEncryption.EncryptMetadataAsync(Path.GetFileName(path), key, ct));
            var folder = await ApiAsync(HttpMethod.Post, "v2/folders", new
            {
                title = "Arquivo ZIP",
                mode = "send",
                encryption = new { ske },
                files = new[] { new { filename = name, filesize = size, mimetype = "application/zip" } }
            }, ct);
            var folderId = Uri.EscapeDataString(folder.GetProperty("folderId").GetString()!);
            var auth = Uri.EscapeDataString(folder.GetProperty("uploadAuth").GetString()!);
            var files = folder.GetProperty("files");
            if (files.GetArrayLength() != 1) throw new InvalidOperationException("O File.Kiwi retornou um arquivo inválido.");
            var metadata = files[0];
            var fileId = Uri.EscapeDataString(metadata.GetProperty("fileId").GetString()!);
            long chunkSize = metadata.GetProperty("chunkSize").GetInt64();
            int chunks = metadata.GetProperty("chunks").GetInt32();
            var urls = metadata.GetProperty("uploadUrls");
            var signatures = urls.GetProperty("signatures");
            if (chunkSize <= 0 || chunks != (size - 1) / chunkSize + 1 || signatures.GetArrayLength() != chunks)
                throw new InvalidOperationException("O File.Kiwi retornou uma divisão de arquivo inválida.");
            var folderUrl = folder.GetProperty("folderUrl").GetString();
            if (!Uri.TryCreate(folderUrl, UriKind.Absolute, out var link) || link.Scheme != "https" || link.Host != "file.kiwi")
                throw new InvalidOperationException("O File.Kiwi não retornou um link válido.");
            var statusPath = $"v2/folders/{folderId}/files/{fileId}/upload-status?uploadAuth={auth}";
            long lastStatusCheck = Environment.TickCount64;
            long completed = 0;
            for (int index = 0; index < chunks; index++)
            {
                ct.ThrowIfCancellationRequested();
                // Keep long transfers alive using the documented status endpoint.
                if (Environment.TickCount64 - lastStatusCheck >= 3600000)
                {
                    await ApiAsync(HttpMethod.Get, statusPath, null, ct);
                    lastStatusCheck = Environment.TickCount64;
                }
                long count = Math.Min(chunkSize, size - completed);
                var url = $"{urls.GetProperty("head").GetString()}{urls.GetProperty("path").GetString()}/{index + 1:D5}?{urls.GetProperty("tail").GetString()}&X-Amz-Signature={signatures[index].GetString()}";
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uploadUri) || uploadUri.Scheme != "https")
                    throw new InvalidOperationException("O File.Kiwi retornou uma URL de upload inválida.");
                using var request = new HttpRequestMessage(HttpMethod.Put, uploadUri);
                request.Content = new EncryptedContent(file, count, completed, size, key, progress);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                foreach (var header in urls.GetProperty("headers").EnumerateObject())
                {
                    if (!request.Headers.TryAddWithoutValidation(header.Name, header.Value.GetString()))
                    {
                        request.Content.Headers.Remove(header.Name);
                        request.Content.Headers.TryAddWithoutValidation(header.Name, header.Value.GetString());
                    }
                }
                using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException($"Falha no envio ao File.Kiwi (HTTP {(int)response.StatusCode}). O ZIP local foi preservado.");
                completed += count;
            }
            // Allow a brief delay for storage visibility, but never report success before confirmation.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                var status = await ApiAsync(HttpMethod.Get, statusPath, null, ct);
                if (status.GetProperty("complete").GetBoolean())
                {
                    progress?.Report(100);
                    return link.GetLeftPart(UriPartial.Path) + "#" + secret;
                }
                if (attempt < 2) await Task.Delay(500, ct);
            }
            throw new InvalidOperationException("O File.Kiwi ainda não confirmou todas as partes do arquivo. O ZIP local foi preservado.");
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private async Task<JsonElement> ApiAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, "https://api.file.kiwi/" + path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body != null) request.Content = JsonContent.Create(body);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        using var response = await _client.SendAsync(request, timeout.Token);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"O File.Kiwi recusou a operação (HTTP {(int)response.StatusCode}). Verifique a conexão e tente novamente.");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        return json.RootElement.Clone();
    }

    private sealed class EncryptedContent(FileStream source, long length, long completed, long total, byte[] key,
        IProgress<double>? progress) : HttpContent
    {
        protected override bool TryComputeLength(out long result) { result = FileKiwiEncryption.EncryptedLength(length); return true; }
        protected override Task SerializeToStreamAsync(Stream target, TransportContext? context) => CopyAsync(target, CancellationToken.None);
        protected override Task SerializeToStreamAsync(Stream target, TransportContext? context, CancellationToken ct) => CopyAsync(target, ct);
        private Task CopyAsync(Stream target, CancellationToken ct)
        {
            long lastReport = Environment.TickCount64;
            return FileKiwiEncryption.EncryptAsync(source, length, target, key, ct, sent =>
            {
                if (sent == length || Environment.TickCount64 - lastReport >= 100)
                {
                    progress?.Report(Math.Min(99, (completed + sent) * 100d / total));
                    lastReport = Environment.TickCount64;
                }
            });
        }
    }
}
