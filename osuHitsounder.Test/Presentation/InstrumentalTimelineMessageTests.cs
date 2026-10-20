using osuHitsounder.Resources.DataModels;
using osuHitsounder.Resources.Messages;
using osuHitsounder.Core.Entities;
using osuHitsounder.Services;
using osuHitsounder.ViewModels.Shell;
using CommunityToolkit.Mvvm.Messaging;

namespace osuHitsounder.Test.Presentation;

public sealed class InstrumentalTimelineMessageTests
{
    [Test]
    public async Task ReadyReplaysTimelineAndRejectsStaleEditorRequests()
    {
        var track = new InstrumentalTrack { KeyFiles = new() { ["C4"] = "piano.wav" } };
        var messenger = new WeakReferenceMessenger();
        var dialogs = new StubInstrumentalTrackDialogService();
        var messages = new List<OHsInstrumentalTimelineSnapshot>();
        messenger.Register<OHsInstrumentalTimelineChangedMessage>(messages, (_, m) => messages.Add(m.Snapshot));
        var owner = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(
            new WorkstationSource("map", "song", new ProjectData { Tracks = [track], Offsets = [0], BPMs = [120] })) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(),
            messenger: messenger, instrumentDialogs: dialogs);
        dialogs.Arrangement = (data, _) =>
        {
            owner.IsAdditionMode = false;
            messenger.Send(new InstrumentalEditorReadyMessage(data.Track.Context));
            Assert.That(messages.Last().IsAdditionMode, Is.False);
            messenger.Send(new OHsInstrumentalTimelineEditRequestedMessage(data.Track.Context, 1, IsAdditionMode: true));
            Assert.That(owner.IsAdditionMode, Is.True);
            Assert.That(messages.Last().IsAdditionMode, Is.True);
            Assert.That(messages, Is.Not.Empty);
            Assert.That(messages.Last().Duration, Is.EqualTo(TimeSpan.FromSeconds(10)));
            messenger.Send(new OHsInstrumentalTimelineEditRequestedMessage(data.Track.Context, 2, 33, 6, false));
            Assert.That(owner.ZoomFactor, Is.EqualTo(33));
            Assert.That(owner.Subdivision, Is.EqualTo(8));
            Assert.That(owner.ScrollWithMarker, Is.False);
            messenger.Send(new OHsInstrumentalTimelineEditRequestedMessage(data.Track.Context, 1, 100));
            messenger.Send(new OHsInstrumentalTimelineEditRequestedMessage(data.Track.Context with { EditorSessionId = Guid.NewGuid() }, 3, 100));
            Assert.That(owner.ZoomFactor, Is.EqualTo(33));
            return Task.CompletedTask;
        };
        try
        {
            await owner.LoadAsync("project.json", default);
            owner.AcknowledgePresentationReady(owner.LoadGeneration);
            await owner.ViewArrangementCommand.ExecuteAsync(track);
        }
        finally { await owner.DeactivateAsync(); messenger.UnregisterAll(messages); }
    }
    [Test]
    public void SnapshotsDefensivelyCopyTiming()
    {
        var offsets = new List<int> { 0 };
        var bpms = new List<int> { 120 };
        var context = new InstrumentalTrackContext("project.json", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var snapshot = new OHsInstrumentalTimelineSnapshot(context, 1, TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(1500), false, true, true, offsets, bpms, 33, 6, false, null);
        offsets[0] = 500;
        bpms.Clear();
        Assert.That(snapshot.Offsets, Is.EqualTo(new[] { 0 }));
        Assert.That(snapshot.Bpms, Is.EqualTo(new[] { 120 }));
        Assert.That(snapshot.Subdivision, Is.EqualTo(8));
    }
}
