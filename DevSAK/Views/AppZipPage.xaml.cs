using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using DevSAK.ViewModels;
using Windows.Storage.Pickers;
using Windows.System;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;

namespace DevSAK.Views
{
    public sealed partial class AppZipPage : Page
    {
        private readonly AppZipViewModel _viewModel;

        public AppZipPage()
        {
            this.InitializeComponent();
            _viewModel = new AppZipViewModel();
            _viewModel.ConfirmOverwriteRequested = ConfirmOverwriteAsync;
            this.DataContext = _viewModel;

            // Subscribe to compression completed event
            _viewModel.CompressionCompleted += OnCompressionCompleted;

            // Load saved settings if any
            try
            {
                _viewModel.LoadSettings();
            }
            catch { }
        }

        private async Task<AppZipOverwriteChoice> ConfirmOverwriteAsync(string zipName)
        {
            var dialog = new ContentDialog
            {
                Title = "Arquivo Existente",
                Content = $"O arquivo '{zipName}' já existe. O que deseja fazer?",
                PrimaryButtonText = "Sobrescrever",
                SecondaryButtonText = "Gerar nome aleatório",
                CloseButtonText = "Cancelar",
                DefaultButton = ContentDialogButton.Secondary,
                XamlRoot = this.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
                return AppZipOverwriteChoice.Overwrite;
            else if (result == ContentDialogResult.Secondary)
                return AppZipOverwriteChoice.GenerateRandom;
            else
                return AppZipOverwriteChoice.Cancel;
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            // Find the main window and go back to home
            var app = Application.Current as App;
            var mainWindow = app?.MainAppWindow as MainWindow;
            if (mainWindow != null)
            {
                mainWindow.GoBackToHome();
            }
        }

        // ============================================================
        // Browse Buttons
        // ============================================================

        private async void BrowseSourceButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");

            // Get the window handle for the picker
            var window = (Application.Current as App)?.MainAppWindow as MainWindow;
            if (window != null)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                _viewModel.SourceDirectory = folder.Path;
                _viewModel.AddRecentSourceDirectory(folder.Path);
            }
        }

        private async void BrowseDestinationButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");

            // Get the window handle for the picker
            var window = (Application.Current as App)?.MainAppWindow as MainWindow;
            if (window != null)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                _viewModel.DestinationDirectory = folder.Path;
                _viewModel.AddRecentDestinationDirectory(folder.Path);
            }
        }

        // ============================================================
        // Ignored Files List Management
        // ============================================================

        private void AddIgnoredFileButton_Click(object sender, RoutedEventArgs e) => AddIgnoredFileFromInput();

        private void IgnoredFilesInput_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                AddIgnoredFileFromInput();
            }
        }

        private void AddIgnoredFileFromInput()
        {
            var text = IgnoredFilesInput.Text?.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                _viewModel.AddIgnoredFile(text);
                IgnoredFilesInput.Text = string.Empty;
            }
        }

        private void RemoveIgnoredFileButton_Click(object sender, RoutedEventArgs e)
        {
            if (IgnoredFilesListView.SelectedItem is string item)
            {
                _viewModel.RemoveIgnoredFile(item);
            }
        }

        private void ClearIgnoredFilesButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.ClearIgnoredFiles();
        }

        // ============================================================
        // Ignored Folders List Management
        // ============================================================

        private void AddIgnoredFolderButton_Click(object sender, RoutedEventArgs e) => AddIgnoredFolderFromInput();

        private void IgnoredFoldersInput_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                AddIgnoredFolderFromInput();
            }
        }

        private void AddIgnoredFolderFromInput()
        {
            var text = IgnoredFoldersInput.Text?.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                _viewModel.AddIgnoredFolder(text);
                IgnoredFoldersInput.Text = string.Empty;
            }
        }

        private void RemoveIgnoredFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (IgnoredFoldersListView.SelectedItem is string item)
            {
                _viewModel.RemoveIgnoredFolder(item);
            }
        }

        private void ClearIgnoredFoldersButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.ClearIgnoredFolders();
        }

        // ============================================================
        // Ignored Extensions List Management
        // ============================================================

        private void AddIgnoredExtensionButton_Click(object sender, RoutedEventArgs e) => AddIgnoredExtensionFromInput();

        private void IgnoredExtensionsInput_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                AddIgnoredExtensionFromInput();
            }
        }

        private void AddIgnoredExtensionFromInput()
        {
            var text = IgnoredExtensionsInput.Text?.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                _viewModel.AddIgnoredExtension(text);
                IgnoredExtensionsInput.Text = string.Empty;
            }
        }

        private void RemoveIgnoredExtensionButton_Click(object sender, RoutedEventArgs e)
        {
            if (IgnoredExtensionsListView.SelectedItem is string item)
            {
                _viewModel.RemoveIgnoredExtension(item);
            }
        }

        private void ClearIgnoredExtensionsButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.ClearIgnoredExtensions();
        }

        // ============================================================
        // Execution Controls
        // ============================================================

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            await _viewModel.StartCompressionAsync();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.CancelCompression();
        }

        // ============================================================
        // Post-compression dialog
        // ============================================================

        private void OnCompressionCompleted(object? sender, string zipPath)
        {
            // Ensure we're on the UI thread
            DispatcherQueue.TryEnqueue(async () =>
            {
                await ShowPostCompressionDialogAsync(zipPath);
            });
        }

        private async Task ShowPostCompressionDialogAsync(string zipPath)
        {
            var panel = new StackPanel { Spacing = 16 };

            panel.Children.Add(new TextBlock
            {
                Text = "Compactação concluída com sucesso!",
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });

            panel.Children.Add(new TextBlock
            {
                Text = $"O arquivo foi salvo em:\n{zipPath}",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var dialog = new ContentDialog
            {
                Title = "Sucesso",
                Content = panel,
                PrimaryButtonText = "Abrir Arquivo",
                SecondaryButtonText = "Abrir Pasta",
                CloseButtonText = "Fechar",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();

            switch (result)
            {
                case ContentDialogResult.Primary:
                    OpenZipFile(zipPath);
                    break;
                case ContentDialogResult.Secondary:
                    OpenFolderAndSelectFile(zipPath);
                    break;
            }
        }

        private void OpenZipFile(string zipPath)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = zipPath,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to open ZIP: {ex.Message}");
            }
        }

        private void OpenFolderAndSelectFile(string zipPath)
        {
            try
            {
                Process.Start("explorer.exe", $"/select,\"{zipPath}\"");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to open folder: {ex.Message}");
            }
        }

    }
}
