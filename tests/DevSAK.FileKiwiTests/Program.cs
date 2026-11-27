using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DevSAK.Services;

static void Check(bool value, string message)
{
    if (!value) throw new Exception(message);
}
static byte[] Base64Url(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));
static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };

// The published RFC 8188 section 3.1 vector independently fixes HKDF, nonce, framing and GCM output.
var vector = Base64Url("I1BsxtFttlv3u_Oo94xnmwAAEAAA-NAVub2qFgBEuQKRapoZu-IxkIva3MEB1PD-ly8Thjg");
using (var target = new MemoryStream())
using (var source = new MemoryStream(Encoding.UTF8.GetBytes("I am the walrus")))
{
    await FileKiwiEncryption.EncryptAsync(source, source.Length, target, Base64Url("yqdlZ-tYemfogSmv7Ws5PQ"), default,
        salt: vector[..16], recordSize: 4096);
    Check(target.ToArray().SequenceEqual(vector), "Criptografia difere do vetor oficial RFC 8188");
}

// Receiver-side decoder; uses explicit HKDF extraction/expansion rather than the production helper.
static byte[] Decrypt(byte[] body, byte[] secret)
{
    int rs = (int)BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(16));
    Check(body[20] == 0, "Key ID inesperado");
    var prk = HMACSHA256.HashData(body[..16], secret);
    var cek = HMACSHA256.HashData(prk, Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0\x01"))[..16];
    var nonceBase = HMACSHA256.HashData(prk, Encoding.ASCII.GetBytes("Content-Encoding: nonce\0\x01"))[..12];
    using var aes = new AesGcm(cek, 16);
    using var plain = new MemoryStream();
    uint seq = 0;
    for (int offset = 21; offset < body.Length; offset += rs)
    {
        int length = Math.Min(rs, body.Length - offset);
        var nonce = (byte[])nonceBase.Clone();
        for (int b = 0; b < 4; b++) nonce[11 - b] ^= (byte)(seq >> (8 * b));
        var record = new byte[length - 16];
        aes.Decrypt(nonce, body.AsSpan(offset, length - 16), body.AsSpan(offset + length - 16, 16), record);
        Check(record[^1] == (offset + length == body.Length ? 2 : 1), "Delimitador RFC 8188 inválido");
        plain.Write(record, 0, record.Length - 1);
        seq++;
    }
    return plain.ToArray();
}

