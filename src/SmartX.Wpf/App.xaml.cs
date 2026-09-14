using System.Windows;
using System.Windows.Threading;
using SmartX.Desktop.Client.Services;
using SmartX.Desktop.Client.ViewModels;
using SmartX.Wpf.Services;

namespace SmartX.Wpf;

public partial class App : Application
{
    private ShellViewModel? _shell;

    public const string DefaultGatewayUrl = "http://localhost:5240/";

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var url = e.Args.Length > 0 && Uri.IsWellFormedUriString(e.Args[0], UriKind.Absolute)
            ? e.Args[0]
            : DefaultGatewayUrl;

        var client = new GatewayClient(url);
        _shell = new ShellViewModel(client, new WpfDispatcher(Dispatcher), new WpfFileDialogService());

        var window = new SmartX.Wpf.MainWindow { DataContext = _shell };
        base.MainWindow = window;
        window.Show();

        _shell.Start();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"The console hit an unexpected error and has recovered.\n\n{e.Exception.Message}",
            "Smart X Gateway Console",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shell?.Dispose();
        base.OnExit(e);
    }
}
