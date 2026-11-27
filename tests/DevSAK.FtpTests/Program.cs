using DevSAK.Models;
using DevSAK.Services;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static void Reject(Action action)
{
    try { action(); }
    catch (Exception ex) when (ex is ArgumentException or System.Text.Json.JsonException or InvalidDataException) { return; }
    throw new Exception("Entrada inválida aceita");
}

var root = Path.Combine(Path.GetTempPath(), "devsak-ftp-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var store = new FtpDestinationStore(root);
    var first = store.Save(new FtpDestination { Host = "localhost", Description = " Produção ", UserName = "user", RemoteDirectory = "/deploy/./releases/../" }, "test-secret");
    var second = store.Save(new FtpDestination { Host = "127.0.0.1", UserName = "other", Security = FtpSecurity.ExplicitTls }, "second-secret");
    Check(first.DisplayName == "Produção" && second.DisplayName == "127.0.0.1" && first.RemoteDirectory == "/deploy", "Descrição ou pasta não normalizada");
    var filePath = Path.Combine(root, "ftp-destinations.json");
    string saved = File.ReadAllText(filePath);
    Check(!saved.Contains("test-secret") && !saved.Contains("second-secret") && !saved.Contains("DisplayName"), "Persistência expôs senha ou campo derivado");
    Check(new FtpDestinationStore(root).Load().Count == 2 && store.GetPassword(first) == "test-secret", "Destinos/senhas não persistiram");
    var edited = store.Save(first with { Description = "Staging", RemoteDirectory = "/pasta com espaço" }, "new-secret");
    Check(edited.Id == first.Id && store.Load().Count == 2 && store.GetPassword(edited) == "new-secret", "Edição perdeu ID ou criou cópia");
    store.Delete(edited.Id);
    Check(store.Load().Single().Id == second.Id, "Exclusão removeu outro destino");
    Reject(() => store.Save(second with { Host = "ftp://host/path" }, "secret"));
    Reject(() => store.Save(second with { Port = 0 }, "secret"));
    Reject(() => store.Save(second with { UserName = "user\r\nDELE /" }, "secret"));
    Reject(() => FtpDestinationValidation.NormalizePath("/test\nDELE /"));
    Check(FtpDestinationValidation.ParentPath("/one/two") == "/one" && FtpDestinationValidation.ParentPath("/") == "/", "Navegação acima da raiz");
    string original = File.ReadAllText(filePath);
    File.WriteAllText(filePath, "{invalid");
    Reject(() => store.Save(second, "secret"));
    Check(File.ReadAllText(filePath) == "{invalid", "Cadastro corrompido foi sobrescrito silenciosamente");
    File.WriteAllText(filePath, original);

    await using var server = new LocalFtpServer();
    var destination = new FtpDestination { Host = "127.0.0.1", Port = server.Port, UserName = "user", RemoteDirectory = "/deploy" };
    var service = new FtpTransferService();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    var listing = await service.ListDirectoriesAsync(destination, "test-secret", "/", timeout.Token);
    Check(listing.Path == "/" && listing.Directories.Select(entry => entry.Name).Contains("pasta com espaço"), "Não listou pastas com espaços");
    var child = await service.ListDirectoriesAsync(destination, "test-secret", "/deploy", timeout.Token);
    Check(child.Path == "/deploy" && child.Directories.Single().Path == "/deploy/releases", "Navegação não manteve caminho absoluto");
    var localPath = Path.Combine(root, "pacote com espaço.zip");
    var bytes = Enumerable.Range(0, 800000).Select(n => (byte)(n % 251)).ToArray();
    await File.WriteAllBytesAsync(localPath, bytes);
    var percentages = new List<double>();
    var remote = await service.UploadAsync(destination, "test-secret", localPath, new InlineProgress(percentages.Add), timeout.Token);
    Check(remote.EndsWith("/deploy/pacote com espaço.zip") && server.Files["/deploy/pacote com espaço.zip"].SequenceEqual(bytes), "Upload não usou pasta salva ou alterou bytes");
    Check(percentages[0] == 0 && percentages[^1] == 100 && percentages.SequenceEqual(percentages.Order()), "Progresso incorreto");
    bool prompted = false;
    try
    {
        await service.UploadAsync(destination, "test-secret", localPath, null, timeout.Token, (_, _) => { prompted = true; return Task.FromResult(false); });
        throw new Exception("Substituiu sem autorização");
    }
    catch (OperationCanceledException) { }
    Check(prompted && server.Files["/deploy/pacote com espaço.zip"].SequenceEqual(bytes), "Cancelamento da substituição alterou arquivo");
    await File.WriteAllBytesAsync(localPath, [9, 8, 7]);
    await service.UploadAsync(destination, "test-secret", localPath, null, timeout.Token, (_, _) => Task.FromResult(true));
    Check(server.Files["/deploy/pacote com espaço.zip"].SequenceEqual(new byte[] { 9, 8, 7 }), "Substituição autorizada não executada");

    try { await service.ListDirectoriesAsync(destination, "wrong-secret", "/", timeout.Token); throw new Exception("Login inválido aceito"); }
    catch (IOException ex) { Check(ex.Message.Contains("530") && ex.Message.Contains("Login incorrect"), "Perdeu erro técnico de autenticação"); }
    try { await service.ListDirectoriesAsync(destination, "test-secret", "/denied", timeout.Token); throw new Exception("Pasta sem permissão aceita"); }
    catch (IOException ex) { Check(ex.Message.Contains("550") && ex.Message.Contains("Permission denied"), "Perdeu erro técnico da pasta"); }
    server.DenyWrites = true;
    try { await service.UploadAsync(destination with { RemoteDirectory = "/" }, "test-secret", localPath, null, timeout.Token); throw new Exception("Upload sem permissão aceito"); }
    catch (IOException ex) { Check(ex.Message.Contains("550") && ex.Message.Contains("Write permission denied"), "Perdeu erro técnico de escrita"); }
    server.DenyWrites = false;
    server.DropUploadConnection = true;
    try { await service.UploadAsync(destination with { RemoteDirectory = "/" }, "test-secret", localPath, null, timeout.Token); throw new Exception("Conexão perdida ignorada"); }
    catch (IOException ex) { Check(ex.Message.Contains("Detalhes técnicos") && File.Exists(localPath), "Falha não preservou diagnóstico/ZIP"); }
    server.DropUploadConnection = false;

    await File.WriteAllBytesAsync(localPath, bytes);
    using var cancel = new CancellationTokenSource();
    try
    {
        await service.UploadAsync(destination with { RemoteDirectory = "/deploy/releases" }, "test-secret", localPath,
            new InlineProgress(percent => { if (percent > 0) cancel.Cancel(); }), cancel.Token);
        throw new Exception("Cancelamento ignorado");
    }
    catch (OperationCanceledException) { Check(File.Exists(localPath), "Cancelamento apagou ZIP local"); }
    Check(FtpTransferService.DescribeError(new Exception("PASS my-secret"), "FTP", "my-secret").Contains("[senha omitida]"), "Diagnóstico expôs senha");
    Console.WriteLine("OK: cadastro global, DPAPI, CRUD, validação, navegação e upload FTP reais em loopback, pasta salva, bytes, progresso, colisão, 530/550, perda de conexão e cancelamento.");
}
finally { Directory.Delete(root, true); }

sealed class InlineProgress(Action<double> report) : IProgress<double>
{
    public void Report(double value) => report(value);
}
