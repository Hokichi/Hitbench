using System.Windows;
using System.Windows.Threading;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.ViewModels.Popups;
using osuHitsounder.Views.Popups;

namespace osuHitsounder.Services.Dialogs;

public sealed class SampleManagementDialogService(Func<Window?> getOwner, Dispatcher dispatcher,
    Func<SampleLoaderPopupVM> createLoader,
    Func<SampleManagementContext, ISampleFileDialogService, ISampleRemovalDialogService, ManageSamplesPopupVM> createManager)
    : ISampleManagementDialogService
{
    private readonly Func<ManageSamplesPopupVM, CancellationToken, Task>? present;
    internal SampleManagementDialogService(Func<Window?> getOwner, Dispatcher dispatcher, Func<SampleLoaderPopupVM> createLoader,
        Func<SampleManagementContext, ISampleFileDialogService, ISampleRemovalDialogService, ManageSamplesPopupVM> createManager,
        Func<ManageSamplesPopupVM, CancellationToken, Task> present) : this(getOwner, dispatcher, createLoader, createManager)
    { this.present = present; }
    public async Task ShowAsync(SampleManagementContext context, CancellationToken token) =>
        await dispatcher.InvokeAsync(async () =>
        {
            if (token.IsCancellationRequested) return;
            var owner = getOwner();
            if (present is null && owner?.IsVisible != true) return;
            ManageSamplesPopup? popup = null;
            var picker = new SampleFileDialogService(() => popup, dispatcher, createLoader);
            var removal = new SampleRemovalDialogService(() => popup, dispatcher);
            var vm = createManager(context, picker, removal);
            try
            {
                if (present is not null) { await vm.InitializeAsync(token); await present(vm, token); return; }
                popup = new ManageSamplesPopup(vm) { Owner = owner, FlowDirection = owner!.FlowDirection };
                using var registration = token.Register(() => dispatcher.BeginInvoke(new Action(() => { if (popup.IsVisible) popup.Close(); })));
                await popup.InitializeAsync(token);
                if (!token.IsCancellationRequested) popup.ShowDialog();
            }
            finally { if (popup is not null) await popup.DeactivateAsync(); else await vm.DeactivateAsync(); }
        }).Task.Unwrap();
}
