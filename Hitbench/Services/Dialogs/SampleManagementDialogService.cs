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
    private readonly Func<ManageSamplesPopupVM, CancellationToken, Task<bool>>? requestClose;
    private ManageSamplesPopupVM? activeManager;
    private CancellationTokenSource? initializing;
    private Func<CancellationToken, Task<bool>>? closePresentation;
    internal SampleManagementDialogService(Func<Window?> getOwner, Dispatcher dispatcher, Func<SampleLoaderPopupVM> createLoader,
        Func<SampleManagementContext, ISampleFileDialogService, ISampleRemovalDialogService, ManageSamplesPopupVM> createManager,
        Func<ManageSamplesPopupVM, CancellationToken, Task> present,
        Func<ManageSamplesPopupVM, CancellationToken, Task<bool>>? requestClose = null) : this(getOwner, dispatcher, createLoader, createManager)
    { this.present = present; this.requestClose = requestClose; }

    public async Task<bool> TryCloseAsync(SampleManagementContext context, CancellationToken token) =>
        await dispatcher.InvokeAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            if (activeManager is not { } manager || manager.Context != context) return false;
            if (initializing is { } initialization)
            {
                initialization.Cancel();
                return true;
            }
            if (!manager.CanClose || closePresentation is null) return false;
            return await closePresentation(token);
        }).Task.Unwrap();

    public async Task ShowAsync(SampleManagementContext context, CancellationToken token) =>
        await dispatcher.InvokeAsync(async () =>
        {
            if (token.IsCancellationRequested) return;
            var owner = getOwner();
            if (present is null && (owner is not MainWindow || !owner.IsVisible)) return;
            if (activeManager is not null) throw new InvalidOperationException("The sample manager is already open.");
            var picker = new SampleFileDialogService(() => owner, dispatcher, createLoader);
            var removal = new SampleRemovalDialogService(() => owner, dispatcher);
            var vm = createManager(context, picker, removal);
            using var initialization = CancellationTokenSource.CreateLinkedTokenSource(token);
            activeManager = vm;
            initializing = initialization;
            try
            {
                await vm.InitializeAsync(initialization.Token);
                if (initialization.IsCancellationRequested) return;
                initializing = null;
                ManageSamplesDrawer? drawer = null;
                closePresentation = requestClose is not null ? cancel => requestClose(vm, cancel)
                    : present is null ? _ => ((MainWindow)owner!).RequestSampleManagerCloseAsync(drawer!) : null;
                if (present is not null) await present(vm, token);
                else await ((MainWindow)owner!).PresentSampleManagerAsync(drawer = new ManageSamplesDrawer(vm), token);
            }
            catch (OperationCanceledException) when (initialization.IsCancellationRequested) { }
            finally
            {
                try { await vm.DeactivateAsync(); }
                finally
                {
                    if (ReferenceEquals(activeManager, vm))
                    { activeManager = null; initializing = null; closePresentation = null; }
                }
            }
        }).Task.Unwrap();
}
