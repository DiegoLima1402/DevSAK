using System;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using DevSAK.Models;
using DevSAK.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace DevSAK.Views
{
    public sealed partial class MySqlBackupRestorePage : Page
    {
        private readonly MySqlBackupRestoreViewModel _viewModel;
        private bool _isAutoScrollEnabled = true;
        private bool _isRestoreAutoScrollEnabled = true;
        private bool _isLoadingDatabasesFromCombo;
        private bool _isRestoreDbComboFocusHandled;
        private ScrollViewer? _outputConsoleScrollViewer;
        private ScrollViewer? _restoreOutputConsoleScrollViewer;

        public MySqlBackupRestorePage()
        {
            _viewModel = new MySqlBackupRestoreViewModel();
            InitializeComponent();
            DataContext = _viewModel;
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;

            // Give the ViewModel a DispatcherQueue reference so that any log or property
            // update fired from a background thread is safely marshalled to the UI thread.
            _viewModel.SetDispatcherQueue(DispatcherQueue);
            ActualThemeChanged += MySqlBackupRestorePage_ActualThemeChanged;

            Loaded += MySqlBackupRestorePage_Loaded;
        }

        private async void MySqlBackupRestorePage_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= MySqlBackupRestorePage_Loaded;
            SyncThemeToViewModel();
            await _viewModel.InitializeAsync();
            var cliWarning = _viewModel.GetCliWarningMessage();
            if (!string.IsNullOrWhiteSpace(cliWarning))
            {
                await ShowInfoDialogAsync("Aviso", cliWarning);
            }

            UpdateAutoScrollToggleVisualState();
            UpdateRestoreAutoScrollToggleVisualState();
            UpdateBusyUiState();

            ModePicker.SelectedIndex = _viewModel.IsRestoreMode ? 1 : 0;
            
            if (ModePicker.SelectedIndex == 0)
            {
                BackupSection.Visibility = Visibility.Visible;
                RestoreSection.Visibility = Visibility.Collapsed;
            }
            else
            {
                BackupSection.Visibility = Visibility.Collapsed;
                RestoreSection.Visibility = Visibility.Visible;
            }
            
            EnsureOutputConsoleScrollViewer();
            EnsureRestoreOutputConsoleScrollViewer();
        }

        private void MySqlBackupRestorePage_ActualThemeChanged(FrameworkElement sender, object args)
        {
            SyncThemeToViewModel();
        }

        private void OutputConsoleTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isAutoScrollEnabled || OutputConsoleTextBox is null)
            {
                return;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                ScrollOutputToBottom();
            });
        }

        private void AutoScrollToggleButton_Checked(object sender, RoutedEventArgs e)
        {
            _isAutoScrollEnabled = true;
            UpdateAutoScrollToggleVisualState();
            ScrollOutputToBottom();
        }

        private void AutoScrollToggleButton_Unchecked(object sender, RoutedEventArgs e)
        {
            _isAutoScrollEnabled = false;
            UpdateAutoScrollToggleVisualState();
        }

        private async void CopyOutputButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_viewModel.OutputLog))
            {
                await ShowInfoDialogAsync("Sem conteúdo", "Não há texto no console para copiar.");
                return;
            }

            var dataPackage = new DataPackage();
            dataPackage.SetText(_viewModel.OutputLog);
            Clipboard.SetContent(dataPackage);
            Clipboard.Flush();

            await ShowInfoDialogAsync("Saída copiada", "O conteúdo do console foi copiado para a área de transferência.");
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if ((Application.Current as App)?.MainAppWindow is MainWindow mainWindow)
            {
                mainWindow.GoBackToHome();
            }
        }

        private async void AddConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            var profile = new MySqlConnectionProfile();
            var result = await ShowConnectionEditorDialogAsync(profile, isEditing: false);
            if (result is not null)
            {
                await _viewModel.AddOrUpdateConnectionAsync(result);
            }
        }

        private async void EditConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.SelectedConnection is null)
            {
                return;
            }

            var clone = _viewModel.SelectedConnection.Clone();
            var result = await ShowConnectionEditorDialogAsync(clone, isEditing: true);
            if (result is not null)
            {
                await _viewModel.AddOrUpdateConnectionAsync(result);
            }
        }

        private async void RemoveConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.SelectedConnection is null)
            {
                return;
            }

            var dialog = new ContentDialog
            {
                Title = "Remover conexão",
                Content = $"Deseja remover a conexão \"{_viewModel.SelectedConnection.FriendlyName}\"?",
                PrimaryButtonText = "Remover",
                CloseButtonText = "Cancelar",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await _viewModel.DeleteSelectedConnectionAsync();
            }
        }

        private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            await _viewModel.TestSelectedConnectionAsync();
        }

        /// <summary>
        /// Shared handler for both the Backup and Restore server ComboBoxes.
        /// Fires immediately when the user picks a server, auto-testing the connection
        /// and loading the database list — no manual button click required.
        /// </summary>
        private async void ConnectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            await OnServerSelectionChangedAsync();
        }

        private async Task OnServerSelectionChangedAsync()
        {
            // Bail out if no server is selected, or if a load is already in flight.
            if (_viewModel.SelectedConnection is null ||
                _isLoadingDatabasesFromCombo ||
                _viewModel.IsBusy)
            {
                return;
            }

            // Reset the DB ComboBox focus guard so the new server gets a fresh session.
            _isRestoreDbComboFocusHandled = false;

            try
            {
                _isLoadingDatabasesFromCombo = true;
                await _viewModel.TestSelectedConnectionAsync();
            }
            finally
            {
                _isLoadingDatabasesFromCombo = false;
            }
        }

        private async void DatabaseComboBox_DropDownOpened(object sender, object e)
        {
            if (_isLoadingDatabasesFromCombo ||
                _viewModel.IsBusy ||
                _viewModel.SelectedConnection is null ||
                _viewModel.IsSelectedConnectionValidated)
            {
                return;
            }

            try
            {
                _isLoadingDatabasesFromCombo = true;
                await _viewModel.TestSelectedConnectionAsync();
            }
            finally
            {
                _isLoadingDatabasesFromCombo = false;
            }
        }

        private async void RefreshBackupDatabasesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _viewModel.RefreshDatabasesForBackupAsync();
            }
            catch (Exception ex)
            {
                await ShowInfoDialogAsync("Falha ao atualizar bancos", ex.Message);
            }
        }

        private void ModePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_viewModel is null || ModePicker is null || BackupSection is null || RestoreSection is null)
            {
                return;
            }

            if (ModePicker.SelectedIndex == 0)
            {
                _viewModel.Mode = MySqlBackupRestoreMode.Backup;
                BackupSection.Visibility = Visibility.Visible;
                RestoreSection.Visibility = Visibility.Collapsed;
            }
            else if (ModePicker.SelectedIndex == 1)
            {
                _viewModel.Mode = MySqlBackupRestoreMode.Restore;
                BackupSection.Visibility = Visibility.Collapsed;
                RestoreSection.Visibility = Visibility.Visible;
            }
        }

        private async void BrowseDestinationFileButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            
            InitializePicker(picker);

            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null)
            {
                _viewModel.OutputFolder = folder.Path;

                if (string.IsNullOrWhiteSpace(_viewModel.OutputFileNameBase))
                {
                    _viewModel.OutputFileNameBase = !string.IsNullOrWhiteSpace(_viewModel.SelectedDatabase)
                        ? _viewModel.SelectedDatabase
                        : "backup_mysql";
                }
            }
        }

        private async void BrowseSourceFileButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".sql");
            picker.FileTypeFilter.Add(".zip");
            picker.FileTypeFilter.Add(".rar");
            picker.FileTypeFilter.Add(".7z");
            picker.FileTypeFilter.Add("*");
            InitializePicker(picker);

            var file = await picker.PickSingleFileAsync();
            if (file is not null)
            {
                _viewModel.SourceSqlFile = file.Path;
            }
        }

        private async void BackupStartButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.Mode = MySqlBackupRestoreMode.Backup;
            await ExecuteStartAsync();
        }

        private async void RestoreStartButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.Mode = MySqlBackupRestoreMode.Restore;
            await ExecuteStartAsync();
        }

        private async Task ExecuteStartAsync()
        {
            CommitRestoreDatabaseSelection();

            try
            {
                _viewModel.ValidateBeforeExecution();
            }
            catch (Exception ex)
            {
                await ShowInfoDialogAsync("Falha na operação", ex.Message);
                return;
            }

            if (_viewModel.IsBackupMode && System.IO.File.Exists(_viewModel.DestinationSqlFile))
            {
                var overwriteDialog = new ContentDialog
                {
                    Title = "Arquivo existente",
                    Content = "O arquivo de destino já existe. Deseja substituí-lo?",
                    PrimaryButtonText = "Sobrescrever o arquivo",
                    CloseButtonText = "Cancelar",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = XamlRoot
                };

                if (await overwriteDialog.ShowAsync() != ContentDialogResult.Primary)
                {
                    _viewModel.SetCancelledState();
                    return;
                }
            }

            var confirmDialog = new ContentDialog
            {
                Title = _viewModel.IsBackupMode ? "Confirmar backup" : "Confirmar restauração",
                Content = new TextBlock
                {
                    Text = _viewModel.BuildConfirmationMessage(),
                    TextWrapping = TextWrapping.Wrap
                },
                PrimaryButtonText = _viewModel.IsBackupMode ? "Confirmar backup" : "Confirmar restauração",
                CloseButtonText = "Cancelar",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            if (await confirmDialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            try
            {
                await _viewModel.ExecuteAsync();
            }
            catch (Exception ex)
            {
                await ShowInfoDialogAsync("Falha na operação", ex.Message);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.CancelOperation();
        }

        private void RestoreCancelButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.CancelOperation();
        }

        private async void RestoreDatabaseComboBox_DropDownOpened(object sender, object e)
        {
            if (_isLoadingDatabasesFromCombo ||
                _viewModel.IsBusy ||
                _viewModel.SelectedConnection is null ||
                _viewModel.IsSelectedConnectionValidated)
            {
                return;
            }

            try
            {
                _isLoadingDatabasesFromCombo = true;
                await _viewModel.TestSelectedConnectionAsync();
            }
            finally
            {
                _isLoadingDatabasesFromCombo = false;
            }
        }

        private async void RefreshRestoreDatabasesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CommitRestoreDatabaseSelection();
                await _viewModel.RefreshDatabasesForRestoreAsync();
            }
            catch (Exception ex)
            {
                await ShowInfoDialogAsync("Falha ao atualizar bancos", ex.Message);
            }
        }

        private async void RestoreDatabaseComboBox_GotFocus(object sender, RoutedEventArgs e)
        {
            // Mirror the same guard used by DropDownOpened: trigger connection test + DB list
            // only on the first focus while the connection has not been validated yet.
            if (_isLoadingDatabasesFromCombo ||
                _isRestoreDbComboFocusHandled ||
                _viewModel.IsBusy ||
                _viewModel.SelectedConnection is null ||
                _viewModel.IsSelectedConnectionValidated)
            {
                return;
            }

            _isRestoreDbComboFocusHandled = true;
            try
            {
                _isLoadingDatabasesFromCombo = true;
                await _viewModel.TestSelectedConnectionAsync();
            }
            finally
            {
                _isLoadingDatabasesFromCombo = false;
            }
        }

        private async void RestoreDatabaseComboBox_TextSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args)
        {
            // Always commit the typed text to the ViewModel so that BuildConfirmationMessage
            // and ValidateBeforeExecution see the latest value.
            _viewModel.SelectedDatabase = sender.Text?.Trim();

            if (_viewModel.CanCreateDatabase && _viewModel.ShowCreateDatabaseButton)
            {
                args.Handled = true;
                await TryCreateDatabaseAsync();
            }
        }

        private void RestoreDatabaseComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox comboBox)
            {
                // Prefer the explicitly selected item from the list; fall back to typed text.
                _viewModel.SelectedDatabase = comboBox.SelectedItem as string ?? comboBox.Text?.Trim();
            }
        }

        private async void CreateDatabaseButton_Click(object sender, RoutedEventArgs e)
        {
            await TryCreateDatabaseAsync();
        }

        private async Task TryCreateDatabaseAsync()
        {
            try
            {
                CommitRestoreDatabaseSelection();
                await _viewModel.CreateRestoreDatabaseAsync();
            }
            catch (Exception ex)
            {
                await ShowInfoDialogAsync("Falha ao criar banco de dados", ex.Message);
            }
        }

        private void RestoreOutputConsoleTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isRestoreAutoScrollEnabled || RestoreOutputConsoleTextBox is null)
            {
                return;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                ScrollRestoreOutputToBottom();
            });
        }

        private void RestoreAutoScrollToggleButton_Checked(object sender, RoutedEventArgs e)
        {
            _isRestoreAutoScrollEnabled = true;
            UpdateRestoreAutoScrollToggleVisualState();
            ScrollRestoreOutputToBottom();
        }

        private void RestoreAutoScrollToggleButton_Unchecked(object sender, RoutedEventArgs e)
        {
            _isRestoreAutoScrollEnabled = false;
            UpdateRestoreAutoScrollToggleVisualState();
        }

        private async Task<MySqlConnectionProfile?> ShowConnectionEditorDialogAsync(MySqlConnectionProfile profile, bool isEditing)
        {
            var nameBox = new TextBox { Header = "Nome da conexão", Text = profile.FriendlyName };
            var hostBox = new TextBox { Header = "Host", Text = profile.Host };
            var portBox = new NumberBox { Header = "Porta", Value = profile.Port, SmallChange = 1, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
            var userBox = new TextBox { Header = "Usuário", Text = profile.UserName };
            var passwordBox = new PasswordBox { Header = "Senha", Password = profile.Password };
            var databaseBox = new TextBox { Header = "Banco padrão opcional", Text = profile.DefaultDatabase ?? string.Empty };
            var sslCheckBox = new CheckBox { Content = "Usar SSL", IsChecked = profile.UseSsl };

            var content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    nameBox,
                    hostBox,
                    portBox,
                    userBox,
                    passwordBox,
                    databaseBox,
                    sslCheckBox
                }
            };

            var dialog = new ContentDialog
            {
                Title = isEditing ? "Editar conexão MySQL" : "Nova conexão MySQL",
                Content = content,
                PrimaryButtonText = "Salvar",
                CloseButtonText = "Cancelar",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            dialog.PrimaryButtonClick += async (_, args) =>
            {
                var deferral = args.GetDeferral();
                try
                {
                    if (string.IsNullOrWhiteSpace(nameBox.Text) ||
                        string.IsNullOrWhiteSpace(hostBox.Text) ||
                        string.IsNullOrWhiteSpace(userBox.Text))
                    {
                        args.Cancel = true;
                        await ShowInfoDialogAsync("Campos obrigatórios", "Preencha nome da conexão, host e usuário.");
                    }
                    else if (portBox.Value <= 0 || portBox.Value > 65535)
                    {
                        args.Cancel = true;
                        await ShowInfoDialogAsync("Porta inválida", "Informe uma porta válida entre 1 e 65535.");
                    }
                }
                finally
                {
                    deferral.Complete();
                }
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return null;
            }

            profile.FriendlyName = nameBox.Text.Trim();
            profile.Host = hostBox.Text.Trim();
            profile.Port = (int)Math.Round(portBox.Value);
            profile.UserName = userBox.Text.Trim();
            profile.Password = passwordBox.Password;
            profile.DefaultDatabase = string.IsNullOrWhiteSpace(databaseBox.Text) ? null : databaseBox.Text.Trim();
            profile.UseSsl = sslCheckBox.IsChecked == true;

            return profile;
        }

        private void InitializePicker(object picker)
        {
            if ((Application.Current as App)?.MainAppWindow is MainWindow mainWindow)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(mainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }
        }

        private async Task ShowInfoDialogAsync(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                CloseButtonText = "Fechar",
                XamlRoot = XamlRoot
            };

            await dialog.ShowAsync();
        }

        private void UpdateAutoScrollToggleVisualState()
        {
            if (AutoScrollToggleButton is null)
            {
                return;
            }

            if (_isAutoScrollEnabled)
            {
                AutoScrollToggleButton.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ApplicationAccentBrush"];
                AutoScrollToggleButton.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ApplicationAccentForegroundBrush"];
                AutoScrollToggleButton.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ApplicationAccentBrush"];
            }
            else
            {
                AutoScrollToggleButton.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ApplicationTileBrush"];
                AutoScrollToggleButton.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ApplicationSubTextBrush"];
                AutoScrollToggleButton.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ApplicationBorderBrush"];
            }
        }

        private void UpdateRestoreAutoScrollToggleVisualState()
        {
            if (RestoreAutoScrollToggleButton is null)
            {
                return;
            }

            if (_isRestoreAutoScrollEnabled)
            {
                RestoreAutoScrollToggleButton.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ApplicationAccentBrush"];
                RestoreAutoScrollToggleButton.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ApplicationAccentForegroundBrush"];
                RestoreAutoScrollToggleButton.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ApplicationAccentBrush"];
            }
            else
            {
                RestoreAutoScrollToggleButton.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ApplicationTileBrush"];
                RestoreAutoScrollToggleButton.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ApplicationSubTextBrush"];
                RestoreAutoScrollToggleButton.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ApplicationBorderBrush"];
            }
        }

        private void ScrollOutputToBottom()
        {
            if (OutputConsoleTextBox is null)
            {
                return;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                EnsureOutputConsoleScrollViewer();
                _outputConsoleScrollViewer?.ChangeView(null, _outputConsoleScrollViewer.ScrollableHeight, null, true);
            });
        }

        private void ScrollRestoreOutputToBottom()
        {
            if (RestoreOutputConsoleTextBox is null)
            {
                return;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                EnsureRestoreOutputConsoleScrollViewer();
                _restoreOutputConsoleScrollViewer?.ChangeView(null, _restoreOutputConsoleScrollViewer.ScrollableHeight, null, true);
            });
        }

        private void EnsureOutputConsoleScrollViewer()
        {
            _outputConsoleScrollViewer ??= FindDescendant<ScrollViewer>(OutputConsoleTextBox);
        }

        private void EnsureRestoreOutputConsoleScrollViewer()
        {
            _restoreOutputConsoleScrollViewer ??= FindDescendant<ScrollViewer>(RestoreOutputConsoleTextBox);
        }

        private static T? FindDescendant<T>(DependencyObject? parent) where T : DependencyObject
        {
            if (parent is null)
            {
                return null;
            }

            var childrenCount = VisualTreeHelper.GetChildrenCount(parent);
            for (var i = 0; i < childrenCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match)
                {
                    return match;
                }

                var descendant = FindDescendant<T>(child);
                if (descendant is not null)
                {
                    return descendant;
                }
            }

            return null;
        }

        private void CommitRestoreDatabaseSelection()
        {
            if (_viewModel.IsRestoreMode && RestoreDatabaseComboBox is not null)
            {
                _viewModel.SelectedDatabase = RestoreDatabaseComboBox.SelectedItem as string ?? RestoreDatabaseComboBox.Text;
            }
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MySqlBackupRestoreViewModel.IsBusy) || string.IsNullOrWhiteSpace(e.PropertyName))
            {
                if (DispatcherQueue.HasThreadAccess)
                {
                    UpdateBusyUiState();
                }
                else
                {
                    DispatcherQueue.TryEnqueue(UpdateBusyUiState);
                }
            }

            // When the connection changes or is invalidated, allow GotFocus to re-trigger the test.
            if (e.PropertyName is
                nameof(MySqlBackupRestoreViewModel.SelectedConnection) or
                nameof(MySqlBackupRestoreViewModel.IsSelectedConnectionValidated))
            {
                if (!_viewModel.IsSelectedConnectionValidated)
                {
                    _isRestoreDbComboFocusHandled = false;
                }
            }
        }

        private void UpdateBusyUiState()
        {
            var isIdle = _viewModel.IsIdle;

            if (BackButton is not null)
            {
                BackButton.IsEnabled = isIdle;
            }

            if (ModePicker is not null)
            {
                ModePicker.IsEnabled = isIdle;
            }

            if (BackupConnectionCard is not null)
            {
                BackupConnectionCard.IsHitTestVisible = isIdle;
                BackupConnectionCard.Opacity = isIdle ? 1 : 0.65;
            }

            if (BackupActionCard is not null)
            {
                BackupActionCard.IsHitTestVisible = true;
                BackupActionCard.Opacity = 1;
            }

            if (BackupInputsPanel is not null)
            {
                BackupInputsPanel.IsHitTestVisible = isIdle;
                BackupInputsPanel.Opacity = isIdle ? 1 : 0.65;
            }

            if (RestoreConnectionCard is not null)
            {
                RestoreConnectionCard.IsHitTestVisible = isIdle;
                RestoreConnectionCard.Opacity = isIdle ? 1 : 0.65;
            }

            if (RestoreActionCard is not null)
            {
                RestoreActionCard.IsHitTestVisible = true;
                RestoreActionCard.Opacity = 1;
            }

            if (RestoreInputsPanel is not null)
            {
                RestoreInputsPanel.IsHitTestVisible = isIdle;
                RestoreInputsPanel.Opacity = isIdle ? 1 : 0.65;
            }

            if (DatabaseComboBox is not null)
            {
                DatabaseComboBox.IsEnabled = isIdle;
            }

            if (RestoreDatabaseComboBox is not null)
            {
                RestoreDatabaseComboBox.IsEnabled = isIdle;
            }

            if (CreateDatabaseButton is not null)
            {
                CreateDatabaseButton.IsEnabled = isIdle && _viewModel.CanCreateDatabase;
            }

            if (ForeignKeyChecksOption is not null)
            {
                ForeignKeyChecksOption.IsEnabled = isIdle;
            }
        }

        private void SyncThemeToViewModel()
        {
            _viewModel.SetCurrentTheme(ActualTheme);
        }
    }
}
