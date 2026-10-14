using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Resources.Messages;
using osuHitsounder.Services;
using osuHitsounder.ViewModels.Shell;

namespace osuHitsounder.Test.Presentation;

public class WorkstationControlPointMarkerTests
{
    [Test]
    public async Task LoadedAndEditedTimestampSnapshotsAreImmutableDistinctAndLive()
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120],
            ControlPoints = [new() { Timestamp = 500, Volume = 50 }, new() { Timestamp = 500, Volume = 75 }, new() { Timestamp = 1500, Volume = 20 }] };
        var messages = new WeakReferenceMessenger(); var dialogs = new StubControlPointsDialogService();
        var owner = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", project)) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), messenger: messages, controlPointsDialog: dialogs);
        ControlPointsSnapshot? latest = null;
        messages.Register<ControlPointsChangedMessage>(this, (_, message) => latest = message.Snapshot);
        dialogs.Show = (initial, _) =>
        {
            var original = owner.ControlPointTimestamps;
            Assert.That(original, Is.EqualTo(new[] { 500, 1500 }));
            Assert.That(((IList<int>)original).IsReadOnly, Is.True);
            messages.Send(new DeleteControlPointRequestedMessage(initial.Context, initial.Points[0].Id));
            Assert.That(owner.ControlPointTimestamps, Is.SameAs(original));
            owner.Position = TimeSpan.FromMilliseconds(1030); owner.SelectedSubdivisionIndex = 3;
            messages.Send(new AddControlPointRequestedMessage(initial.Context));
            Assert.That(owner.ControlPointTimestamps, Is.EqualTo(new[] { 500, 1000, 1500 }));
            Assert.That(original, Is.EqualTo(new[] { 500, 1500 }));
            owner.Position = TimeSpan.FromMilliseconds(2030);
            messages.Send(new SnapControlPointRequestedMessage(initial.Context, initial.Points.Single(p => p.Timestamp == 1500).Id));
            Assert.That(owner.ControlPointTimestamps, Is.EqualTo(new[] { 500, 1000, 2000 }));
            var moved = owner.ControlPointTimestamps;
            messages.Send(new SetControlPointRequestedMessage(initial.Context, initial.Points.Single(p => p.Timestamp == 1500).Id, 35, "Drum"));
            Assert.That(owner.ControlPointTimestamps, Is.SameAs(moved));
            foreach (var point in latest!.Points.ToArray()) messages.Send(new DeleteControlPointRequestedMessage(initial.Context, point.Id));
            Assert.That(owner.ControlPointTimestamps, Is.Empty); return Task.CompletedTask;
        };
        try
        {
            await owner.LoadAsync("project.json", default); owner.AcknowledgePresentationReady(owner.LoadGeneration);
            Assert.That(owner.ControlPointTimestamps, Is.EqualTo(new[] { 500, 1500 }));
            await owner.AddControlPointCommand.ExecuteAsync(null); Assert.That(owner.ErrorMessage, Is.Null);
        }
        finally { await owner.DeactivateAsync(); messages.UnregisterAll(this); }
    }

    [Test]
    public async Task ReloadFailureClearsPreviousTimestampPresentation()
    {
        var source = new StubWorkstationSourceService
        {
            Load = (path, _) => path == "old.json"
                ? Task.FromResult(new WorkstationSource("map", "song", new ProjectData { ControlPoints = [new() { Timestamp = 1000 }] }))
                : Task.FromException<WorkstationSource>(new IOException("Missing project."))
        };
        var owner = new WorkstationPageVM(source, new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler());
        try
        {
            await owner.LoadAsync("old.json", default); Assert.That(owner.ControlPointTimestamps, Is.EqualTo(new[] { 1000 }));
            await owner.LoadAsync("missing.json", default); Assert.That(owner.ControlPointTimestamps, Is.Empty);
        }
        finally { await owner.DeactivateAsync(); }
    }
}
