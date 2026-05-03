using DevSAK.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace DevSAK.Views
{
    public sealed partial class SmtpTestPage : Page
    {
        private readonly SmtpTestViewModel _viewModel;

        public SmtpTestPage()
        {
            InitializeComponent();
            _viewModel = new SmtpTestViewModel();
            DataContext = _viewModel;
            Loaded += SmtpTestPage_Loaded;
            Unloaded += SmtpTestPage_Unloaded;
        }

        private async void SmtpTestPage_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= SmtpTestPage_Loaded;
            await _viewModel.InitializeAsync();
            _viewModel.AppendLog("Ferramenta Testar SMTP carregada.");
        }

        private async void SmtpTestPage_Unloaded(object sender, RoutedEventArgs e)
        {
            Unloaded -= SmtpTestPage_Unloaded;
            await _viewModel.SaveNowAsync();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            var app = Application.Current as App;
            var mainWindow = app?.MainAppWindow as MainWindow;
            mainWindow?.GoBackToHome();
        }

        private async void TestSmtpButton_Click(object sender, RoutedEventArgs e)
        {
            await _viewModel.TestSmtpAsync();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.CancelTest();
        }

        private void ClearFieldsButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.ClearFields();
        }

        private void CopyLogButton_Click(object sender, RoutedEventArgs e)
        {
            var package = new DataPackage();
            package.SetText(_viewModel.LogText ?? string.Empty);
            Clipboard.SetContent(package);
            _viewModel.StatusText = "Log copiado para a área de transferência.";
        }

        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.ClearLog();
        }

        private void LogTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_viewModel.AutoScrollEnabled)
            {
                return;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                var scrollViewer = FindDescendant<ScrollViewer>(LogTextBox);
                scrollViewer?.ChangeView(null, scrollViewer.ScrollableHeight, null, disableAnimation: true);
            });
        }

        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var index = 0; index < count; index++)
            {
                var child = VisualTreeHelper.GetChild(root, index);
                if (child is T typedChild)
                {
                    return typedChild;
                }

                var nestedChild = FindDescendant<T>(child);
                if (nestedChild is not null)
                {
                    return nestedChild;
                }
            }

            return null;
        }
    }
}
