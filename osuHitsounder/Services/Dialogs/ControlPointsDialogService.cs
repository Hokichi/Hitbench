using System.Windows;
using System.Windows.Threading;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.ViewModels.Popups;
using osuHitsounder.Views.Popups;

namespace osuHitsounder.Services.Dialogs;

public sealed class ControlPointsDialogService(Func<Window?> getOwner, Dispatcher dispatcher,
    Func<ControlPointsSnapshot, ControlPointsDrawerVM> createDrawer) : IControlPointsDialogService
{
    internal Func<ControlPointsDrawerVM, CancellationToken, Task>? Present { get; init; }
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
                else await ((MainWindow)owner!).PresentDrawerAsync(new OHsControlPointsDrawer(vm), token);
            }
            finally { await vm.DeactivateAsync(); }
        }).Task.Unwrap();
}
