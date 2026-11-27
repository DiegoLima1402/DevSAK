using DevSAK.Models;
using DevSAK.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DevSAK.Views;

public sealed class FtpDestinationEditorDialog : ContentDialog
{
    private readonly FtpDestinationStore _store;
    private readonly FtpDestination _original;
    private readonly TextBox _description = new() { Header = "Descrição", PlaceholderText = "Opcional; sem descrição, será usado o host" };
    private readonly TextBox _host = new() { Header = "Host", PlaceholderText = "ftp.exemplo.com" };
    private readonly NumberBox _port = new() { Header = "Porta", Minimum = 1, Maximum = 65535, Value = 21, SmallChange = 1 };
    private readonly TextBox _user = new() { Header = "Usuário", PlaceholderText = "Usuário do FTP ou anonymous" };
    private readonly PasswordBox _password = new() { Header = "Senha", PasswordRevealMode = PasswordRevealMode.Hidden };
    private readonly Microsoft.UI.Xaml.Controls.Primitives.ToggleButton _showPassword = new()
    {
        Content = new FontIcon { Glyph = "\uE890", FontSize = 16 },
        VerticalAlignment = VerticalAlignment.Bottom,
        Width = 36, Height = 32
    };
    private readonly ComboBox _security = new() { Header = "Tipo de conexão", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly CheckBox _passive = new() { Content = "Usar modo passivo", IsChecked = true };
    private readonly TextBox _folder = new() { Header = "Pasta de destino no servidor", Text = "/" };
    private readonly Button _connect = new() { Content = "Conectar / Carregar pastas" };
    private readonly Button _up = new() { Content = "Pasta acima", IsEnabled = false };
    private readonly Button _open = new() { Content = "Abrir pasta", IsEnabled = false };
    private readonly ListView _directories = new() { Height = 145, DisplayMemberPath = "Name", SelectionMode = ListViewSelectionMode.Single };
    private readonly TextBlock _browserStatus = new() { Text = "Carregue as pastas e navegue até o destino desejado.", TextWrapping = TextWrapping.Wrap };
    private readonly InfoBar _error = new() { IsOpen = false, IsClosable = true, Severity = InfoBarSeverity.Error, Title = "Não foi possível concluir" };
    private readonly StackPanel _connectionFields = new() { Spacing = 12 };
    private readonly StackPanel _browser = new() { Spacing = 12, Visibility = Visibility.Collapsed };
    private CancellationTokenSource? _browsing;
    private bool _closing;
    private bool _loading;
    private string? _loadedPath;

    public FtpDestination? SavedDestination { get; private set; }

    public FtpDestinationEditorDialog(FtpDestinationStore store, FtpDestination? destination = null)
    {
        _store = store;
        _original = destination ?? new FtpDestination();
        Title = destination == null ? "Adicionar destino FTP" : "Editar destino FTP";
        PrimaryButtonText = "Salvar";
        CloseButtonText = "Cancelar";
        DefaultButton = ContentDialogButton.Primary;
        Resources["ContentDialogMaxWidth"] = 680d;

        _loading = true;
        _description.Text = _original.Description;
        _host.Text = _original.Host;
        _port.Value = _original.Port;
        _user.Text = _original.UserName;
        _passive.IsChecked = _original.Passive;
        _folder.Text = _original.RemoteDirectory;
        _security.Items.Add("FTP");
        _security.Items.Add("FTPS — TLS explícito");
        _security.Items.Add("FTPS — TLS implícito");
        _security.SelectedIndex = (int)_original.Security;
        try { _password.Password = _store.GetPassword(_original); }
        catch (Exception ex) { ShowError(ex.Message); }
        _loading = false;

        var address = new Grid { ColumnSpacing = 12 };
        address.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        address.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        Grid.SetColumn(_port, 1);
        address.Children.Add(_host);
        address.Children.Add(_port);
        _connectionFields.Children.Add(_description);
        _connectionFields.Children.Add(address);
        _connectionFields.Children.Add(_user);
        var passwordRow = new Grid { ColumnSpacing = 8 };
        passwordRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        passwordRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        passwordRow.Children.Add(_password);
        Grid.SetColumn(_showPassword, 1);
        passwordRow.Children.Add(_showPassword);
        ToolTipService.SetToolTip(_showPassword, "Mostrar senha");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_showPassword, "Mostrar senha");
        _showPassword.Click += (_, _) =>
        {
            bool visible = _showPassword.IsChecked == true;
            _password.PasswordRevealMode = visible ? PasswordRevealMode.Visible : PasswordRevealMode.Hidden;
            string label = visible ? "Ocultar senha" : "Mostrar senha";
            ToolTipService.SetToolTip(_showPassword, label);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_showPassword, label);
        };
        _connectionFields.Children.Add(passwordRow);
        _connectionFields.Children.Add(_security);
        _connectionFields.Children.Add(_passive);
        ToolTipService.SetToolTip(_passive, "No modo passivo, o DevSAK inicia também a conexão de dados com o servidor para listar pastas e enviar arquivos. Isso costuma facilitar o uso atrás de roteadores, NAT e firewalls. Mantenha marcado, salvo se o servidor exigir modo ativo.");

