using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DevSAK.Models;
using DevSAK.Services;
using DevSAK.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DevSAK;

public sealed partial class MainWindow
{
    private readonly GitHubUpdateService _updates = new();
    private readonly CancellationTokenSource _updateLifetime = new();
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromHours(4) };
    private CancellationTokenSource? _downloadCancellation;
    private AppUpdate? _availableUpdate;
    private string? _downloadedInstaller;
    private Version? _lastNotifiedVersion;
    private bool _checkingUpdate;
    private bool _installingUpdate;
    private bool _updatesStarted;

    private void StartUpdateChecks()
    {
        if (_updatesStarted) return;
        _updatesStarted = true;
        _updateTimer.Tick += async (_, _) => await CheckForUpdatesAsync(false);
        Closed += (_, _) =>
        {
            _updateTimer.Stop();
            _updateLifetime.Cancel();
            _downloadCancellation?.Cancel();
        };
        _updateTimer.Start();
        _ = CheckForUpdatesAsync(false);
    }

    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(true);

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_checkingUpdate || _downloadCancellation != null || _installingUpdate || _updateLifetime.IsCancellationRequested) return;
        _checkingUpdate = true;
        CheckUpdatesButton.IsEnabled = false;
        try
        {
            var update = await _updates.CheckAsync(_releaseNotesService.CurrentVersion, _updateLifetime.Token);
            if (_updateLifetime.IsCancellationRequested) return;
            if (update == null)
            {
                if (manual) ShowUpdateMessage("Atualizações", "Você está usando a versão mais recente disponível.", InfoBarSeverity.Success);
                return;
            }
            if (_availableUpdate?.Version != update.Version)
            {
                DeleteDownloadedInstaller();
                _availableUpdate = update;
            }
            if (manual || _lastNotifiedVersion != update.Version)
            {
                _lastNotifiedVersion = update.Version;
                ShowUpdateMessage("Nova versão disponível", $"DevSAK {update.Version.ToString(3)} está disponível.", InfoBarSeverity.Informational,
                    _downloadedInstaller == null ? "Ver atualização" : "Instalar e reiniciar");
            }
        }
        catch (OperationCanceledException) when (_updateLifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"Atualizações: {ex}");
            if (manual) ShowUpdateMessage("Não foi possível verificar atualizações", ex is OperationCanceledException ? "A consulta demorou demais. Tente novamente." : ex.Message, InfoBarSeverity.Warning);
        }
        finally
        {
            _checkingUpdate = false;
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private void ShowUpdateMessage(string title, string message, InfoBarSeverity severity, string? action = null)
    {
        UpdateInfoBar.Title = title;
        UpdateInfoBar.Message = message;
        UpdateInfoBar.Severity = severity;
        UpdateInfoBar.IsOpen = true;
        UpdateActionButton.Content = action ?? "";
        UpdateActionButton.Visibility = action == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void UpdateActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_downloadCancellation != null) { _downloadCancellation.Cancel(); return; }
        if (_availableUpdate == null || _installingUpdate) return;
        UpdateActionButton.IsEnabled = false;
        try
        {
            if (!UpdateInstallerService.IsSupportedInstallation())
            {
                ShowUpdateMessage("Instalação manual necessária", "Instale a versão x64 pelo instalador oficial para habilitar atualizações automáticas. Builds de desenvolvimento, versões portáteis e MSIX devem ser atualizados manualmente.", InfoBarSeverity.Warning);
                return;
            }
            if (_downloadedInstaller != null) { await InstallUpdateAsync(); return; }
            var update = _availableUpdate;
            var panel = new StackPanel { Spacing = 12, MaxWidth = 520 };
            panel.Children.Add(new TextBlock
            {
                Text = $"Versão {update.Version.ToString(3)} • {update.Size / 1024d / 1024d:F1} MB\n\nApós o download, clique em Instalar e reiniciar. O DevSAK será fechado para instalar a atualização e aberto novamente. Operações em andamento precisam ser concluídas antes da instalação.",
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(new ScrollViewer
            {
                MaxHeight = 280,
                Content = new TextBlock { Text = update.Notes, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }
            });
            var consent = new ContentDialog
            {
                Title = "Atualizar o DevSAK?", Content = panel, PrimaryButtonText = "Baixar atualização",
                CloseButtonText = "Depois", DefaultButton = ContentDialogButton.Close, XamlRoot = RootGrid.XamlRoot,
                RequestedTheme = _currentTheme
            };
            if (await consent.ShowAsync() != ContentDialogResult.Primary) return;
            await DownloadUpdateAsync(update);
        }
        catch (OperationCanceledException)
        {
            if (!_updateLifetime.IsCancellationRequested)
                ShowUpdateMessage("Download interrompido", "O download foi cancelado ou excedeu o tempo limite. Você pode tentar novamente.", InfoBarSeverity.Informational, "Ver atualização");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Atualizações: {ex}");
            if (ex is InvalidDataException) DeleteDownloadedInstaller();
            ShowUpdateMessage("Não foi possível atualizar", ex.Message, InfoBarSeverity.Error,
                _downloadedInstaller == null ? "Tentar novamente" : "Instalar e reiniciar");
        }
        finally { UpdateActionButton.IsEnabled = true; }
    }

    private async Task DownloadUpdateAsync(AppUpdate update)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_updateLifetime.Token);
        _downloadCancellation = cancellation;
        UpdateInfoBar.IsClosable = false;
        UpdateProgress.Visibility = Visibility.Visible;
        UpdateProgress.Value = 0;
        CheckUpdatesButton.IsEnabled = false;
        UpdateActionButton.IsEnabled = true;
        ShowUpdateMessage("Baixando atualização", "Preparando download...", InfoBarSeverity.Informational, "Cancelar download");
        try
        {
            _downloadedInstaller = await _updates.DownloadAsync(update, new Progress<double>(value =>
            {
                if (_downloadCancellation != cancellation) return;
                UpdateProgress.Value = value;
                UpdateInfoBar.Message = $"{value:F0}% baixado. O instalador será verificado antes de executar.";
            }), cancellation.Token);
        }
        finally
        {
            _downloadCancellation = null;
            UpdateInfoBar.IsClosable = true;
            UpdateProgress.Visibility = Visibility.Collapsed;
            CheckUpdatesButton.IsEnabled = true;
        }
        if (_updateLifetime.IsCancellationRequested) return;
        // A separate explicit action avoids closing a tool or a dialog opened during download.
        ShowUpdateMessage("Atualização pronta", "Download verificado. Conclua suas operações e clique em Instalar e reiniciar.", InfoBarSeverity.Success, "Instalar e reiniciar");
    }

    private async Task InstallUpdateAsync()
    {
        if (OperationActivity.IsBusy)
        {
            ShowUpdateMessage("Aguardando suas operações", "Conclua as operações em andamento antes de instalar a atualização.", InfoBarSeverity.Warning, "Instalar e reiniciar");
            return;
        }
        _installingUpdate = true;
        CheckUpdatesButton.IsEnabled = false;
        // Disable tool input while saving settings and handing off to the installer.
        HomeScrollViewer.IsEnabled = false;
        AppFrame.IsEnabled = false;
        try
        {
            if (AppFrame.Content is FrameworkElement page)
            {
                switch (page.DataContext)
                {
                    case SmtpTestViewModel smtp: await smtp.SaveNowAsync(); break;
                    case AppZipViewModel zip: await zip.SaveSettingsAsync(); break;
                    case MySqlBackupRestoreViewModel mysql: await mysql.SavePreferencesForUpdateAsync(); break;
                }
            }
            await SaveCurrentThemeAsync(_currentTheme);
            if (OperationActivity.IsBusy) throw new InvalidOperationException("Uma operação foi iniciada. Aguarde sua conclusão e tente novamente.");
            await UpdateInstallerService.StartAsync(_downloadedInstaller!, _availableUpdate!, _updateLifetime.Token);
            Application.Current.Exit();
        }
        finally
        {
            _installingUpdate = false;
            CheckUpdatesButton.IsEnabled = true;
            HomeScrollViewer.IsEnabled = true;
            AppFrame.IsEnabled = true;
        }
    }

    private void DeleteDownloadedInstaller()
    {
        if (_downloadedInstaller == null) return;
        try { File.Delete(_downloadedInstaller); } catch { }
        _downloadedInstaller = null;
    }
}
