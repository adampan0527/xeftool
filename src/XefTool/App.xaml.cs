using System.Windows;
using System.Windows.Threading;

namespace XefTool;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        MessageBox.Show($"发生未处理的错误:\n{e.Exception.Message}", "错误",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            try
            {
                if (Dispatcher.CheckAccess())
                    MessageBox.Show($"发生严重错误:\n{ex.Message}", "错误",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                else
                    Dispatcher.Invoke(() =>
                        MessageBox.Show($"发生严重错误:\n{ex.Message}", "错误",
                            MessageBoxButton.OK, MessageBoxImage.Error));
            }
            catch { }
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        try
        {
            if (Dispatcher.CheckAccess())
                MessageBox.Show($"发生异步错误:\n{e.Exception.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            else
                Dispatcher.Invoke(() =>
                    MessageBox.Show($"发生异步错误:\n{e.Exception.Message}", "错误",
                        MessageBoxButton.OK, MessageBoxImage.Error));
        }
        catch { }
    }
}
