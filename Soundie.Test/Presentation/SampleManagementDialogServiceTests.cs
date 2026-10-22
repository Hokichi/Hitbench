using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Soundie.Services.Dialogs;
using Soundie.ViewModels.Popups;

namespace Soundie.Test.Presentation;

[Apartment(ApartmentState.STA)] [NonParallelizable]
public sealed class SampleManagementDialogServiceTests
{
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
