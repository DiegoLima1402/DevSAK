using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using DevSAK.ViewModels;
using Windows.Storage.Pickers;
using Windows.System;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using CommunityToolkit.WinUI.Controls;

namespace DevSAK.Views
{
    public sealed partial class ExportPemPage : Page
    {
        private readonly ExportPemViewModel _viewModel;
        public ValidateCertificateViewModel ValidationViewModel { get; } = new();
        private CancellationTokenSource? _cts;
        private CancellationTokenSource? _valCts;

        public ExportPemPage()
        {
            InitializeComponent();
            _viewModel = new ExportPemViewModel();
            _viewModel.ConfirmOverwriteRequested = ConfirmOverwriteAsync;
            DataContext = _viewModel;
        }

        private async Task<bool> ConfirmOverwriteAsync()
        {
            var dialog = new ContentDialog
            {
                Title = "Confirmar Sobrescrita",
                Content = "O arquivo de destino já existe. Deseja sobrescrever?",
                PrimaryButtonText = "Sobrescrever",
                CloseButtonText = "Cancelar",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            var app = Application.Current as App;
            var mainWindow = app?.MainAppWindow as MainWindow;
            mainWindow?.GoBackToHome();
        }

        private void ModePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ModePicker == null || ExportSection == null || ValidationSection == null) return;
            
            if (ModePicker.SelectedIndex == 0)
            {
                ExportSection.Visibility = Visibility.Visible;
                ValidationSection.Visibility = Visibility.Collapsed;
            }
            else
            {
                ExportSection.Visibility = Visibility.Collapsed;
                ValidationSection.Visibility = Visibility.Visible;
            }
        }

        private async Task<Windows.Storage.StorageFile?> PickCertificateFileAsync()
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".pfx");
            picker.FileTypeFilter.Add(".p12");
            picker.FileTypeFilter.Add(".cer");
            picker.FileTypeFilter.Add(".crt");

            var window = (Application.Current as App)?.MainAppWindow as MainWindow;
            if (window != null)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            return await picker.PickSingleFileAsync();
        }

        private async void BrowseCertificateButton_Click(object sender, RoutedEventArgs e)
        {
            var file = await PickCertificateFileAsync();
            if (file != null)
            {
                _viewModel.InputCertificatePath = file.Path;
                _viewModel.AppendLog($"Arquivo selecionado: {file.Path}");
            }
        }

        private async void BrowseValidationCertificateButton_Click(object sender, RoutedEventArgs e)
        {
            var file = await PickCertificateFileAsync();
            if (file != null)
            {
                ValidationViewModel.InputCertificatePath = file.Path;
            }
        }

        private async void BrowseOutputFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");

            var window = (Application.Current as App)?.MainAppWindow as MainWindow;
            if (window != null)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                _viewModel.OutputFolder = folder.Path;
                _viewModel.AppendLog($"Pasta de saída: {folder.Path}");
            }
        }

        private async void RunButton_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            await _viewModel.RunAsync(_cts.Token);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
        }

        private async void ValidateButton_Click(object sender, RoutedEventArgs e)
        {
            _valCts?.Dispose();
            _valCts = new CancellationTokenSource();
            await ValidationViewModel.RunAsync(_valCts.Token);
        }

        private void ClearValidationButton_Click(object sender, RoutedEventArgs e)
        {
            ValidationViewModel.Clear();
        }

        private void CopyValidationSummaryButton_Click(object sender, RoutedEventArgs e)
        {
            var text = ValidationViewModel.GetSummary();
            if (!string.IsNullOrWhiteSpace(text))
            {
                var package = new DataPackage();
                package.SetText(text);
                Clipboard.SetContent(package);
            }
        }

        private void CopyThumbprintButton_Click(object sender, RoutedEventArgs e)
        {
            var text = ValidationViewModel.Thumbprint;
            if (!string.IsNullOrWhiteSpace(text) && text != "-")
            {
                var package = new DataPackage();
                package.SetText(text);
                Clipboard.SetContent(package);
            }
        }

        private void CopyLogButton_Click(object sender, RoutedEventArgs e)
        {
            var text = _viewModel.LogText ?? string.Empty;
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
            _viewModel.Status = "Log copiado para a área de transferência.";
        }

        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.ClearLog();
        }

        private void LogTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_viewModel.AutoScrollEnabled)
                return;

            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    var scrollViewer = FindDescendant<ScrollViewer>(LogTextBox);
                    scrollViewer?.ChangeView(null, scrollViewer.ScrollableHeight, null, disableAnimation: true);
                }
                catch
                {
                    // ignore
                }
            });
        }

        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T typed)
                    return typed;

                var deeper = FindDescendant<T>(child);
                if (deeper is not null)
                    return deeper;
            }

            return null;
        }
    }
}
