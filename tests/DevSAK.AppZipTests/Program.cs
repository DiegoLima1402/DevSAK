using System.IO.Compression;
using System.Text.Json;
using DevSAK.Models;
using DevSAK.Services;
using DevSAK.ViewModels;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
static void Reject(Action action, string message)
{
    try { action(); }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or InvalidDataException or JsonException) { return; }
    throw new Exception(message);
}

var root = Path.Combine(Path.GetTempPath(), "devsak-zip-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var store = new AppZipSettingsService(root);
    var legacy = new AppZipSettings
    {
        SourceDirectory = "origem", DestinationDirectory = "destino", ZipName = "deploy",
        IgnoredFiles = "web.config; appsettings.json", IgnoredFolders = "logs;temp", IgnoredExtensions = ".pdb;.log"
    };
    var legacyJson = JsonSerializer.Serialize(new
    {
        legacy.SourceDirectory, legacy.DestinationDirectory, legacy.ZipName,
        legacy.IgnoredFiles, legacy.IgnoredFolders, legacy.IgnoredExtensions
    });
    File.WriteAllText(store.SettingsFilePath, legacyJson);
    var settings = store.LoadSettings();
    var general = settings.IgnoreProfiles.Single();
    Check(general.Name == "GERAL" && general.IgnoredFiles.SequenceEqual(new[] { "web.config", "appsettings.json" }), "Migração de arquivos incorreta");
    Check(general.IgnoredFolders.SequenceEqual(new[] { "logs", "temp" }) && general.IgnoredExtensions.SequenceEqual(new[] { ".pdb", ".log" }), "Migração de pastas/extensões incorreta");
    Check(settings.SourceDirectory == "origem" && settings.ZipName == "deploy", "Migração alterou configurações de pacote");
    store.ClearCache();
    settings = await store.LoadSettingsAsync();
    Check(settings.IgnoreProfiles.Single().Id == general.Id, "Migração não é persistente/idempotente");

    var vm = new AppZipViewModel(store);
    vm.LoadSettings();
    Check(vm.IsUploadDisabled && !vm.IsStorageToSelected && !vm.IsFileKiwiSelected, "Padrão habilitou upload");
    var oldUploadStore = new AppZipSettingsService(Path.Combine(root, "legacy-upload"));
    File.WriteAllText(oldUploadStore.SettingsFilePath, "{\"UploadToStorageTo\":true}");
    var oldUploadVm = new AppZipViewModel(oldUploadStore);
    oldUploadVm.LoadSettings();
    Check(oldUploadVm.IsStorageToSelected, "Preferência antiga não migrou para StorageTo");
    oldUploadVm.IsFileKiwiSelected = true;
    oldUploadVm.IsStorageToSelected = false; // RadioButton unchecks the previous selection.
    Check(oldUploadVm.UploadProvider == AppZipUploadProvider.FileKiwi && !oldUploadVm.IsStorageToSelected && !oldUploadVm.IsUploadDisabled, "Seleção não é exclusiva");
    var reopenedUploadVm = new AppZipViewModel(new AppZipSettingsService(oldUploadStore.SettingsDirectory));
    reopenedUploadVm.LoadSettings();
    Check(reopenedUploadVm.IsFileKiwiSelected, "Escolha File.Kiwi não persistiu");
    reopenedUploadVm.IsUploadDisabled = true;
    Check(!reopenedUploadVm.IsFileKiwiSelected && !reopenedUploadVm.IsStorageToSelected, "Não desabilitou upload");
    vm.CreateIgnoreProfile(" NODE + REACT ");
    vm.AddIgnoredFolder("node_modules");
    vm.AddIgnoredExtension("map");
    vm.AddIgnoredFile(".env");
    var node = vm.SelectedIgnoreProfile!;
    vm.SelectedIgnoreProfile = vm.IgnoreProfiles.First(p => p.Name == "GERAL");
    Check(vm.IgnoredFilesList.Contains("web.config") && !vm.IgnoredFoldersList.Contains("node_modules"), "Regras vazaram entre perfis");
    vm.AddIgnoredFile("secret.json");
    vm.SelectedIgnoreProfile = node;
    Check(vm.IgnoredFoldersList.SequenceEqual(new[] { "node_modules" }) && vm.IgnoredExtensionsList.Contains(".map"), "Troca perdeu regras");
    Reject(() => vm.CreateIgnoreProfile("node + react"), "Permitiu nomes duplicados");
    Reject(() => vm.RenameIgnoreProfile(" "), "Permitiu nome vazio");
    vm.RenameIgnoreProfile("NODE");
    var exported = vm.ExportIgnoreProfile();
    Check(!exported.Contains("SourceDirectory") && !exported.Contains("\"Id\""), "Exportação expôs configurações locais");
    var roundTrip = AppZipProfileJson.Deserialize(exported);
    Check(roundTrip.Name == "NODE" && roundTrip.IgnoredFiles.Contains(".env"), "Round trip perdeu regras");
    Reject(() => vm.ImportIgnoreProfile(exported, false), "Colisão foi sobrescrita sem autorização");
    vm.AddIgnoredFile("temporary.txt");
    vm.ImportIgnoreProfile(exported, true);
    Check(vm.SelectedIgnoreProfile!.Id == node.Id && !vm.IgnoredFilesList.Contains("temporary.txt"), "Reimportação não substituiu corretamente");
    vm.AddIgnoredFile("after-import.txt");
    var fresh = new AppZipViewModel(new AppZipSettingsService(root));
    fresh.LoadSettings();
    Check(fresh.SelectedIgnoreProfile!.Name == "NODE" && fresh.IgnoredFilesList.Contains("after-import.txt"), "Reabertura perdeu seleção/regras");
    Check(fresh.IgnoreProfiles.First(p => p.Name == "GERAL").IgnoredFiles.Contains("secret.json"), "Edição anterior à troca não foi salva");

    foreach (var json in new[] { "null", "[]", "{}", "{", "{\"Name\":\"X\",\"Version\":2}", "{\"Name\":\"X\",\"IgnoredFiles\":null}", "{\"Name\":\"X\",\"IgnoredFiles\":[null]}", "{\"Name\":\"X\",\"IgnoredFiles\":\"foo\"}" })
        Reject(() => fresh.ImportIgnoreProfile(json, true), "JSON inválido aceito: " + json);
    Check(fresh.IgnoreProfiles.Count == 2 && fresh.IgnoredFilesList.Contains("after-import.txt"), "Importação inválida alterou perfis");
    var normalized = AppZipProfileJson.Deserialize("{\"name\":\" TESTE \",\"ignoredExtensions\":[\" log \",\".LOG\"]}");
    Check(normalized.Name == "TESTE" && normalized.IgnoredExtensions.SequenceEqual(new[] { ".log" }), "Normalização falhou");

    var source = Path.Combine(root, "source");
    Directory.CreateDirectory(Path.Combine(source, "node_modules"));
    foreach (var file in new[] { "app.js", "app.js.map", ".env", "web.config", "node_modules/dependency.js" })
        await File.WriteAllTextAsync(Path.Combine(source, file), "content");
    fresh.SourceDirectory = source;
    fresh.DestinationDirectory = Path.Combine(root, "output");
    fresh.UseAutomaticZipName = false;
    fresh.ZipName = "node";
    await fresh.StartCompressionAsync();
    using (var zip = ZipFile.OpenRead(Path.Combine(fresh.DestinationDirectory, "node.zip")))
        Check(zip.Entries.Select(e => e.FullName).Order().SequenceEqual(new[] { "app.js", "web.config" }), "ZIP não aplicou o perfil NODE");
    fresh.SelectedIgnoreProfile = fresh.IgnoreProfiles.First(p => p.Name == "GERAL");
    fresh.ZipName = "general";
    await fresh.StartCompressionAsync();
    using (var zip = ZipFile.OpenRead(Path.Combine(fresh.DestinationDirectory, "general.zip")))
        Check(!zip.Entries.Any(e => e.FullName == "web.config") && zip.Entries.Any(e => e.FullName == ".env"), "ZIP não aplicou o perfil GERAL");

    // Upload begins only after the ZIP is complete and delays the completion dialog.
    fresh.UploadToStorageTo = true;
    fresh.ZipName = "upload";
    bool uploaded = false, completed = false;
    fresh.UploadRequested = (zipPath, progress, ct) =>
    {
        Check(File.Exists(zipPath) && fresh.IsProcessing, "Upload começou antes do ZIP ou fora da operação");
        Check(fresh.Progress == 0 && fresh.Status == "Enviando arquivo...", "Progresso/status não reiniciou para upload");
        Check(!completed, "Diálogo apareceu antes do upload");
        uploaded = true;
        return Task.FromResult("https://storage.to/test12345");
    };
    fresh.CompressionCompleted += (_, _) => completed = true;
    await fresh.StartCompressionAsync();
    Check(uploaded && completed && fresh.UploadedUrl == "https://storage.to/test12345" && fresh.Progress == 100 && !fresh.IsProcessing, "Upload não concluiu corretamente");
    var uploadSettings = new AppZipViewModel(new AppZipSettingsService(root));
    uploadSettings.LoadSettings();
    Check(uploadSettings.UploadToStorageTo, "Preferência de upload não foi salva");

    fresh.IsFileKiwiSelected = true;
    fresh.ZipName = "filekiwi-upload";
    fresh.UploadRequested = (zipPath, progress, ct) =>
    {
        Check(fresh.UploadProvider == AppZipUploadProvider.FileKiwi && File.Exists(zipPath), "File.Kiwi não selecionado após ZIP");
        return Task.FromResult("https://file.kiwi/folder#secret");
    };
    await fresh.StartCompressionAsync();
    Check(fresh.UploadedUrl == "https://file.kiwi/folder#secret" && fresh.Progress == 100, "Fluxo ignorou File.Kiwi");

    var ftpDestination = new FtpDestination { Host = "ftp.example.test", UserName = "user", RemoteDirectory = "/deploy", ProtectedPassword = "encrypted-password-marker" };
    fresh.ReloadFtpDestinations(new[] { ftpDestination });
    fresh.SelectedFtpDestination = fresh.FtpDestinations.Single();
    fresh.IsFtpSelected = true;
    fresh.IsFileKiwiSelected = false;
    Check(fresh.IsFtpSelected && !fresh.IsFileKiwiSelected && !fresh.IsStorageToSelected, "FTP não é exclusivo");
    fresh.ZipName = "ftp-upload";
    fresh.FtpUploadRequested = (zipPath, progress, ct) =>
    {
        Check(File.Exists(zipPath) && fresh.SelectedFtpDestination?.RemoteDirectory == "/deploy" && fresh.Progress == 0, "FTP não recebeu ZIP/pasta/progresso esperado");
        return Task.FromResult("ftp.example.test:21/deploy/ftp-upload.zip");
    };
    await fresh.StartCompressionAsync();
    Check(fresh.UploadedFtpPath == "ftp.example.test:21/deploy/ftp-upload.zip" && fresh.UploadedUrl == null && fresh.Progress == 100, "FTP foi tratado como link compartilhável");
    Check(!File.ReadAllText(store.SettingsFilePath).Contains("encrypted-password-marker"), "Configurações ZIP duplicaram credenciais globais");
    var reopenedFtpVm = new AppZipViewModel(new AppZipSettingsService(root));
    reopenedFtpVm.LoadSettings();
    reopenedFtpVm.ReloadFtpDestinations(new[] { ftpDestination });
    Check(reopenedFtpVm.IsFtpSelected && reopenedFtpVm.SelectedFtpDestination?.Id == ftpDestination.Id, "Destino FTP selecionado não persistiu");
    reopenedFtpVm.ReloadFtpDestinations(new[] { new FtpDestination { Host = "other.example.test", UserName = "user" } });
    Check(reopenedFtpVm.SelectedFtpDestination == null, "Destino removido foi substituído silenciosamente por outro servidor");
    reopenedFtpVm.SourceDirectory = source;
    reopenedFtpVm.DestinationDirectory = Path.Combine(root, "no-ftp-output");
    reopenedFtpVm.ZipName = "no-destination";
    await reopenedFtpVm.StartCompressionAsync();
    Check(reopenedFtpVm.Status.Contains("destino FTP") && !Directory.Exists(reopenedFtpVm.DestinationDirectory), "Compactou sem selecionar destino FTP");
    fresh.ZipName = "ftp-error";
    fresh.FtpUploadRequested = (_, _, _) => throw new IOException("FtpCommandException [FTP 550]: Write permission denied");
    await fresh.StartCompressionAsync();
    Check(fresh.UploadError!.Contains("FTP 550") && fresh.UploadedFtpPath == null && fresh.UploadedUrl == null, "Erro técnico FTP foi perdido");
    fresh.IsFileKiwiSelected = true;

    fresh.ZipName = "upload-failure";
    fresh.UploadRequested = (_, _, _) => throw new InvalidOperationException("Falha simulada");
    await fresh.StartCompressionAsync();
    Check(fresh.UploadedUrl == null && fresh.UploadError!.Contains("Falha simulada") && File.Exists(Path.Combine(fresh.DestinationDirectory, "upload-failure.zip")), "Falha de upload perdeu ZIP ou manteve link antigo");

    fresh.ZipName = "upload-cancel";
    fresh.UploadRequested = (_, _, ct) =>
    {
        fresh.CancelCompression();
        ct.ThrowIfCancellationRequested();
        throw new Exception("Cancelamento não propagado");
    };
    await fresh.StartCompressionAsync();
    Check(fresh.UploadedUrl == null && fresh.UploadError!.Contains("cancelado") && !fresh.IsProcessing, "Cancelamento de upload incorreto");
    fresh.UploadToStorageTo = false;
    fresh.ZipName = "no-upload";
    fresh.UploadRequested = (_, _, _) => throw new Exception("Upload executado sem marcar opção");
    await fresh.StartCompressionAsync();
    Check(fresh.UploadedUrl == null && fresh.UploadError == null, "Operação comum manteve estado de upload");

    fresh.IsFileKiwiSelected = true;
    fresh.DeleteZipAfterUpload = true;
    fresh.ZipName = "recoverable";
    fresh.UploadRequested = (_, _, _) => throw new IOException("Falha de rede simulada");
    await fresh.StartCompressionAsync();
    var pending = fresh.PendingZipPath!;
    Check(File.Exists(pending) && !fresh.LocalZipDeleted, "Falha apagou ZIP antes de confirmar upload");
    var originalZip = File.ReadAllBytes(pending);
    fresh.ChooseAnotherUploadDestination();
    Check(fresh.IsChoosingUploadDestination && fresh.StartButtonText == "Enviar Arquivo" && fresh.CanCancel && !fresh.CanNavigate, "Modo de seleção de destino inconsistente");
    fresh.UploadRequested = (path, _, _) =>
    {
        Check(path == pending && File.ReadAllBytes(path).SequenceEqual(originalZip), "Nova tentativa recriou ou trocou o ZIP");
        return Task.FromResult("https://file.kiwi/retried#secret");
    };
    await fresh.UploadExistingZipAsync();
    Check(!File.Exists(pending) && fresh.LocalZipDeleted && fresh.UploadedUrl!.Contains("retried") && !fresh.IsChoosingUploadDestination && fresh.PendingZipPath == null, "Sucesso não excluiu ZIP ou não saiu da recuperação");

    fresh.ZipName = "cancel-recovery";
    fresh.UploadRequested = (_, _, _) => throw new IOException("Offline");
    await fresh.StartCompressionAsync();
    pending = fresh.PendingZipPath!;
    fresh.ChooseAnotherUploadDestination();
    fresh.CancelCompression();
    Check(File.Exists(pending) && fresh.PendingZipPath == null && fresh.IsNormalView && !fresh.CanCancel && fresh.StartButtonText == "Iniciar compactação", "Cancelamento perdeu ZIP ou não restaurou tela");
    fresh.DeleteZipAfterUpload = false;
    fresh.DeleteIgnoreProfile();
    Check(fresh.IgnoreProfiles.Count == 1 && fresh.SelectedIgnoreProfile!.Name == "NODE", "Exclusão/seleção incorreta");
    Reject(fresh.DeleteIgnoreProfile, "Excluiu último perfil");
    fresh.UploadToStorageTo = true;
    fresh.Clear();
    Check(fresh.IgnoreProfiles.Single().Name == "GERAL" && fresh.IgnoredFilesList.Count == 0 && !fresh.UploadToStorageTo, "Reset inconsistente");
    Check(fresh.FtpDestinations.Single().Id == ftpDestination.Id && fresh.SelectedFtpDestination == null, "Reset do compactador apagou cadastro global FTP ou manteve seleção");

    // If migration cannot be written, keep the loaded rules and the original file intact.
    var blockedStore = new AppZipSettingsService(Path.Combine(root, "blocked"));
    File.WriteAllText(blockedStore.SettingsFilePath, legacyJson);
    Directory.CreateDirectory(blockedStore.SettingsFilePath + ".tmp");
    var unsavedMigration = blockedStore.LoadSettings();
    Check(unsavedMigration.IgnoreProfiles.Single().IgnoredFiles.Contains("web.config"), "Falha de gravação descartou regras migradas");
    Check(File.ReadAllText(blockedStore.SettingsFilePath) == legacyJson, "Falha de gravação alterou o original");

    var asyncRoot = Path.Combine(root, "async");
    var asyncStore = new AppZipSettingsService(asyncRoot);
    await File.WriteAllTextAsync(asyncStore.SettingsFilePath, legacyJson);
    var migratedAsync = await asyncStore.LoadSettingsAsync();
    Check(migratedAsync.IgnoreProfiles.Single().Name == "GERAL", "Migração assíncrona falhou");
    Check(File.ReadAllText(asyncStore.SettingsFilePath).Contains("GERAL"), "Migração assíncrona não salva");
    Console.WriteLine("OK: migração síncrona/assíncrona, persistência, isolamento, CRUD, JSON, colisões, reset e ZIPs reais com dois perfis.");
}
finally { Directory.Delete(root, recursive: true); }
