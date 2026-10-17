using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Resources.Messages;
using osuHitsounder.ViewModels.Popups;

namespace osuHitsounder.Test.Presentation;

public sealed class InstrumentalArrangementDrawerVMTests
{
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
        messenger.Send(new OHsInstrumentalTimelineChangedMessage(Timeline(2, 1500)));
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
