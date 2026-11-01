using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Services.Dialogs;
using Hitbench.ViewModels.Popups;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA)] [NonParallelizable]
public sealed class SampleManagementDialogServiceTests
{
    [Test]
    public void CloseDuringInitializationNeverPresents()
    {
        var names = new TaskCompletionSource<IReadOnlyList<string>>();
        var files = new StubSampleFileService([]) { List = (_, _) => names.Task };
        var context = new Hitbench.Resources.DataModels.SampleManagementContext("project.json", null, Guid.NewGuid());
        ManageSamplesPopupVM? manager = null;
        var presentations = 0;
        var service = new SampleManagementDialogService(() => null, Dispatcher.CurrentDispatcher,
            () => throw new AssertionException("Loader unexpectedly created"),
            (data, _, _) => manager = new(data, files, new(files), new StubSampleFileDialogService([]),
                new StubSampleRemovalDialogService(), new WeakReferenceMessenger(), new ImmediateUiScheduler()),
            (_, _) => { presentations++; return Task.CompletedTask; });
        var showing = service.ShowAsync(context, default);
        UiTestPump.Run(Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.Background).Task);
        try
        {
            var closing = service.TryCloseAsync(context, default);
            UiTestPump.Run(closing);
            Assert.That(closing.Result, Is.True);
            Assert.That(showing.IsCompleted, Is.False);
        }
        finally { names.TrySetResult(["late.wav"]); UiTestPump.Run(showing); }
        Assert.That(presentations, Is.Zero);
        Assert.That(manager!.Entries, Is.Empty);
        Assert.That(manager.AddNewSampleCommand.CanExecute(null), Is.False);
    }

    [Test]
    public void StaleContextCannotCloseNewManager()
    {
        var first = new Hitbench.Resources.DataModels.SampleManagementContext("project.json", null, Guid.NewGuid());
        var current = first with { SessionId = Guid.NewGuid() };
        var files = new StubSampleFileService(["hit.wav"]);
        var completion = new TaskCompletionSource();
        var presented = new TaskCompletionSource();
        var service = new SampleManagementDialogService(() => null, Dispatcher.CurrentDispatcher,
            () => throw new AssertionException("Loader unexpectedly created"),
            (data, _, _) => new(data, files, new(files), new StubSampleFileDialogService([]),
                new StubSampleRemovalDialogService(), new WeakReferenceMessenger(), new ImmediateUiScheduler()),
            (_, token) => { presented.TrySetResult(); return completion.Task.WaitAsync(token); },
            (_, _) => { completion.TrySetResult(); return Task.FromResult(true); });
        var showing = service.ShowAsync(current, default);
        try
        {
            UiTestPump.Run(presented.Task);
            var stale = service.TryCloseAsync(first, default); UiTestPump.Run(stale);
            Assert.That(stale.Result, Is.False);
            Assert.That(showing.IsCompleted, Is.False);
            var closing = service.TryCloseAsync(current, default); UiTestPump.Run(closing);
            Assert.That(closing.Result, Is.True);
        }
        finally { completion.TrySetResult(); UiTestPump.Run(showing); }
    }

    [Test]
    public void CloseWaitsForExitAndManagerDeactivation()
    {
        var context = new Hitbench.Resources.DataModels.SampleManagementContext("project.json", null, Guid.NewGuid());
        var files = new StubSampleFileService(["hit.wav"]);
        var exit = new TaskCompletionSource<bool>();
        var completion = new TaskCompletionSource();
        var presented = new TaskCompletionSource();
        var disposed = new TaskCompletionSource();
        var preview = new StubSamplePreviewService { Dispose = () => disposed.Task };
        ManageSamplesPopupVM? manager = null;
        var service = new SampleManagementDialogService(() => null, Dispatcher.CurrentDispatcher,
            () => throw new AssertionException("Loader unexpectedly created"),
            (data, _, _) => manager = new(data, files, new(files), new StubSampleFileDialogService([]),
                new StubSampleRemovalDialogService(), new WeakReferenceMessenger(), new ImmediateUiScheduler(), preview),
            (_, token) => { presented.TrySetResult(); return completion.Task.WaitAsync(token); },
            async (_, _) => { var accepted = await exit.Task; completion.TrySetResult(); return accepted; });
        var showing = service.ShowAsync(context, default);
        try
        {
            UiTestPump.Run(presented.Task);
            var closing = service.TryCloseAsync(context, default);
            UiTestPump.Run(Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.Background).Task);
            Assert.That(closing.IsCompleted, Is.False);
            exit.SetResult(true); UiTestPump.Run(closing);
            Assert.That(closing.Result, Is.True);
            Assert.That(showing.IsCompleted, Is.False);
            Assert.That(manager!.AddNewSampleCommand.CanExecute(null), Is.False);
        }
        finally { exit.TrySetResult(true); completion.TrySetResult(); disposed.TrySetResult(); UiTestPump.Run(showing); }
        Assert.That(preview.DisposeCalls, Is.EqualTo(1));
    }

    [Test]
    public void CancellationDuringInitializationNeverPresentsAndDeactivates()
    {
        var names = new TaskCompletionSource<IReadOnlyList<string>>();
        var files = new StubSampleFileService([]) { List = (_, _) => names.Task };
        using var cancellation = new CancellationTokenSource(); ManageSamplesPopupVM? manager = null; var presentations = 0;
        var service = new SampleManagementDialogService(() => null, Dispatcher.CurrentDispatcher,
            () => throw new AssertionException("Loader unexpectedly created"),
            (context, _, _) => manager = new(context, files, new(files), new StubSampleFileDialogService([]), new StubSampleRemovalDialogService(), new WeakReferenceMessenger(), new ImmediateUiScheduler()),
            (_, _) => { presentations++; return Task.CompletedTask; });
        var showing = service.ShowAsync(new("project.json", null, Guid.NewGuid()), cancellation.Token);
        UiTestPump.Run(Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.Background).Task);
        Assert.That(manager, Is.Not.Null);
        cancellation.Cancel(); names.SetResult(["late.wav"]); UiTestPump.Run(showing);
        Assert.That(presentations, Is.Zero);
        if (manager is not null) { Assert.That(manager.Entries, Is.Empty); Assert.That(manager.AddNewSampleCommand.CanExecute(null), Is.False); }
    }

    [Test]
    public void FailedPresentationAlwaysDeactivatesManager()
    {
        var files = new StubSampleFileService(["hit.wav"]); ManageSamplesPopupVM? manager = null;
        var service = new SampleManagementDialogService(() => null, Dispatcher.CurrentDispatcher,
            () => throw new AssertionException("Loader unexpectedly created"),
            (context, _, _) => manager = new(context, files, new(files), new StubSampleFileDialogService([]), new StubSampleRemovalDialogService(), new WeakReferenceMessenger(), new ImmediateUiScheduler()),
            (_, _) => Task.FromException(new IOException("Presentation failed")));
        Assert.That(() => UiTestPump.Run(service.ShowAsync(new("project.json", null, Guid.NewGuid()), default)), Throws.TypeOf<IOException>());
        Assert.That(manager, Is.Not.Null); Assert.That(manager!.AddNewSampleCommand.CanExecute(null), Is.False);
    }

    [Test] public void ContextLoadsBeforeSubstitutedPresentation()
    {
        var files = new StubSampleFileService(["hit.wav"]); var presented = false;
        var service = new SampleManagementDialogService(() => null, Dispatcher.CurrentDispatcher,
            () => throw new AssertionException("Loader unexpectedly created"),
            (context, _, _) => new ManageSamplesPopupVM(context, files, new(files), new StubSampleFileDialogService(Array.Empty<string>()),
                new StubSampleRemovalDialogService(), new WeakReferenceMessenger(), new ImmediateUiScheduler()),
            (vm, _) => { presented = true; Assert.That(vm.Entries.Single().FileName, Is.EqualTo("hit.wav")); return Task.CompletedTask; });
        UiTestPump.Run(service.ShowAsync(new("project.json", null, Guid.NewGuid()), default));
        Assert.That(presented, Is.True);
    }
}
