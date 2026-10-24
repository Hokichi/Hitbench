using System.Windows;
using System.Windows.Threading;
using Hitbench.Resources.CustomControls;
using Hitbench.Views.Popups;

namespace Hitbench.Services.Dialogs;

public sealed class NodePasteDialogService(Func<Window?> getOwner, Dispatcher dispatcher) : INodePasteDialogService
{
    public async Task ShowCollisionAsync(CancellationToken cancellationToken) => await dispatcher.InvokeAsync(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = getOwner();
        if (owner?.IsVisible != true) return;
        var popup = new OHsMessageBoxPopup("Existing sample nodes found.", "Cannot place sample nodes", MessageBoxButton.OK, MessageBoxImage.Information) { Owner = owner, FlowDirection = owner.FlowDirection };
        using var registration = cancellationToken.Register(() => dispatcher.BeginInvoke(new Action(() => { if (popup.IsVisible) popup.Close(); })));
        popup.ShowDialog();
    }, DispatcherPriority.Normal, cancellationToken);
}
