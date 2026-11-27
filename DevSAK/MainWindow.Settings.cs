using DevSAK.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;

namespace DevSAK;

public sealed partial class MainWindow
{
    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowSettingsAsync();
    }

    public async Task ShowSettingsAsync()
    {
        if (!SettingsButton.IsEnabled) return;
        SettingsButton.IsEnabled = false;
        try
        {
            var store = new StorageToSettingsStore();
            var password = new PasswordBox
            {
                Header = "Chave de API do StorageTo",
                PlaceholderText = "Deixe em branco para enviar anonimamente",
                Password = store.LoadApiKey(),
                MinWidth = 320
            };
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(password);
            panel.Children.Add(new TextBlock
            {
                Text = "A chave é salva protegida para seu usuário do Windows e validada antes de cada upload. Apague a chave e salve para usar uploads anônimos.",
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(error);
            var dialog = new ContentDialog
            {
                Title = "Configurações",
                Content = panel,
                PrimaryButtonText = "Salvar",
                CloseButtonText = "Cancelar",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = RootGrid.XamlRoot,
                RequestedTheme = _currentTheme
            };
            dialog.PrimaryButtonClick += (_, args) =>
            {
                try { store.SaveApiKey(password.Password); }
                catch { error.Text = "Não foi possível salvar a chave. Tente novamente."; args.Cancel = true; }
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Não foi possível abrir as configurações: {ex.GetType().Name}");
        }
        finally { SettingsButton.IsEnabled = true; }
    }
}
