using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Soundie.Resources.DataModels;
using Soundie.Services;
using Soundie.Services.Dialogs;
using Soundie.ViewModels.Popups;

namespace Soundie.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class InstrumentalTrackDialogServiceTests
{
    [Test]
    public void FailedPresenterAlwaysDeactivatesDrawer()
    {
        var messenger = new WeakReferenceMessenger();
        InstrumentalArrangementDrawerVM? vm = null;
        var snapshot = Snapshot();
        var context = InstrumentalArrangementTestContext.Create(snapshot,
            [0], [120], TimeSpan.Zero, 4, 32);
        var service = new InstrumentalTrackDialogService(() => null, Dispatcher.CurrentDispatcher,
            data => vm = new(data, messenger, new ImmediateUiScheduler()),
            _ => throw new AssertionException("Mapping factory should not run."),
            (_, _) => Task.FromException(new InvalidOperationException("presenter failed")));
        Assert.Throws<InvalidOperationException>(() => UiTestPump.Run(service.ShowArrangementAsync(context, default)));
        Assert.That(vm!.CanEdit, Is.False);
    }

    [Test]
    public void CancelledMappingPresenterSettlesBeforeReturning()
    {
        var messenger = new WeakReferenceMessenger();
        KeysMappingDrawerVM? vm = null;
        using var cancel = new CancellationTokenSource();
        var service = new InstrumentalTrackDialogService(() => null, Dispatcher.CurrentDispatcher,
            _ => throw new AssertionException("Arrangement factory should not run."),
            snapshot => vm = new(snapshot, messenger, new ImmediateUiScheduler(), new StubSampleFileService([]), new StubSampleFileDialogService((string?)null)),
            presentMapping: async (_, token) => { cancel.Cancel(); await Task.Delay(Timeout.Infinite, token); });
        Assert.Throws<TaskCanceledException>(() => UiTestPump.Run(service.ShowKeysMappingAsync(Snapshot(), cancel.Token)));
        Assert.That(vm!.CanEditMapping, Is.False);
        Assert.That(vm.CanOpenKey, Is.False);
    }

    [Test]
    public void FailedMappingPresenterDeactivatesRecipient()
    {
        var messenger = new WeakReferenceMessenger();
        KeysMappingDrawerVM? mapping = null;
        var service = new InstrumentalTrackDialogService(() => null, Dispatcher.CurrentDispatcher,
            _ => throw new AssertionException("Arrangement factory should not run."),
            snapshot => mapping = new(snapshot, messenger, new ImmediateUiScheduler(), new StubSampleFileService([]), new StubSampleFileDialogService((string?)null)),
            presentMapping: (_, _) => Task.FromException(new InvalidOperationException("mapping presenter failed")));
        Assert.Throws<InvalidOperationException>(() => UiTestPump.Run(service.ShowKeysMappingAsync(Snapshot(), default)));
        Assert.That(mapping!.CanEditMapping, Is.False);
        Assert.That(mapping.CanOpenKey, Is.False);
    }

    private static InstrumentalTrackSnapshot Snapshot() => new(
        new("project.json", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), "Piano", 100,
        new Dictionary<string, string>(), [], [], 0);
}