var path = Path.Combine(Path.GetTempPath(), "devsak-filekiwi-" + Guid.NewGuid().ToString("N") + ".zip");
var allBytes = Enumerable.Range(0, 300000).Select(n => (byte)(n % 251)).ToArray();
try
{
    foreach (int size in new[] { 1, 65519, 65520, 300000 })
    {
        var bytes = allBytes[..size];
        await File.WriteAllBytesAsync(path, bytes);
        foreach (bool split in new[] { false, true })
        {
            int chunkSize = split ? 65519 : size;
            int chunks = (size - 1) / chunkSize + 1;
            var received = new List<byte[]>();
            var percentages = new List<double>();
            string? ske = null, filename = null;
            bool verified = false;
            using var client = new HttpClient(new Handler(async (request, ct) =>
            {
                Check(request.Headers.Authorization == null, "Chave StorageTo vazou para File.Kiwi");
                if (request.Method == HttpMethod.Post)
                {
                    Check(request.RequestUri!.ToString() == "https://api.file.kiwi/v2/folders", "Endpoint incorreto");
                    var json = await request.Content!.ReadAsStringAsync(ct);
                    using var body = JsonDocument.Parse(json);
                    ske = body.RootElement.GetProperty("encryption").GetProperty("ske").GetString();
                    var file = body.RootElement.GetProperty("files")[0];
                    filename = file.GetProperty("filename").GetString();
                    Check(file.GetProperty("filesize").GetInt64() == size && file.GetProperty("mimetype").GetString() == "application/zip", "Metadados incorretos");
                    Check(!json.Contains(Path.GetFileName(path)), "Nome enviado sem criptografia");
                    return Json(new
                    {
                        folderId = "folder", folderUrl = "https://file.kiwi/folder", uploadAuth = "auth-test",
                        files = new[] { new { fileId = "file", chunkSize, chunks, uploadUrls = new
                        {
                            head = "https://r2.example/", path = "opaque/prefix", tail = "signed=yes",
                            signatures = Enumerable.Range(1, chunks).Select(i => "sig" + i).ToArray(),
                            headers = new Dictionary<string, string> { ["x-amz-meta-folder_id"] = "folder", ["x-amz-meta-chunks"] = chunks.ToString() }
                        } } }
                    });
                }
                if (request.Method == HttpMethod.Put)
                {
                    int n = received.Count + 1;
                    Check(request.RequestUri!.ToString() == $"https://r2.example/opaque/prefix/{n:D5}?signed=yes&X-Amz-Signature=sig{n}", "URL/assinatura da parte incorreta");
                    Check(request.Headers.GetValues("x-amz-meta-folder_id").Single() == "folder", "Cabeçalhos assinados ausentes");
                    Check(!request.Headers.Contains("uploadAuth"), "Token de confirmação vazou para R2");
                    var encrypted = await request.Content!.ReadAsByteArrayAsync(ct);
                    int plainLength = Math.Min(chunkSize, size - received.Count * chunkSize);
                    Check(encrypted.Length == FileKiwiEncryption.EncryptedLength(plainLength) && request.Content.Headers.ContentLength == encrypted.Length, "Comprimento cifrado incorreto");
                    received.Add(encrypted);
                    return new HttpResponseMessage(HttpStatusCode.OK);
                }
                Check(request.RequestUri!.AbsolutePath == "/v2/folders/folder/files/file/upload-status" && request.RequestUri.Query.Contains("uploadAuth=auth-test"), "Confirmação incorreta");
                Check(received.Count == chunks && !percentages.Contains(100), "Sucesso antes da confirmação");
                verified = true;
                return Json(new { complete = true, missing = Array.Empty<int>() });
            }));
            var result = await new FileKiwiUploadService(client).UploadAsync(path, new InlineProgress(percentages.Add), default);
            var secret = new Uri(result).Fragment[1..];
            Check(Base64Url(secret).Length == 16, "Chave do link inválida");
            Check(Encoding.UTF8.GetString(Decrypt(Convert.FromBase64String(ske!), Base64Url(secret))) == secret, "SKE incompatível");
            Check(Encoding.UTF8.GetString(Decrypt(Convert.FromBase64String(filename!), Base64Url(secret))) == Path.GetFileName(path), "Nome incompatível");
            var decrypted = received.SelectMany(chunk => Decrypt(chunk, Base64Url(secret))).ToArray();
            Check(decrypted.SequenceEqual(bytes), "Download não recupera o arquivo original");
            Check(verified && percentages[0] == 0 && percentages[^1] == 100 && percentages.SequenceEqual(percentages.Order()), "Progresso ou confirmação inválidos");
            Check(received.Select(chunk => Convert.ToHexString(chunk[..16])).Distinct().Count() == chunks, "Salt reutilizado entre partes");
        }
    }

    foreach (string failure in new[] { "create", "put", "status", "incomplete", "cancel" })
    {
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        var percentages = new List<double>();
        using var cancellation = new CancellationTokenSource();
        using var client = new HttpClient(new Handler((request, ct) =>
        {
            if (request.Method == HttpMethod.Post)
                return Task.FromResult(failure == "create" ? new HttpResponseMessage(HttpStatusCode.TooManyRequests) : Json(new
                {
                    folderId = "folder", folderUrl = "https://file.kiwi/folder", uploadAuth = "auth",
                    files = new[] { new { fileId = "file", chunkSize = 3, chunks = 1, uploadUrls = new { head = "https://r2.example/", path = "prefix", tail = "signed=yes", signatures = new[] { "sig" }, headers = new { } } } }
                }));
            if (request.Method == HttpMethod.Put)
            {
                if (failure == "cancel") { cancellation.Cancel(); ct.ThrowIfCancellationRequested(); }
                return Task.FromResult(new HttpResponseMessage(failure == "put" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));
            }
            return Task.FromResult(failure == "status" ? new HttpResponseMessage(HttpStatusCode.BadGateway) : Json(new { complete = false, missing = new[] { 1 } }));
        }));
        bool rejected = false;
        try { await new FileKiwiUploadService(client).UploadAsync(path, new InlineProgress(percentages.Add), cancellation.Token); }
        catch (InvalidOperationException) when (failure != "cancel") { rejected = true; }
        catch (OperationCanceledException) when (failure == "cancel") { rejected = true; }
        Check(rejected && !percentages.Contains(100) && File.Exists(path), "Falha/cancelamento não preservou arquivo/estado");
    }
    Console.WriteLine("OK: vetor RFC 8188, descriptografia de SKE/nome/conteúdo, limites de registros, partes, progresso, confirmação, falhas e cancelamento.");
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
