using Microsoft.Win32;
using System.Windows;
using System.Windows.Threading;
using SmartX.Desktop.Client.Services;

namespace SmartX.Wpf.Services;

public sealed class WpfDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher;

    public WpfDispatcher(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public void Invoke(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
        {
            return;
        }

        _dispatcher.Invoke(action, DispatcherPriority.Background);
    }
}

public sealed class WpfFileDialogService : IFileDialogService
{
    public IReadOnlyList<string> PickFiles(string title, string filter, bool allowMultiple = true)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            Multiselect = allowMultiple,
            CheckFileExists = true,
            CheckPathExists = true
        };

        return dialog.ShowDialog(Application.Current?.MainWindow) == true
            ? dialog.FileNames
            : Array.Empty<string>();
    }

    public string? PickSaveLocation(string title, string suggestedFileName, string filter)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            FileName = suggestedFileName,
            Filter = filter,
            OverwritePrompt = true
        };

        return dialog.ShowDialog(Application.Current?.MainWindow) == true ? dialog.FileName : null;
    }
}
