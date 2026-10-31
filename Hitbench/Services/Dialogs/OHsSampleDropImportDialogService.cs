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
            Exception? primary = null;
            try
            {
                await vm.InitializeAsync(token); bool accepted;
                if (present is not null) accepted = await present(vm, token);
                else
                {
                    popup = new(vm) { Owner = owner, FlowDirection = owner!.FlowDirection };
                    popup.BeginPresentation();
                    using var cancel = token.Register(() => dispatcher.BeginInvoke(() => { if (popup.IsPresentationActive) popup.TryComplete(false); }));
                    token.ThrowIfCancellationRequested(); accepted = popup.ShowDialog() == true;
                }
                vm.TryCompleteSelection(accepted);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return Array.Empty<string>(); }
            catch (Exception error) { primary = error; throw; }
            finally
            {
                Exception? cleanup = null;
                try { popup?.ReleaseOwner(); } catch (Exception error) { cleanup = error; }
                try { await vm.DeactivateAsync(); } catch (Exception error) { cleanup ??= error; }
                if (cleanup is not null)
                {
                    System.Diagnostics.Trace.TraceWarning("Dropped import dialog cleanup failed: {0}", cleanup);
                    if (primary is null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanup).Throw();
                }
            }
            return !token.IsCancellationRequested && vm.SelectionAccepted == true ? vm.SelectedPaths : Array.Empty<string>();
        }).Task.Unwrap();
    }
}
