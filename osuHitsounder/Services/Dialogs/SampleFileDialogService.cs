using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace osuHitsounder.Services.Dialogs;

public sealed class SampleFileDialogService(Func<Window?> getOwner, Dispatcher dispatcher) : ISampleFileDialogService
{
    public async Task<string?> SelectAsync(CancellationToken token) => await dispatcher.InvokeAsync(() =>
    {
        token.ThrowIfCancellationRequested();
        var owner = getOwner();
        if (owner is null || !owner.IsVisible) return null;
        var picker = new OpenFileDialog
        {
            Filter = "Audio files (*.wav;*.ogg;*.mp3)|*.wav;*.ogg;*.mp3",
            CheckFileExists = true,
            Multiselect = false
        };
        return picker.ShowDialog(owner) == true ? picker.FileName : null;
    }, DispatcherPriority.Normal, token);
}
