using System.Windows;
using System.Windows.Threading;
using Hitbench.Resources.DataModels;
using Hitbench.Services.FileSystem;
using Hitbench.ViewModels.Popups;
using Hitbench.Views.Popups;

namespace Hitbench.Services.Dialogs;

public sealed class OHsHitNormalDialogService(Func<Window?> getOwner, Dispatcher dispatcher, OHsHitNormalSamples samples,
    Func<OHsHitNormalPopupVM, CancellationToken, Task<bool>>? present = null) : IOHsHitNormalDialogService
{
    public async Task<OHsHitNormalConfiguration?> RequestAsync(string projectPath, string mapsetDirectory, OHsHitNormalConfiguration current, CancellationToken token)
    {
        var choices = await samples.ListAsync(projectPath, mapsetDirectory, token);
        token.ThrowIfCancellationRequested();
        return await dispatcher.InvokeAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            var vm = new OHsHitNormalPopupVM(current, choices);
            OHsHitNormalPopup? popup = null;
            try
            {
                bool accepted;
                if (present is not null) accepted = await present(vm, token);
                else
                {
                    var owner = getOwner();
                    if (owner?.IsVisible != true) return null;
                    popup = new(vm) { Owner = owner, FlowDirection = owner.FlowDirection };
                    using var cancel = token.Register(() => dispatcher.BeginInvoke(() => popup.TryComplete(false)));
                    token.ThrowIfCancellationRequested();
                    accepted = popup.ShowDialog() == true;
                }
                token.ThrowIfCancellationRequested();
                return accepted ? vm.CreateResult() : null;
            }
            finally { if (popup is not null) popup.Owner = null; }
        }).Task.Unwrap();
    }
}
