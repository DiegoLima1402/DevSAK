using DevSAK.Services;
using DevSAK.Views;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using WinRT.Interop;

namespace DevSAK
{
    public sealed partial class MainWindow : Window
    {
        private ElementTheme _currentTheme = ElementTheme.Light;
        private readonly AppSettingsService _appSettingsService = AppSettingsService.Instance;
        private readonly ReleaseNotesService _releaseNotesService = new();
        private bool _releaseNotesChecked;

        public MainWindow()
        {
            this.InitializeComponent();

            // Inicia maximizada
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }

            // Configuração inicial do título da janela (opcional no WinUI 3)
            this.Title = "DevSAK";

            if (VersionTextBlock is not null)
            {
                VersionTextBlock.Text = GetDisplayVersion();
            }
        }

        private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
        {
            var initialTheme = await LoadInitialThemeAsync();

            ApplyTheme(initialTheme);

            if (!_releaseNotesChecked)
            {
                _releaseNotesChecked = true;
                await ShowReleaseNotesIfNeededAsync();
            }
        }

        private void ApplyTheme(ElementTheme theme)
        {
            _currentTheme = theme;

            // Aplica o tema ao RootGrid (o container principal do XAML atualizado)
            if (RootGrid != null)
            {
                RootGrid.RequestedTheme = theme;
            }

            // Atualiza o ícone do botão de toggle
            UpdateThemeToggleIcon(theme);
        }

        private void UpdateThemeToggleIcon(ElementTheme theme)
        {
            // Sol para tema Claro, Lua para tema Escuro (Glyphs do Segoe Fluent Icons)
            if (ThemeToggleIcon != null)
            {
                ThemeToggleIcon.Glyph = theme == ElementTheme.Dark
                    ? "\uE708"  // Moon
                    : "\uE706";  // Brightness
            }
        }

        /// <summary>
        /// Método público para retornar à Dashboard a partir das páginas filhas.
        /// </summary>
        public void GoBackToHome()
        {
            AppFrame.Visibility = Visibility.Collapsed;
            HomeScrollViewer.Visibility = Visibility.Visible;

            // Limpa a navegação para economizar memória
            AppFrame.Content = null;
        }

        private void AppFrame_Navigated(object sender, NavigationEventArgs e)
        {
            // Garante que o Frame fique visível quando houver conteúdo
            AppFrame.Visibility = Visibility.Visible;

            // Sincroniza o tema da página carregada com o tema global da janela
            if (e.Content is FrameworkElement pageRoot)
            {
                pageRoot.RequestedTheme = _currentTheme;
            }
        }

