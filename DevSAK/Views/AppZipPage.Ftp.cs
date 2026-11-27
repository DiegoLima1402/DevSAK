using DevSAK.Models;
using DevSAK.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DevSAK.Views;

public sealed partial class AppZipPage
{
    private readonly FtpDestinationStore _ftpStore = new();
    private bool _ftpDialogOpen;

    private async void LoadFtpDestinations(object sender, RoutedEventArgs e)
    {
        try { _viewModel.ReloadFtpDestinations(_ftpStore.Load()); }
        catch (Exception ex) { await ShowFtpAlertAsync("Erro ao carregar destinos FTP", ex.Message); }
    }

    private async void AddFtpDestination_Click(object sender, RoutedEventArgs e) => await EditFtpDestinationAsync(null);
    private async void EditFtpDestination_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedFtpDestination is { } destination) await EditFtpDestinationAsync(destination);
    }

    private async Task EditFtpDestinationAsync(FtpDestination? destination)
    {
        if (_ftpDialogOpen || _viewModel.IsProcessing) return;
        _ftpDialogOpen = true;
        try
        {
            var dialog = new FtpDestinationEditorDialog(_ftpStore, destination) { XamlRoot = XamlRoot, RequestedTheme = RequestedTheme };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.SavedDestination == null) return;
            _viewModel.ReloadFtpDestinations(_ftpStore.Load());
            _viewModel.SelectedFtpDestination = _viewModel.FtpDestinations.First(entry => entry.Id == dialog.SavedDestination.Id);
            _viewModel.SaveSettings();
        }
        catch (Exception ex) { await ShowFtpAlertAsync("Erro ao salvar destino FTP", ex.Message); }
        finally { _ftpDialogOpen = false; }
    }

    private async void DeleteFtpDestination_Click(object sender, RoutedEventArgs e)
    {
        if (_ftpDialogOpen || _viewModel.IsProcessing || _viewModel.SelectedFtpDestination is not { } destination) return;
        _ftpDialogOpen = true;
        try
        {
            var dialog = new ContentDialog
            {
                Title = "Excluir destino FTP",
                Content = $"Excluir '{destination.DisplayName}' do cadastro global? Arquivos no servidor serão preservados.",
                PrimaryButtonText = "Excluir", CloseButtonText = "Cancelar", DefaultButton = ContentDialogButton.Close, XamlRoot = XamlRoot
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            _ftpStore.Delete(destination.Id);
            _viewModel.ReloadFtpDestinations(_ftpStore.Load());
            _viewModel.SaveSettings();
        }
        catch (Exception ex) { await ShowFtpAlertAsync("Erro ao excluir destino FTP", ex.Message); }
        finally { _ftpDialogOpen = false; }
    }

    private Task<string> UploadFtpAsync(string zipPath, IProgress<double> progress, CancellationToken ct)
    {
        var destination = _viewModel.SelectedFtpDestination ?? throw new InvalidOperationException("Selecione um destino FTP.");
        return new FtpTransferService().UploadAsync(destination, _ftpStore.GetPassword(destination), zipPath, progress, ct, ConfirmFtpOverwriteAsync);
    }

    private async Task<bool> ConfirmFtpOverwriteAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var dialog = new ContentDialog
        {
            Title = "Arquivo já existe no FTP",
            Content = $"O arquivo '{path}' já existe em '{_viewModel.SelectedFtpDestination?.DisplayName}'. Deseja substituir o arquivo remoto?",
            PrimaryButtonText = "Substituir", CloseButtonText = "Cancelar", DefaultButton = ContentDialogButton.Close, XamlRoot = XamlRoot
        };
        using var registration = ct.Register(() => DispatcherQueue.TryEnqueue(() => dialog.Hide()));
        var result = await dialog.ShowAsync();
        ct.ThrowIfCancellationRequested();
        if (result == ContentDialogResult.Primary) return true;
        _viewModel.CancelCompression();
        return false;
    }

    private async Task ShowFtpAlertAsync(string title, string message)
    {
        await new ContentDialog
        {
            Title = title,
            Content = new ScrollViewer
            {
                MaxHeight = 400,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }
            },
            CloseButtonText = "Fechar", XamlRoot = XamlRoot
        }.ShowAsync();
    }
}
