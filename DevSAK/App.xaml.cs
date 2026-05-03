using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using System.Diagnostics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace DevSAK
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;

        /// <summary>
        /// Gets the main application window.
        /// </summary>
        public Window? MainAppWindow => _window;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            try
            {
                InitializeComponent();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
                try
                {
                    var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    var dir = System.IO.Path.Combine(local, "DevSAK");
                    Directory.CreateDirectory(dir);
                    var path = System.IO.Path.Combine(dir, "startup_error_constructor.txt");
                    File.WriteAllText(path, ex.ToString());
                }
                catch { }
            }

            // Register global exception handlers to catch startup/runtime errors
            this.UnhandledException += App_UnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
        {
            LogException(e.Exception, "unobserved_task_exception.txt");
        }

        private void CurrentDomain_UnhandledException(object? sender, System.UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                LogException(ex, "domain_unhandled_exception.txt");
            }
        }

        private void App_UnhandledException(object? sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            LogException(e.Exception, "xaml_unhandled_exception.txt");
            // mark handled so default crash doesn't occur while debugging
            e.Handled = true;
        }

        private void LogException(Exception ex, string filename)
        {
            try
            {
                Debug.WriteLine(ex.ToString());
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var dir = System.IO.Path.Combine(local, "DevSAK");
                Directory.CreateDirectory(dir);
                var path = System.IO.Path.Combine(dir, filename);
                File.WriteAllText(path, ex.ToString());
            }
            catch { }
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            try
            {
                _window = new MainWindow();
                _window.Activate();
            }
            catch (Exception ex)
            {
                // Log to debug output and a local file to help diagnose XAML startup issues
                Debug.WriteLine(ex.ToString());
                try
                {
                    var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    var dir = System.IO.Path.Combine(local, "DevSAK");
                    Directory.CreateDirectory(dir);
                    var path = System.IO.Path.Combine(dir, "startup_error.txt");
                    File.WriteAllText(path, ex.ToString());
                }
                catch { }

                // Show a minimal fallback window constructed in code so the exception is visible
                var fallback = new Window();
                var tb = new TextBlock
                {
                    Text = "Startup error:\n" + ex.Message,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(20)
                };
                fallback.Content = tb;
                fallback.Activate();
            }
        }
    }
}
