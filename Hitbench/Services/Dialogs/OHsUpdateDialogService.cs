using System.IO;
using System.Windows;
using System.Windows.Threading;
using Hitbench.Resources.DataModels;
using Hitbench.ViewModels.Popups;
using Hitbench.Views.Popups;

namespace Hitbench.Services.Dialogs;

public sealed class OHsUpdateDialogService(Func<Window?> getOwner, Dispatcher dispatcher,
    Func<OHsUpdateRelease, OHsUpdatePopupVM> createViewModel,
    Func<OHsUpdatePopupVM, CancellationToken, Task>? present = null) : IOHsUpdateDialogService
{
    public async Task<OHsStagedUpdate?> ShowAsync(OHsUpdateRelease release, CancellationToken token) =>
        await dispatcher.InvokeAsync(async () =>
        {
            if (token.IsCancellationRequested) return null;
            var owner = getOwner();
            if (present is null && owner?.IsVisible != true) return null;
            var vm = createViewModel(release);
            OHsUpdatePopup? popup = null;
            using var registration = token.Register(() => dispatcher.BeginInvoke(new Action(vm.RequestClose)));
            try
            {
                if (present is not null) await present(vm, token);
                else { popup = new OHsUpdatePopup(vm) { Owner = owner, FlowDirection = owner!.FlowDirection }; popup.ShowDialog(); }
                if (vm.Error is not null) throw new IOException(vm.Error);
                return vm.Result;
            }
            finally { await vm.DeactivateAsync(); popup?.Detach(); }
        }).Task.Unwrap();
}