        private void TileButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button && button.Tag is string toolName)
                {
                    switch (toolName)
                    {
                        case "AspNetAppZip":
                            NavigateToTool(typeof(AppZipPage));
                            break;
                        case "ExportPemCertificate":
                            NavigateToTool(typeof(ExportPemPage));
                            break;
                        case "MySqlBackupRestore":
                            NavigateToTool(typeof(MySqlBackupRestorePage));
                            break;
                        case "SmtpTest":
                            NavigateToTool(typeof(SmtpTestPage));
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro de navegação: {ex.Message}");
            }
        }

        private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var nextTheme = _currentTheme == ElementTheme.Dark
                    ? ElementTheme.Light
                    : ElementTheme.Dark;

                ApplyTheme(nextTheme);
                _ = SaveCurrentThemeAsync(nextTheme);

                // Se houver uma página aberta no Frame, atualiza o tema dela também
                if (AppFrame.Content is FrameworkElement pageRoot)
                {
                    pageRoot.RequestedTheme = nextTheme;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao alternar tema: {ex.Message}");
            }
        }

        private void NavigateToTool(Type pageType)
        {
            HomeScrollViewer.Visibility = Visibility.Collapsed;
            AppFrame.Visibility = Visibility.Visible;
            AppFrame.Navigate(pageType, null);
        }

        private async Task<ElementTheme> LoadInitialThemeAsync()
        {
            try
            {
                var settings = await _appSettingsService.LoadAsync();
                if (Enum.TryParse<ElementTheme>(settings.Theme, ignoreCase: true, out var savedTheme) &&
                    savedTheme is ElementTheme.Light or ElementTheme.Dark)
                {
                    return savedTheme;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao carregar tema salvo: {ex.Message}");
            }

            // Detecta o tema do sistema na primeira inicialização quando não há preferência salva.
            return RootGrid.ActualTheme == ElementTheme.Dark
                ? ElementTheme.Dark
                : ElementTheme.Light;
        }

        private async Task SaveCurrentThemeAsync(ElementTheme theme)
        {
            if (theme is not (ElementTheme.Light or ElementTheme.Dark))
            {
                return;
            }

            try
            {
                var settings = await _appSettingsService.LoadAsync();
                settings.Theme = theme.ToString();
                await _appSettingsService.SaveAsync(settings);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao salvar tema: {ex.Message}");
            }
        }

        private static string GetDisplayVersion()
        {
            var assembly = Assembly.GetExecutingAssembly();

            var informationalVersion = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informationalVersion))
            {
                var cleanVersion = informationalVersion.Split('+')[0];
                return $"Versão {cleanVersion}";
            }

            var assemblyVersion = assembly.GetName().Version?.ToString();
            return string.IsNullOrWhiteSpace(assemblyVersion)
                ? "Versão desconhecida"
                : $"Versão {assemblyVersion}";
        }

        private async Task ShowReleaseNotesIfNeededAsync()
        {
            try
            {
                var pendingReleaseNotes = await _releaseNotesService.GetPendingReleaseNotesAsync();
                if (pendingReleaseNotes is null)
                {
                    return;
                }

                await ShowReleaseNotesDialogAsync(pendingReleaseNotes);
                await _releaseNotesService.MarkReleaseNotesAsShownAsync(pendingReleaseNotes.Version);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao exibir release notes: {ex}");
            }
        }

        private async Task ShowReleaseNotesDialogAsync(ReleaseNotesInfo releaseNotesInfo)
        {
            var versionText = ReleaseNotesService.ToDisplayVersion(releaseNotesInfo.Version);
            var accentBrush = (Brush)Application.Current.Resources["ApplicationAccentBrush"];
            var accentForegroundBrush = (Brush)Application.Current.Resources["ApplicationAccentForegroundBrush"];
            var surfaceBrush = (Brush)Application.Current.Resources["ApplicationSurfaceBrush"];
            var tileBrush = (Brush)Application.Current.Resources["ApplicationTileBrush"];
            var borderBrush = (Brush)Application.Current.Resources["ApplicationBorderBrush"];
            var textBrush = (Brush)Application.Current.Resources["ApplicationTextBrush"];
            var subTextBrush = (Brush)Application.Current.Resources["ApplicationSubTextBrush"];

            var headerGrid = new Grid
            {
                ColumnSpacing = 14,
                Margin = new Thickness(0, 0, 0, 16)
            };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            headerGrid.Children.Add(new Border
            {
                Width = 52,
                Height = 52,
                CornerRadius = new CornerRadius(12),
                Background = accentBrush,
                Child = new FontIcon
                {
                    Glyph = "\uE895",
                    FontFamily = new FontFamily("Segoe Fluent Icons"),
                    FontSize = 24,
                    Foreground = accentForegroundBrush
                }
            });

            var headerText = new StackPanel { Spacing = 3 };
            headerText.Children.Add(new TextBlock
            {
                Text = $"Novidades da versão {versionText}",
                FontSize = 22,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = textBrush
            });
            headerText.Children.Add(new TextBlock
            {
                Text = "Confira as melhorias mais recentes do DevSAK.",
                FontSize = 13,
                Foreground = subTextBrush,
                TextWrapping = TextWrapping.Wrap
            });
            Grid.SetColumn(headerText, 1);
            headerGrid.Children.Add(headerText);

            var notesPanel = BuildReleaseNotesContent(releaseNotesInfo.Markdown, textBrush, subTextBrush, accentBrush);
            var notesCard = new Border
            {
                Background = tileBrush,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(18),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Child = new ScrollViewer
                {
                    MaxHeight = 520,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Content = notesPanel
                }
            };

            var content = new Border
            {
                Background = surfaceBrush,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(4),
                Child = new StackPanel
                {
                    Width = 520,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Children =
                    {
                        headerGrid,
                        notesCard
                    }
                }
            };

            var dialog = new ContentDialog
            {
                Content = content,
                CloseButtonText = "Entendi",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = RootGrid.XamlRoot
            };

            await dialog.ShowAsync();
        }

        private static StackPanel BuildReleaseNotesContent(string markdown, Brush textBrush, Brush subTextBrush, Brush accentBrush)
        {
            const double contentWidth = 438;

            var panel = new StackPanel
            {
                Spacing = 8,
                Width = contentWidth,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            var lines = markdown.Replace("\r\n", "\n").Split('\n');

            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd();
                if (string.IsNullOrWhiteSpace(line))
                {
                    panel.Children.Add(new Border { Height = 6 });
                    continue;
                }

                if (line.StartsWith("**") && line.EndsWith("**"))
                {
                    var text = line.Trim('*').Trim();
                    panel.Children.Add(new TextBlock
                    {
                        Text = text,
                        FontSize = panel.Children.Count == 0 ? 18 : 15,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = panel.Children.Count == 0 ? accentBrush : textBrush,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, panel.Children.Count == 0 ? 0 : 8, 0, 2)
                    });
                    continue;
                }

                var indentLevel = rawLine.StartsWith("  -", StringComparison.Ordinal) ? 1 : 0;
                var bulletText = line.StartsWith("- ", StringComparison.Ordinal)
                    ? line[2..]
                    : line.StartsWith("  - ", StringComparison.Ordinal)
                        ? line[4..]
                        : line;

                panel.Children.Add(new TextBlock
                {
                    Text = $"• {CleanMarkdownInline(bulletText)}",
                    Width = contentWidth - (indentLevel * 22),
                    Margin = new Thickness(indentLevel * 22, 0, 0, 0),
                    Foreground = subTextBrush,
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 20
                });
            }

            return panel;
        }

        private static string CleanMarkdownInline(string value)
            => value.Replace("**", string.Empty)
                .Replace("`", string.Empty)
                .Trim();
    }
}
