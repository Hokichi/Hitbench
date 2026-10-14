using System.Windows;
using System.Windows.Threading;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.ViewModels.Popups;
using osuHitsounder.Views.Popups;

namespace osuHitsounder.Services.Dialogs;

public sealed class ControlPointsDialogService(Func<Window?> getOwner, Dispatcher dispatcher,
    Func<ControlPointsSnapshot, ControlPointsDrawerVM> createDrawer) : IControlPointsDialogService
{
    internal Func<ControlPointsDrawerVM, CancellationToken, Task>? Present { get; init; }
    internal Func<OHsMessageBoxPopup, CancellationToken, Task<bool>>? PresentConfirmation { get; init; }
    public async Task<bool> ConfirmReplaceAsync(ControlPointSnapshot selected, int targetTimestamp, int conflictingPointCount, CancellationToken token) =>
        await dispatcher.InvokeAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            var owner = getOwner();
            if (PresentConfirmation is null && owner?.IsVisible != true) return false;
            var popup = new OHsMessageBoxPopup(
                $"Replace {conflictingPointCount} existing control point(s) at {targetTimestamp} ms? The selected point will move here and retain its {selected.Volume}% volume and {selected.Sampleset} sampleset. Skip leaves all points unchanged.",
                "Replace control point?", MessageBoxButton.OKCancel, MessageBoxImage.Warning,
                true, "Replace", "Replace", "Delete", "Skip");
            try
            {
                if (PresentConfirmation is not null)
                {
                    var accepted = await PresentConfirmation(popup, token);
                    token.ThrowIfCancellationRequested();
                    return accepted;
                }
                popup.Owner = owner;
                popup.FlowDirection = owner!.FlowDirection;
                using var registration = token.Register(() => dispatcher.BeginInvoke(new Action(() =>
                {
                    if (popup.IsVisible) popup.Close();
                })));
                popup.ShowDialog();
                token.ThrowIfCancellationRequested();
                return owner!.IsVisible && popup.Result == MessageBoxResult.OK;
            }
            finally { popup.Close(); }
        }).Task.Unwrap();
    public async Task ShowAsync(ControlPointsSnapshot initial, CancellationToken token) =>
        await dispatcher.InvokeAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            var owner = getOwner();
            if (Present is null && (owner is not MainWindow || !owner.IsVisible)) return;
            var vm = createDrawer(initial);
            try
            {
                vm.Initialize();
                if (Present is not null) await Present(vm, token);
                else await ((MainWindow)owner!).PresentDrawerAsync(new OHsControlPointsDrawer(vm), token, bottomNonModal: true);
            }
            finally { await vm.DeactivateAsync(); }
        }).Task.Unwrap();
}
