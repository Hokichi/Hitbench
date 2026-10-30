using System.IO;
using System.Windows;
using Hitbench.Views.Pages;
using Hitbench.ViewModels.Shell;
namespace Hitbench;

public partial class MainWindow
{
    private bool CanReceiveFileDrop => !closed && !closeRequested && !IsAudioBusy && !IsPopupOverlayVisible && PageHost.Content is WorkstationPage { DataContext: WorkstationPageVM { CanImportDroppedSamples: true } };
    private void OnSampleFileDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop, false)) return;
        e.Handled = true; e.Effects = CanReceiveFileDrop && e.Data.GetData(DataFormats.FileDrop, false) is string[] paths && paths.Any(p => new[] { ".wav", ".ogg", ".mp3" }.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase)) ? DragDropEffects.Copy : DragDropEffects.None;
    }
    private async void OnSampleFileDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop, false)) return; e.Handled = true; e.Effects = DragDropEffects.None;
        try { await HandleSampleFileDropAsync(e.Data); } catch (Exception error) { reportError(error.Message); }
    }
    internal async Task HandleSampleFileDropAsync(IDataObject data)
    {
        if (!CanReceiveFileDrop || data.GetData(DataFormats.FileDrop, false) is not string[] paths) return;
        if (PageHost.Content is WorkstationPage { DataContext: WorkstationPageVM vm }) await vm.ImportDroppedSamplesAsync(paths);
    }
}
