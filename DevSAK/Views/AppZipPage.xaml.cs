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
using System.Linq;
using System.Collections.Generic;
using DevSAK.Services;
using Windows.ApplicationModel.DataTransfer;

namespace DevSAK.Views
{
    public sealed partial class AppZipPage : Page
    {
        private readonly AppZipViewModel _viewModel;

        private void EditorScroll_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_viewModel?.IsChoosingUploadDestination != true && Workspace.ActualWidth >= 850)
                EditorGrid.Height = Math.Max(620, e.NewSize.Height);
        }

        private void Workspace_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyWorkspaceLayout();
        }

        private void ApplyWorkspaceLayout()
        {
            bool recovery = _viewModel?.IsChoosingUploadDestination == true;
            bool wide = Workspace.ActualWidth >= 1350;
            bool narrow = recovery || Workspace.ActualWidth < 850;
            Workspace.ColumnDefinitions[0].Width = new GridLength(3, GridUnitType.Star);
            Workspace.ColumnDefinitions[1].Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            Grid.SetColumn(ExecutionSection, wide ? 1 : 0);
            Grid.SetRow(ExecutionSection, wide ? 0 : 1);
            Grid.SetColumnSpan(ExecutionSection, wide ? 1 : 2);
            ExecutionSection.MaxHeight = Math.Max(180, Workspace.ActualHeight - 48);
            Grid.SetColumnSpan(EditorScroll, wide ? 1 : 2);

            Grid.SetColumn(RulesSection, narrow ? 0 : 1);
            Grid.SetRow(RulesSection, narrow ? 1 : 0);
            EditorGrid.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(3, GridUnitType.Star);
            EditorGrid.RowDefinitions[0].Height = narrow ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
            EditorGrid.Height = narrow ? double.NaN : Math.Max(620, EditorScroll.ActualHeight);
            RulesSection.Height = narrow ? 620 : double.NaN;
            ExecutionSummary.Visibility = wide && !recovery ? Visibility.Visible : Visibility.Collapsed;

            ExecutionGrid.ColumnDefinitions.Clear();
            ExecutionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            if (!wide)
                ExecutionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 180 });
            Grid.SetColumn(ExecutionActions, wide ? 0 : 1);
            Grid.SetRow(ExecutionActions, wide ? 2 : 0);
            Grid.SetRowSpan(ExecutionActions, wide ? 1 : 3);
            ExecutionActions.VerticalAlignment = wide ? VerticalAlignment.Stretch : VerticalAlignment.Center;
        }

        public AppZipPage()
        {
            this.InitializeComponent();
            _viewModel = new AppZipViewModel();
            _viewModel.FtpUploadRequested = UploadFtpAsync;
            _viewModel.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(AppZipViewModel.IsChoosingUploadDestination)) ApplyWorkspaceLayout(); };
            Loaded += LoadFtpDestinations;
            _viewModel.ConfirmOverwriteRequested = ConfirmOverwriteAsync;
            _viewModel.UploadRequested = (path, progress, ct) =>
            {
                if (_viewModel.UploadProvider == DevSAK.Models.AppZipUploadProvider.FileKiwi)
                    return new FileKiwiUploadService().UploadAsync(path, progress, ct);
                var settings = new StorageToSettingsStore();
                return new StorageToUploadService().UploadAsync(path, settings.LoadApiKey(), settings.GetVisitorToken(), progress, ct);
            };
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

        private void TodayButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.StartDateOffset = new DateTimeOffset(DateTime.Today);
        }

        private async void NewProfile_Click(object sender, RoutedEventArgs e) => await EditProfileNameAsync(false);

        private async void RenameProfile_Click(object sender, RoutedEventArgs e) => await EditProfileNameAsync(true);

        private async Task EditProfileNameAsync(bool rename)
        {
            var input = new TextBox
            {
                Header = "Nome do perfil",
                PlaceholderText = "Ex.: ASP NET ou NODE + REACT",
                Text = rename ? _viewModel.SelectedIgnoreProfile?.Name ?? string.Empty : string.Empty
            };
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(input);
            panel.Children.Add(error);
            var dialog = new ContentDialog
            {
                Title = rename ? "Renomear perfil" : "Novo perfil",
                Content = panel,
                PrimaryButtonText = "Salvar",
                CloseButtonText = "Cancelar",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };
            dialog.PrimaryButtonClick += (_, args) =>
            {
                try
                {
                    if (rename) _viewModel.RenameIgnoreProfile(input.Text);
                    else _viewModel.CreateIgnoreProfile(input.Text);
                }
                catch (Exception ex) { error.Text = ex.Message; args.Cancel = true; }
            };
            await dialog.ShowAsync();
        }

        private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.SelectedIgnoreProfile == null) return;
            var dialog = new ContentDialog
            {
                Title = "Excluir perfil",
                Content = $"Excluir o perfil '{_viewModel.SelectedIgnoreProfile.Name}' e suas regras?",
                PrimaryButtonText = "Excluir",
                CloseButtonText = "Cancelar",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            try { _viewModel.DeleteIgnoreProfile(); }
            catch (Exception ex) { await ShowProfileErrorAsync(ex); }
        }

        private async void ImportProfile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker();
                picker.FileTypeFilter.Add(".json");
                InitializeProfilePicker(picker);
                var file = await picker.PickSingleFileAsync();
                if (file == null) return;
                var json = await File.ReadAllTextAsync(file.Path);
                var imported = AppZipProfileJson.Deserialize(json);
                bool replace = false;
                if (_viewModel.IgnoreProfiles.Any(profile => profile.Name.Equals(imported.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    var dialog = new ContentDialog
                    {
                        Title = "Perfil já existente",
                        Content = $"Substituir as regras do perfil '{imported.Name}' pelas regras do arquivo?",
                        PrimaryButtonText = "Substituir",
                        CloseButtonText = "Cancelar",
                        DefaultButton = ContentDialogButton.Close,
                        XamlRoot = XamlRoot
                    };
                    if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
                    replace = true;
                }
                _viewModel.ImportIgnoreProfile(json, replace);
                _viewModel.Status = $"Perfil '{imported.Name}' importado com sucesso.";
            }
            catch (Exception ex) { await ShowProfileErrorAsync(ex); }
        }

        private async void ExportProfile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var name = _viewModel.SelectedIgnoreProfile?.Name ?? "Perfil";
                var safeName = string.Concat(name.Select(c => Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c)).TrimEnd('.', ' ');
                var picker = new FileSavePicker { SuggestedFileName = string.IsNullOrEmpty(safeName) ? "Perfil" : safeName };
                picker.FileTypeChoices.Add("Perfil de regras ZIP", new List<string> { ".json" });
                InitializeProfilePicker(picker);
                var file = await picker.PickSaveFileAsync();
                if (file == null) return;
                await File.WriteAllTextAsync(file.Path, _viewModel.ExportIgnoreProfile());
                _viewModel.Status = $"Perfil '{name}' exportado com sucesso.";
            }
            catch (Exception ex) { await ShowProfileErrorAsync(ex); }
        }

        private static void InitializeProfilePicker(object picker)
        {
            var window = (Application.Current as App)?.MainAppWindow as MainWindow;
            if (window != null)
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
        }

        private async Task ShowProfileErrorAsync(Exception ex)
        {
            await new ContentDialog
            {
                Title = "Não foi possível alterar o perfil",
                Content = ex.Message,
                CloseButtonText = "Fechar",
                XamlRoot = XamlRoot
            }.ShowAsync();
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
            if (_viewModel.IsFtpSelected && _viewModel.SelectedFtpDestination == null)
            {
                await ShowFtpAlertAsync("Destino FTP necessário", "Selecione um destino FTP ou use o botão + para cadastrar um servidor e sua pasta de destino.");
                return;
            }
            if (_viewModel.IsChoosingUploadDestination) await _viewModel.UploadExistingZipAsync();
            else await _viewModel.StartCompressionAsync();
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
            var uploadedUrl = _viewModel.UploadedUrl;
            var uploadError = _viewModel.UploadError;
            var ftpPath = _viewModel.UploadedFtpPath;
            // Ensure we're on the UI thread
            DispatcherQueue.TryEnqueue(async () =>
            {
                await ShowPostCompressionDialogAsync(zipPath, uploadedUrl, uploadError, ftpPath);
            });
        }

        private async Task ShowPostCompressionDialogAsync(string zipPath, string? uploadedUrl, string? uploadError, string? ftpPath)
        {
            if (uploadError != null)
            {
                var failure = new ContentDialog
                {
                    Title = "Upload não concluído",
                    Content = new ScrollViewer { MaxHeight = 400, Content = new TextBlock { Text = uploadError, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true } },
                    PrimaryButtonText = "Tentar Novamente", SecondaryButtonText = "Selecionar Outro Destino", CloseButtonText = "Fechar", XamlRoot = XamlRoot
                };
                // Allow enough room for the three actions, respecting the available window width.
                failure.Resources["ContentDialogMaxWidth"] = 720d;
                failure.Resources["ContentDialogMinWidth"] = Math.Min(680d, Math.Max(0d, XamlRoot.Size.Width - 48d));
                var choice = await failure.ShowAsync();
                if (choice == ContentDialogResult.Primary) await _viewModel.UploadExistingZipAsync();
                else if (choice == ContentDialogResult.Secondary) _viewModel.ChooseAnotherUploadDestination();
                else _viewModel.FinishUploadRecovery();
                return;
            }
            var panel = new StackPanel { Spacing = 16 };

            panel.Children.Add(new TextBlock
            {
                Text = uploadError != null ? "ZIP criado; upload não concluído." : uploadedUrl != null || ftpPath != null ? "Compactação e upload concluídos!" : "Compactação concluída com sucesso!",
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });

            panel.Children.Add(new TextBlock
            {
                Text = _viewModel.LocalZipDeleted ? "O ZIP local foi excluído após o upload confirmado." : $"O arquivo foi salvo em:\n{zipPath}",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });

            if (uploadedUrl != null || uploadError != null)
                panel.Children.Add(new TextBlock { Text = uploadedUrl ?? uploadError, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
            if (ftpPath != null)
                panel.Children.Add(new TextBlock { Text = $"Enviado para o FTP:\n{ftpPath}", TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });

            if (_viewModel.CleanupWarning != null)
                panel.Children.Add(new TextBlock { Text = _viewModel.CleanupWarning, TextWrapping = TextWrapping.Wrap });
            var dialog = new ContentDialog
            {
                Title = uploadError == null ? "Sucesso" : "Upload não concluído",
                Content = new ScrollViewer { Content = panel, MaxHeight = 450, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
                PrimaryButtonText = uploadedUrl != null ? "Copiar Link" : _viewModel.DeleteZipAfterUpload && ftpPath != null ? string.Empty : "Abrir Arquivo",
                SecondaryButtonText = _viewModel.DeleteZipAfterUpload && (uploadedUrl != null || ftpPath != null) ? string.Empty : "Abrir Pasta",
                CloseButtonText = "Fechar",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();

            switch (result)
            {
                case ContentDialogResult.Primary:
                    if (uploadedUrl != null)
                    {
                        try
                        {
                            var data = new DataPackage();
                            data.SetText(uploadedUrl);
                            Clipboard.SetContent(data);
                            Clipboard.Flush();
                            _viewModel.Status = "Link copiado!";
                        }
                        catch { _viewModel.Status = "Não foi possível copiar o link. Tente novamente."; }
                    }
                    else OpenZipFile(zipPath);
                    break;
                case ContentDialogResult.Secondary:
                    if (_viewModel.LocalZipDeleted) await Launcher.LaunchFolderPathAsync(Path.GetDirectoryName(zipPath)!);
                    else OpenFolderAndSelectFile(zipPath);
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
                ExplorerFolderService.OpenAndSelectFile(zipPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to open folder: {ex.Message}");
            }
        }

    }
}
