using System.Windows;
using System.Windows.Threading;
using Hitbench.Resources.DataModels;
using Hitbench.Services.FileSystem;
using Hitbench.Services.Ui;
using Hitbench.ViewModels.Popups;
using Hitbench.Views.Popups;

namespace Hitbench.Services.Dialogs;

public sealed class OHsHitNormalDialogService(Func<Window?> getOwner, Dispatcher dispatcher, OHsHitNormalSamples samples,
    Func<OHsHitNormalPopupVM, CancellationToken, Task<bool>>? present = null,
    Func<ISampleBrowserService>? createWaveformReader = null, IUiScheduler? scheduler = null) : IOHsHitNormalDialogService
{
    public async Task<OHsHitNormalConfiguration?> RequestAsync(string projectPath, string mapsetDirectory, OHsHitNormalConfiguration current, CancellationToken token)
    {
        var choices = await samples.ListAsync(projectPath, mapsetDirectory, token);
        token.ThrowIfCancellationRequested();
        return await dispatcher.InvokeAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            var reader = createWaveformReader?.Invoke();
            OHsHitNormalPopupVM? vm = null;
            OHsHitNormalPopup? popup = null;
            try
            {
                vm = new(current, choices, projectPath, mapsetDirectory, reader, scheduler);
                await vm.InitializeAsync(token);
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
            finally
            {
                popup?.ReleaseOwner();
                try { if (vm is not null) await vm.DeactivateAsync(); }
                finally
                {
                    if (reader is IDisposable disposable) disposable.Dispose();
                    if (popup is not null) popup.Owner = null;
                }
            }
        }).Task.Unwrap();
    }
}
