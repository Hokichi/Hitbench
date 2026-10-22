using CommunityToolkit.Mvvm.Messaging;
using Soundie.Resources.DataModels;
using Soundie.Resources.Messages;
using Soundie.ViewModels.Popups;

namespace Soundie.Test.Presentation;

public sealed class InstrumentalArrangementDrawerVMTests
{
    [Test]
    public async Task EditingModeFollowsSnapshotsAndStopsAfterDeactivation()
    {
        var context = new InstrumentalTrackContext("project.json", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var messenger = new WeakReferenceMessenger();
        OHsInstrumentalTimelineSnapshot Timeline(long revision, bool mode) => new(context, revision, TimeSpan.FromSeconds(10), TimeSpan.Zero, false, true, true, [0], [120], 32, 3, true, null, mode);
        var vm = new InstrumentalArrangementDrawerVM(new(new(context, "Piano", 100, new Dictionary<string, string>(), [], null, 0), Timeline(0, false)), messenger, new ImmediateUiScheduler());
        await vm.InitializeAsync(default);
        try
        {
            Assert.That(vm.IsAdditionMode, Is.False);
            messenger.Send(new OHsInstrumentalTimelineChangedMessage(Timeline(2, true)));
            Assert.That(vm.IsAdditionMode, Is.True);
            messenger.Send(new OHsInstrumentalTimelineChangedMessage(Timeline(1, false)));
            Assert.That(vm.IsAdditionMode, Is.True);
            await vm.DeactivateAsync();
            messenger.Send(new OHsInstrumentalTimelineChangedMessage(Timeline(3, false)));
            Assert.That(vm.IsAdditionMode, Is.True);
        }
        finally { await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task TrackIdentityUpdatesIgnoreStaleAndForeignSnapshots()
    {
        var context = new InstrumentalTrackContext("project.json", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        InstrumentalTrackSnapshot Snapshot(int index, long revision, InstrumentalTrackContext? other = null) =>
            new(other ?? context, "Piano", 100, new Dictionary<string, string>(), [], null, revision, trackColorIndex: index);
        var messenger = new WeakReferenceMessenger();
        var vm = new InstrumentalArrangementDrawerVM(InstrumentalArrangementTestContext.Create(Snapshot(8, 0),
            [0], [120], TimeSpan.Zero, 4, 32), messenger, new ImmediateUiScheduler());
        await vm.InitializeAsync(default);
        var notifications = new List<string?>();
        vm.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        messenger.Send(new InstrumentalTrackChangedMessage(Snapshot(1, 2)));
        messenger.Send(new InstrumentalTrackChangedMessage(Snapshot(3, 1)));
        messenger.Send(new InstrumentalTrackChangedMessage(Snapshot(5, 3, context with { EditorSessionId = Guid.NewGuid() })));
        Assert.That(vm.TrackColorIndex, Is.EqualTo(1));
        Assert.That(notifications, Does.Contain(nameof(vm.TrackColorIndex)));
        await vm.DeactivateAsync();
        messenger.Send(new InstrumentalTrackChangedMessage(Snapshot(7, 4)));
        Assert.That(vm.TrackColorIndex, Is.EqualTo(1));
    }

    [Test]
    public async Task TimelineUpdatesAreSessionQualifiedAndStopAfterDeactivation()
    {
        var ctx = new InstrumentalTrackContext("project.json", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        OHsInstrumentalTimelineSnapshot Timeline(long revision, int ms, InstrumentalTrackContext? context = null) =>
            new(context ?? ctx, revision, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(ms), true,
                true, false, [0], [120], 32, 3, true, null);
        var messenger = new WeakReferenceMessenger();
        var vm = new InstrumentalArrangementDrawerVM(new(new(ctx, "Piano", 100,
            new Dictionary<string, string>(), [], null, 0), Timeline(0, 0)), messenger, new ImmediateUiScheduler());
        await vm.InitializeAsync(default);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        messenger.Send(new OHsInstrumentalTimelineChangedMessage(Timeline(2, 1500)));
        Assert.That(changed, Is.EqualTo(new[] { nameof(vm.Position) }));
        messenger.Send(new OHsInstrumentalTimelineChangedMessage(Timeline(1, 500)));
        messenger.Send(new OHsInstrumentalTimelineChangedMessage(Timeline(3, 900, ctx with { EditorSessionId = Guid.NewGuid() })));
        Assert.That(vm.Position, Is.EqualTo(TimeSpan.FromMilliseconds(1500)));
        Assert.That(vm.CanEdit, Is.True, "Audio availability must not prevent editing valid timing.");
        await vm.DeactivateAsync();
        messenger.Send(new OHsInstrumentalTimelineChangedMessage(Timeline(4, 2000)));
        Assert.That(vm.Position, Is.EqualTo(TimeSpan.FromMilliseconds(1500)));
        Assert.That(vm.CanEdit, Is.False);
    }
}
