using DevSAK.Views;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Diagnostics;
using System.Reflection;
using WinRT.Interop;

namespace DevSAK
{
    public sealed partial class MainWindow : Window
    {
        private ElementTheme _currentTheme = ElementTheme.Light;

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

        private void RootGrid_Loaded(object sender, RoutedEventArgs e)
        {
            // Detecta o tema do sistema na primeira inicialização
            var initialTheme = RootGrid.ActualTheme == ElementTheme.Dark
                ? ElementTheme.Dark
                : ElementTheme.Light;

            ApplyTheme(initialTheme);
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
    }
}
