using System.Windows;
using System.Windows.Threading;
using Soundie.Resources.CustomControls;

namespace Soundie.Services.Dialogs;

public sealed class TrackRemovalDialogService(Func<Window?> getOwner, Dispatcher dispatcher)
    : ITrackRemovalDialogService
{
    public async Task<bool> ConfirmAsync(CancellationToken cancellationToken) => await dispatcher.InvokeAsync(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = getOwner();
        if (owner?.IsVisible != true) return false;
        var popup = new OHsMessageBoxPopup(
            "Removing this track will delete all of its sample placements and settings from the project. This cannot be undone. Continue?",
            "Remove track?", MessageBoxButton.OKCancel, MessageBoxImage.Warning,
            destructiveConfirmation: true) { Owner = owner };
        using var registration = cancellationToken.Register(() => dispatcher.BeginInvoke(new Action(() =>
        {
            if (popup.IsVisible) popup.Close();
        })));
        popup.ShowDialog();
        cancellationToken.ThrowIfCancellationRequested();
        return owner.IsVisible && popup.Result == MessageBoxResult.OK;
    }, DispatcherPriority.Normal, cancellationToken);
}
