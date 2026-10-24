using System.Windows;
using System.Windows.Threading;
using Hitbench.Resources.DataModels;
using Hitbench.ViewModels.Popups;
using Hitbench.Views.Popups;

namespace Hitbench.Services.Dialogs;

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
            if (present is null && (owner is not MainWindow || !owner.IsVisible)) return;
            var picker = new SampleFileDialogService(() => owner, dispatcher, createLoader);
            var removal = new SampleRemovalDialogService(() => owner, dispatcher);
            var vm = createManager(context, picker, removal);
            try
            {
                await vm.InitializeAsync(token);
                if (token.IsCancellationRequested) return;
                if (present is not null) await present(vm, token);
                else await ((MainWindow)owner!).PresentSampleManagerAsync(new ManageSamplesDrawer(vm), token);
            }
            finally { await vm.DeactivateAsync(); }
        }).Task.Unwrap();
}
