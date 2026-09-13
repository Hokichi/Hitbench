using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace osuHitsounder.Services.Dialogs;

public sealed class InstallationDialogService(Func<Window?> getOwner, Dispatcher dispatcher)
    : IInstallationDialogService
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<string?> LocateAfterScanAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await dispatcher.InvokeAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var owner = getOwner();
                if (owner is null || !owner.IsVisible) return null;
                var result = MessageBox.Show(owner,
                    "Unable to find the osu! installation folder\nClick OK to manually locate...",
                    "osuHitsounder", MessageBoxButton.OK, MessageBoxImage.Information);
                cancellationToken.ThrowIfCancellationRequested();
                if (result != MessageBoxResult.OK || !owner.IsVisible) return null;
                var picker = new OpenFolderDialog { Title = "Select the osu! installation folder", Multiselect = false };
                var chosen = picker.ShowDialog(owner) == true ? picker.FolderName : null;
                cancellationToken.ThrowIfCancellationRequested();
                return owner.IsVisible ? chosen : null;
            }, DispatcherPriority.Normal, cancellationToken);
        }
        finally { gate.Release(); }
    }

    public async Task ShowNotFoundAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await dispatcher.InvokeAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var owner = getOwner();
                if (owner is not null && owner.IsVisible)
                    MessageBox.Show(owner, "osu! not found", "osuHitsounder", MessageBoxButton.OK, MessageBoxImage.Error);
                cancellationToken.ThrowIfCancellationRequested();
            }, DispatcherPriority.Normal, cancellationToken);
        }
        finally { gate.Release(); }
    }
}

