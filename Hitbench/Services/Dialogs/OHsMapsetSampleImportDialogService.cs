using System.Windows;
using System.Windows.Threading;
using Hitbench.ViewModels.Popups;
using Hitbench.Views.Popups;
namespace Hitbench.Services.Dialogs;
public sealed class OHsMapsetSampleImportDialogService(Func<Window?> getOwner, Dispatcher dispatcher,
    Func<OHsMapsetSampleImportPopupVM, CancellationToken, Task<bool>>? present = null) : IOHsMapsetSampleImportDialogService
{
    public async Task<IReadOnlyList<string>> SelectAsync(IReadOnlyList<string> paths, CancellationToken token) =>
        await dispatcher.InvokeAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            var vm = new OHsMapsetSampleImportPopupVM(paths);
            OHsMapsetSampleImportPopup? popup = null;
            try
            {
                bool accepted;
                if (present is not null) accepted = await present(vm, token);
                else
                {
                    var owner = getOwner();
                    if (owner?.IsVisible != true) return Array.Empty<string>();
                    popup = new(vm) { Owner = owner, FlowDirection = owner.FlowDirection };
                    using var cancellation = token.Register(() => dispatcher.BeginInvoke(() => popup.TryComplete(false)));
                    token.ThrowIfCancellationRequested();
                    accepted = popup.ShowDialog() == true;
                }
                vm.TryCompleteSelection(accepted);
                return !token.IsCancellationRequested && vm.SelectionAccepted == true ? vm.SelectedPaths : Array.Empty<string>();
            }
            finally { popup?.ReleaseOwner(); vm.Deactivate(); }
        }).Task.Unwrap();
}
