using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DevSAK.Services;

static void Check(bool value, string message)
{
    if (!value) throw new Exception(message);
}

static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
{
    Content = new StringContent(JsonSerializer.Serialize(value))
};

var path = Path.Combine(Path.GetTempPath(), "devsak-storageto-" + Guid.NewGuid().ToString("N") + ".zip");
var bytes = Enumerable.Range(0, 300000).Select(n => (byte)(n % 251)).ToArray();
await File.WriteAllBytesAsync(path, bytes);
try
{
    foreach (bool multipart in new[] { false, true })
    foreach (bool authenticated in new[] { false, true })
    {
        var received = new List<byte>();
        var calls = new List<string>();
        var percentages = new List<double>();
        var handler = new Handler(async (request, ct) =>
        {
            var endpoint = request.RequestUri!.AbsolutePath;
            calls.Add(endpoint);
            if (request.Method == HttpMethod.Put)
            {
                Check(request.Headers.Authorization == null && !request.Headers.Contains("X-Visitor-Token") && !request.Headers.Contains("X-Owner-Token"), "Credenciais vazaram para R2");
                var chunk = await request.Content!.ReadAsByteArrayAsync(ct);
                Check(request.Content.Headers.ContentLength == chunk.Length, "Content-Length incorreto");
                received.AddRange(chunk);
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Headers.ETag = new EntityTagHeaderValue("\"etag" + calls.Count + "\"");
                return response;
            }
            Check(request.Headers.Authorization?.Parameter == (authenticated ? "secret-test" : null), "Autenticação incorreta");
            Check(request.Headers.GetValues("X-Visitor-Token").Single() == "visitor-test", "Visitor token ausente");
            switch (endpoint)
            {
                case "/api/user": return Json(new { id = 1 });
                case "/api/upload/init":
                    using (var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)))
                        Check(body.RootElement.GetProperty("size").GetInt64() == bytes.Length, "Tamanho incorreto");
                    return multipart
                        ? Json(new { success = true, type = "multipart", r2_key = "key", upload_id = "upload", part_size = 131072, total_parts = 3, initial_urls = new Dictionary<string, string> { ["1"] = "https://r2.example/1" }, owner_token = "owner" })
                        : Json(new { success = true, type = "single", r2_key = "key", upload_url = "https://r2.example/single", headers = new Dictionary<string, string[]> { ["Content-Type"] = ["application/zip"], ["Host"] = ["r2.example"] } });
                case "/api/upload/parts":
                    Check(request.Headers.GetValues("X-Owner-Token").Single() == "owner", "Owner token ausente");
                    return Json(new { success = true, part_urls = new[] { new { partNumber = 2, url = "https://r2.example/2" }, new { partNumber = 3, url = "https://r2.example/3" } } });
                case "/api/upload/complete-multipart":
                    using (var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)))
                    {
                        var parts = body.RootElement.GetProperty("parts");
                        Check(parts.GetArrayLength() == 3 && parts[0].GetProperty("etag").GetString()!.StartsWith('"'), "ETags incorretos");
                    }
                    return Json(new { success = true });
                case "/api/upload/confirm":
                    Check(received.SequenceEqual(bytes), "Bytes enviados diferem do arquivo");
                    return Json(new { success = true, file = new { url = "https://storage.to/test12345" } });
                default: throw new Exception("Endpoint inesperado: " + endpoint);
            }
        });
        using var client = new HttpClient(handler);
        var result = await new StorageToUploadService(client).UploadAsync(path, authenticated ? "secret-test" : "", "visitor-test", new InlineProgress(percentages.Add), default);
        Check(result == "https://storage.to/test12345", "Link incorreto");
        Check(percentages[0] == 0 && percentages[^1] == 100 && percentages.SequenceEqual(percentages.Order()), "Progresso incorreto");
        Check(calls.Contains("/api/user") == authenticated && calls[^1] == "/api/upload/confirm", "Ordem de chamadas incorreta");
    }

    int rejectedCalls = 0;
    using (var client = new HttpClient(new Handler((request, ct) =>
    {
        rejectedCalls++;
        Check(request.RequestUri!.AbsolutePath == "/api/user", "Upload iniciado com chave inválida");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
    })))
    {
        try { await new StorageToUploadService(client).UploadAsync(path, "invalid", "visitor", null, default); throw new Exception("Chave inválida aceita"); }
        catch (InvalidOperationException ex) { Check(ex.Message.Contains("inválida"), "Erro de chave não é claro"); }
        Check(rejectedCalls == 1, "Fallback anônimo inesperado");
    }

    foreach (bool cancel in new[] { false, true })
    {
        bool aborted = false, confirmed = false;
        using var cancellation = new CancellationTokenSource();
        using var client = new HttpClient(new Handler((request, ct) =>
        {
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/api/upload/init": return Task.FromResult(Json(new { type = "multipart", r2_key = "key", upload_id = "upload", part_size = bytes.Length, total_parts = 1, initial_urls = new Dictionary<string, string> { ["1"] = "https://r2.example/fail" }, owner_token = "owner" }));
                case "/fail":
                    if (cancel) { cancellation.Cancel(); ct.ThrowIfCancellationRequested(); }
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                case "/api/upload/abort": aborted = true; Check(!ct.IsCancellationRequested, "Limpeza usa token cancelado"); return Task.FromResult(Json(new { success = true }));
                case "/api/upload/confirm": confirmed = true; break;
            }
            throw new Exception("Endpoint inesperado");
        }));
        try { await new StorageToUploadService(client).UploadAsync(path, "", "visitor", null, cancellation.Token); throw new Exception("Falha ignorada"); }
        catch (InvalidOperationException) when (!cancel) { }
        catch (OperationCanceledException) when (cancel) { }
        Check(aborted && !confirmed && File.Exists(path), "Falha/cancelamento não preservou estado corretamente");
    }
    Console.WriteLine("OK: upload simples/multipart, bytes, progresso, anônimo/autenticado, validação de chave, isolamento de credenciais, falha e cancelamento com abort.");
}
finally { File.Delete(path); }

sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
}
sealed class InlineProgress(Action<double> report) : IProgress<double>
{
    public void Report(double value) => report(value);
}
