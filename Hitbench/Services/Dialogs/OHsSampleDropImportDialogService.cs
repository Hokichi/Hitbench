using System.Windows;
using System.Windows.Threading;
using Hitbench.ViewModels.Popups;
using Hitbench.Views.Popups;
namespace Hitbench.Services.Dialogs;

public sealed class OHsSampleDropImportDialogService(Func<Window?> getOwner, Dispatcher dispatcher, Func<IReadOnlyList<string>, OHsSampleDropImportPopupVM> create,
 Func<OHsSampleDropImportPopupVM, CancellationToken, Task<bool>>? present = null) : IOHsSampleDropImportDialogService
{
    public async Task<IReadOnlyList<string>> SelectAsync(IReadOnlyList<string> paths, CancellationToken token)
    {
        return await dispatcher.InvokeAsync(async () =>
        {
            token.ThrowIfCancellationRequested(); var owner = present is null ? getOwner() : null;
            if (present is null && owner?.IsVisible != true) return Array.Empty<string>();
            var vm = create(paths); OHsSampleDropImportPopup? popup = null;
            try
            {
                await vm.InitializeAsync(token); bool accepted;
                if (present is not null) accepted = await present(vm, token);
                else
                {
                    popup = new(vm) { Owner = owner, FlowDirection = owner!.FlowDirection };
                    using var cancel = token.Register(() => dispatcher.BeginInvoke(() => { if (popup.IsVisible) popup.DialogResult = false; }));
                    token.ThrowIfCancellationRequested(); accepted = popup.ShowDialog() == true;
                }
                token.ThrowIfCancellationRequested(); return accepted ? vm.GetCheckedPaths() : Array.Empty<string>();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return Array.Empty<string>(); }
            finally { if (popup is not null) popup.ReleaseOwner(); await vm.DeactivateAsync(); }
        }).Task.Unwrap();
    }
}
