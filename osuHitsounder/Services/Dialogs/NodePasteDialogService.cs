using System.Windows;
using System.Windows.Threading;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.ViewModels.Popups;
using osuHitsounder.Views.Popups;

namespace osuHitsounder.Services.Dialogs;

public sealed class NodePasteDialogService(Func<Window?> getOwner, Dispatcher dispatcher, Func<NodePastePopupVM> createChoice) : INodePasteDialogService
{
    public async Task<NodePasteTiming?> ChooseTimingAsync(CancellationToken cancellationToken) => await dispatcher.InvokeAsync(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = getOwner();
        if (owner?.IsVisible != true) return null;
        var popup = new NodePastePopup(createChoice()) { Owner = owner, FlowDirection = owner.FlowDirection };
        using var registration = cancellationToken.Register(() => dispatcher.BeginInvoke(new Action(() => { if (popup.IsVisible) popup.Close(); })));
        popup.ShowDialog();
        cancellationToken.ThrowIfCancellationRequested();
        return owner.IsVisible ? popup.Result : null;
    }, DispatcherPriority.Normal, cancellationToken);

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
