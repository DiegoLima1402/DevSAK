using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DevSAK.Models;

namespace DevSAK.ViewModels;

public partial class AppZipViewModel
{
    private bool _deleteZipAfterUpload;
    public bool DeleteZipAfterUpload
    {
        get => _deleteZipAfterUpload;
        set { _deleteZipAfterUpload = value; OnPropertyChanged(); }
    }
    public bool HasUploadDestination => UploadProvider != AppZipUploadProvider.None;
    public string? PendingZipPath { get; private set; }
    public bool LocalZipDeleted { get; private set; }
    public string? CleanupWarning { get; private set; }
    public bool IsChoosingUploadDestination { get; private set; }
    public bool IsNormalView => !IsChoosingUploadDestination;
    public bool CanCancel => IsProcessing || IsChoosingUploadDestination;
    public bool CanNavigate => !IsProcessing && !IsChoosingUploadDestination;
    public string StartButtonText => IsChoosingUploadDestination ? "Enviar Arquivo" : "Iniciar compactação";

    public void ChooseAnotherUploadDestination()
    {
        if (IsProcessing || PendingZipPath == null) return;
        IsChoosingUploadDestination = true;
        NotifyRecoveryState();
        Status = "Selecione o destino e clique em Enviar Arquivo.";
    }

    public void FinishUploadRecovery()
    {
        PendingZipPath = null;
        IsChoosingUploadDestination = false;
        NotifyRecoveryState();
    }

    private void NotifyRecoveryState()
    {
        OnPropertyChanged(nameof(IsChoosingUploadDestination));
        OnPropertyChanged(nameof(IsNormalView));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanNavigate));
        OnPropertyChanged(nameof(StartButtonText));
    }

    public async Task UploadExistingZipAsync()
    {
        if (IsProcessing || PendingZipPath == null) return;
        if (!HasUploadDestination || (IsFtpSelected && SelectedFtpDestination == null))
        { Status = "Selecione um destino de upload válido."; return; }
        var path = PendingZipPath;
        IsProcessing = true;
        _cancellationTokenSource = new CancellationTokenSource();
        var cancellation = _cancellationTokenSource;
        bool canceled;
        try { await SaveSettingsAsync(); await UploadZipCoreAsync(path); }
        finally
        {
            canceled = cancellation.IsCancellationRequested;
            IsProcessing = false;
            CurrentZipEntry = string.Empty;
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
        }
        if (canceled)
        {
            FinishUploadRecovery();
            Progress = 0;
            Status = "Pronto para iniciar";
            return;
        }
        OnCompressionCompleted(path);
    }

    private async Task UploadZipCoreAsync(string zipPath)
    {
        UploadedUrl = null;
        UploadedFtpPath = null;
        UploadError = null;
        LocalZipDeleted = false;
        CleanupWarning = null;
        PendingZipPath = zipPath;
        Progress = 0;
        CurrentZipEntry = Path.GetFileName(zipPath);
        Status = "Enviando arquivo...";
        var operation = _cancellationTokenSource!;
        var progress = new Progress<double>(percent =>
        {
            if (ReferenceEquals(operation, _cancellationTokenSource) && IsProcessing && UploadedUrl == null && UploadedFtpPath == null && UploadError == null)
                Progress = Math.Clamp(percent, 0, 99);
        });
        try
        {
            if (!File.Exists(zipPath)) throw new FileNotFoundException("O ZIP local não está mais disponível para envio.", zipPath);
            var upload = IsFtpSelected ? FtpUploadRequested : UploadRequested;
            if (upload == null) throw new InvalidOperationException("O serviço de upload não está disponível.");
            var result = await upload(zipPath, progress, operation.Token);
            if (string.IsNullOrWhiteSpace(result)) throw new IOException("O destino não confirmou o envio do arquivo.");
            if (IsFtpSelected) UploadedFtpPath = result;
            else UploadedUrl = result;
            Progress = 100;
            Status = "Upload concluído com sucesso!";
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        { UploadError = "Upload cancelado. O ZIP foi preservado no diretório de destino."; Status = UploadError; return; }
        catch (Exception ex)
        { UploadError = $"O ZIP foi criado, mas o upload falhou: {ex.Message}"; Status = UploadError; return; }

        if (DeleteZipAfterUpload)
        {
            try { File.Delete(zipPath); LocalZipDeleted = true; }
            catch (Exception ex) { CleanupWarning = $"O upload foi concluído, mas não foi possível excluir o ZIP local: {ex.Message}"; }
        }
        FinishUploadRecovery();
    }
}