        var navigation = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        navigation.Children.Add(_up);
        navigation.Children.Add(_open);
        var panel = new StackPanel { Spacing = 12, MinWidth = 400, Margin = new Thickness(0, 0, 24, 8) };
        panel.Children.Add(_connectionFields);
        panel.Children.Add(_connect);
        _browser.Children.Add(_folder);
        _browser.Children.Add(new TextBlock { Text = "Pastas no servidor", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        _browser.Children.Add(navigation);
        _browser.Children.Add(_directories);
        _browser.Children.Add(_browserStatus);
        _browser.Children.Add(new TextBlock { Text = "A pasta exibida no campo de destino será usada nos próximos uploads.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(_browser);
        panel.Children.Add(_error);
        Content = new ScrollViewer { Content = panel, MaxHeight = 620, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        _connect.Click += async (_, _) => await BrowseAsync(_folder.Text);
        _up.Click += async (_, _) => { if (_loadedPath != null) await BrowseAsync(FtpDestinationValidation.ParentPath(_loadedPath)); };
        _open.Click += async (_, _) => await OpenSelectedAsync();
        _directories.DoubleTapped += async (_, _) => await OpenSelectedAsync();
        _directories.SelectionChanged += (_, _) => _open.IsEnabled = _browsing == null && _directories.SelectedItem != null;
        _host.TextChanged += (_, _) => InvalidateBrowser();
        _user.TextChanged += (_, _) => InvalidateBrowser();
        _password.PasswordChanged += (_, _) => InvalidateBrowser();
        _port.ValueChanged += (_, _) => InvalidateBrowser();
        _passive.Checked += (_, _) => InvalidateBrowser();
        _passive.Unchecked += (_, _) => InvalidateBrowser();
        _security.SelectionChanged += (_, _) =>
        {
            if (_security.SelectedIndex == (int)FtpSecurity.ImplicitTls && _port.Value == 21) _port.Value = 990;
            else if (_security.SelectedIndex != (int)FtpSecurity.ImplicitTls && _port.Value == 990) _port.Value = 21;
            InvalidateBrowser();
        };
        PrimaryButtonClick += Save_Click;
        Closing += (_, _) => { _closing = true; _browsing?.Cancel(); };
        Opened += async (_, _) => { if (destination != null) await BrowseAsync(_folder.Text); };
    }

    private FtpDestination ReadDestination()
    {
        if (double.IsNaN(_port.Value) || _port.Value != Math.Truncate(_port.Value))
            throw new ArgumentException("Informe uma porta inteira entre 1 e 65535.");
        return FtpDestinationValidation.Normalize(_original with
        {
            Description = _description.Text, Host = _host.Text, Port = checked((int)_port.Value), UserName = _user.Text,
            Security = (FtpSecurity)_security.SelectedIndex, Passive = _passive.IsChecked == true, RemoteDirectory = _folder.Text
        });
    }

    private async Task OpenSelectedAsync()
    {
        if (_browsing == null && _directories.SelectedItem is FtpDirectoryEntry entry) await BrowseAsync(entry.Path);
    }

    private async Task BrowseAsync(string path)
    {
        if (_browsing != null || _closing) return;
        FtpDestination destination;
        try
        {
            destination = ReadDestination();
            FtpDestinationValidation.RejectControls(_password.Password);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            if (string.IsNullOrWhiteSpace(_host.Text)) _host.Focus(FocusState.Programmatic);
            else if (string.IsNullOrWhiteSpace(_user.Text)) _user.Focus(FocusState.Programmatic);
            return;
        }
        using var source = new CancellationTokenSource();
        _browsing = source;
        SetBusy(true);
        _error.IsOpen = false;
        _browserStatus.Text = "Carregando pastas...";
        try
        {
            var result = await new FtpTransferService().ListDirectoriesAsync(destination, _password.Password, path, source.Token);
            if (_closing) return;
            _loadedPath = result.Path;
            _browser.Visibility = Visibility.Visible;
            _folder.Text = result.Path;
            _directories.ItemsSource = result.Directories;
            _browserStatus.Text = result.Directories.Length == 0 ? "Esta pasta não contém subpastas. Você pode usá-la como destino." : $"Pasta atual: {result.Path}";
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!_closing)
            {
                _browserStatus.Text = "Não foi possível carregar as pastas.";
                ShowError(ex.Message);
            }
        }
        finally { _browsing = null; if (!_closing) SetBusy(false); }
    }

    private void SetBusy(bool busy)
    {
        IsPrimaryButtonEnabled = !busy;
        _description.IsEnabled = !busy;
        _host.IsEnabled = !busy;
        _port.IsEnabled = !busy;
        _user.IsEnabled = !busy;
        _password.IsEnabled = !busy;
        _showPassword.IsEnabled = !busy;
        _security.IsEnabled = !busy;
        _passive.IsEnabled = !busy;
        _folder.IsEnabled = !busy;
        _connect.IsEnabled = !busy;
        _directories.IsEnabled = !busy;
        _up.IsEnabled = !busy && _loadedPath != null && _loadedPath != "/";
        _open.IsEnabled = !busy && _directories.SelectedItem != null;
    }

    private void InvalidateBrowser()
    {
        if (_loading || _browsing != null) return;
        _loadedPath = null;
        _browser.Visibility = Visibility.Collapsed;
        _directories.ItemsSource = null;
        _up.IsEnabled = false;
        _open.IsEnabled = false;
        _browserStatus.Text = "A conexão mudou. Carregue as pastas novamente.";
    }

    private void Save_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        try { SavedDestination = _store.Save(ReadDestination(), _password.Password); }
        catch (Exception ex) { args.Cancel = true; ShowError(ex.Message); }
    }

    private void ShowError(string message)
    {
        _error.Content = new TextBlock { Text = message, IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap };
        _error.IsOpen = true;
    }
}
