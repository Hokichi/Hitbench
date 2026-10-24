using System.Windows;
using System.Windows.Threading;
using Onset.Resources.DataModels;
using Onset.ViewModels.Popups;
using Onset.Views.Popups;
namespace Onset.Services.Dialogs;

public sealed class OHsExportDialogService(Func<Window?> getOwner, Dispatcher dispatcher,
    Func<OHsExportRequest, OHsExportPopupVM> createViewModel,
    Func<OHsExportPopupVM, CancellationToken, Task>? present = null) : IOHsExportDialogService
{
    public async Task ShowAsync(OHsExportRequest request, CancellationToken token) =>
        await dispatcher.InvokeAsync(async () =>
        {
            if (token.IsCancellationRequested) return;
            var owner = getOwner();
            if (present is null && owner?.IsVisible != true) return;
            var vm = createViewModel(request);
            OHsExportPopup? popup = null;
            using var registration = token.Register(() => dispatcher.BeginInvoke(new Action(vm.RequestClose)));
            try
            {
                if (present is not null) await present(vm, token);
                else
                {
                    popup = new OHsExportPopup(vm) { Owner = owner, FlowDirection = owner!.FlowDirection };
                    popup.ShowDialog();
                }
            }
            finally { await vm.DeactivateAsync(); popup?.Detach(); }
        }).Task.Unwrap();
}
