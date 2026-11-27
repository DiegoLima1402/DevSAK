using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DevSAK.Services;

public sealed class StorageToUploadService
{
    // API credentials are attached only to API requests, never to signed R2 URLs.
    private static readonly HttpClient SharedClient = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly HttpClient _client;
    public StorageToUploadService(HttpClient? client = null) => _client = client ?? SharedClient;

    public async Task<string> UploadAsync(string path, string apiKey, string visitorToken,
        IProgress<double>? progress, CancellationToken cancellationToken)
    {
        apiKey = apiKey.Trim();
        progress?.Report(0);
        if (apiKey.Length > 0)
            await ApiAsync(HttpMethod.Get, "user", null, apiKey, visitorToken, null, cancellationToken);

        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true);
        var name = Path.GetFileName(path);
        var size = file.Length;
        if (size == 0) throw new InvalidOperationException("O ZIP está vazio.");
        var init = await ApiAsync(HttpMethod.Post, "upload/init", new { filename = name, size, content_type = "application/zip" },
            apiKey, visitorToken, null, cancellationToken);
        var key = init.GetProperty("r2_key").GetString()!;
        string? uploadId = null;
        string? owner = null;
        try
        {
            if (init.GetProperty("type").GetString() == "multipart")
            {
                uploadId = init.GetProperty("upload_id").GetString()!;
                owner = init.TryGetProperty("owner_token", out var token) ? token.GetString() : null;
                var partSize = init.GetProperty("part_size").GetInt64();
                var total = init.GetProperty("total_parts").GetInt32();
                if (partSize <= 0 || total != (size - 1) / partSize + 1)
                    throw new InvalidOperationException("O StorageTo retornou uma divisão de arquivo inválida.");
                var urls = new Dictionary<int, string>();
                if (init.TryGetProperty("initial_urls", out var initial))
                    foreach (var entry in initial.EnumerateObject()) urls[int.Parse(entry.Name)] = entry.Value.GetString()!;
                var parts = new List<object>();
                for (int number = 1; number <= total; number++)
                {
                    if (!urls.TryGetValue(number, out var url))
                    {
                        var numbers = new List<int>();
                        for (int n = number; n <= total && numbers.Count < 100; n++)
                            if (!urls.ContainsKey(n)) numbers.Add(n);
                        var additional = await ApiAsync(HttpMethod.Post, "upload/parts", new { upload_id = uploadId, part_numbers = numbers },
                            apiKey, visitorToken, owner, cancellationToken);
                        foreach (var entry in additional.GetProperty("part_urls").EnumerateArray())
                            urls[entry.GetProperty("partNumber").GetInt32()] = entry.GetProperty("url").GetString()!;
                        url = urls[number];
                    }
                    var etag = await PutAsync(file, Math.Min(partSize, size - file.Position), size, url, null, progress, cancellationToken);
                    if (string.IsNullOrWhiteSpace(etag)) throw new InvalidOperationException("O StorageTo não retornou o ETag da parte enviada.");
                    parts.Add(new { partNumber = number, etag });
                }
                await ApiAsync(HttpMethod.Post, "upload/complete-multipart", new { upload_id = uploadId, parts },
                    apiKey, visitorToken, owner, cancellationToken);
            }
            else
            {
                JsonElement? headers = init.TryGetProperty("headers", out var h) ? h : null;
                await PutAsync(file, size, size, init.GetProperty("upload_url").GetString()!, headers, progress, cancellationToken);
            }
            var confirm = await ApiAsync(HttpMethod.Post, "upload/confirm",
                new { filename = name, size, content_type = "application/zip", r2_key = key }, apiKey, visitorToken, owner, cancellationToken);
            var link = confirm.GetProperty("file").GetProperty("url").GetString();
            if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme != "https")
                throw new InvalidOperationException("O StorageTo não retornou um link válido.");
            progress?.Report(100);
            return link!;
        }
        catch
        {
            if (uploadId != null)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try { await ApiAsync(HttpMethod.Post, "upload/abort", new { upload_id = uploadId }, apiKey, visitorToken, owner, cleanup.Token); }
                catch { /* Preserve the original upload error. */ }
            }
            throw;
        }
    }

    private async Task<JsonElement> ApiAsync(HttpMethod method, string endpoint, object? body, string key,
        string visitor, string? owner, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, "https://storage.to/api/" + endpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (key.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Headers.Add("X-Visitor-Token", visitor);
        if (owner != null) request.Headers.Add("X-Owner-Token", owner);
        if (body != null) request.Content = JsonContent.Create(body);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        using var response = await _client.SendAsync(request, timeout.Token);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden && key.Length > 0)
            throw new InvalidOperationException("A chave de API do StorageTo é inválida ou não tem permissão. Atualize a chave em Configurações ou remova-a para enviar anonimamente.");
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"O StorageTo recusou a operação (HTTP {(int)response.StatusCode}). Verifique sua conexão, os limites da conta e tente novamente.");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        if (json.RootElement.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False)
            throw new InvalidOperationException("O StorageTo não conseguiu concluir a operação.");
        return json.RootElement.Clone();
    }

    private async Task<string?> PutAsync(FileStream file, long count, long total, string url, JsonElement? headers,
        IProgress<double>? progress, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            throw new InvalidOperationException("O StorageTo retornou uma URL de upload inválida.");
        using var request = new HttpRequestMessage(HttpMethod.Put, uri);
        request.Content = new UploadContent(file, count, total, progress);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        if (headers.HasValue)
            foreach (var header in headers.Value.EnumerateObject())
            {
                if (header.Name.Equals("Host", StringComparison.OrdinalIgnoreCase) || header.Name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
                var values = new List<string>();
                if (header.Value.ValueKind == JsonValueKind.Array)
                    foreach (var value in header.Value.EnumerateArray()) values.Add(value.GetString()!);
                else values.Add(header.Value.GetString()!);
                if (!request.Headers.TryAddWithoutValidation(header.Name, values))
                {
                    request.Content.Headers.Remove(header.Name);
                    request.Content.Headers.TryAddWithoutValidation(header.Name, values);
                }
            }
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Falha no envio do ZIP (HTTP {(int)response.StatusCode}). O arquivo local foi preservado.");
        return response.Headers.ETag?.ToString();
    }

    private sealed class UploadContent(FileStream file, long length, long total, IProgress<double>? progress) : HttpContent
    {
        protected override bool TryComputeLength(out long result) { result = length; return true; }
        protected override Task SerializeToStreamAsync(Stream target, TransportContext? context) => CopyAsync(target, CancellationToken.None);
        protected override Task SerializeToStreamAsync(Stream target, TransportContext? context, CancellationToken ct) => CopyAsync(target, ct);
        private async Task CopyAsync(Stream target, CancellationToken ct)
        {
            var buffer = new byte[131072];
            long remaining = length;
            long lastReport = Environment.TickCount64;
            while (remaining > 0)
            {
                int read = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), ct);
                if (read == 0) throw new EndOfStreamException("O ZIP foi alterado durante o envio.");
                await target.WriteAsync(buffer.AsMemory(0, read), ct);
                remaining -= read;
                if (remaining == 0 || Environment.TickCount64 - lastReport >= 100)
                {
                    progress?.Report(Math.Min(99, file.Position * 100d / total));
                    lastReport = Environment.TickCount64;
                }
            }
        }
    }
}
