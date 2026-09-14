using System.Windows;
using System.Windows.Controls;
using SmartX.Desktop.Client.ViewModels;

namespace SmartX.Wpf.Views;

public partial class FileAttachmentsView : UserControl
{
    public FileAttachmentsView()
    {
        InitializeComponent();
    }

    private void OnDropZoneDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDropZoneDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not ShellViewModel shell) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;

        _ = shell.UploadAsync(paths);
        e.Handled = true;
    }

    private void OnDropZoneClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel shell && shell.AttachFilesCommand.CanExecute(null))
        {
            shell.AttachFilesCommand.Execute(null);
        }
    }
}
