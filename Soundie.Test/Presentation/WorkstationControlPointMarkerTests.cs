using Soundie.Core.Entities;
using Soundie.Resources.Messages;
using Soundie.Services;
using Soundie.ViewModels.Shell;

namespace Soundie.Test.Presentation;

public class WorkstationControlPointMarkerTests
{
    [Test]
    public async Task LoadedAndEditedTimestampSnapshotsAreImmutableDistinctAndLive()
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120], ControlPoints =
            [new() { Timestamp = 500, Volume = 50 }, new() { Timestamp = 500, Volume = 75 }, new() { Timestamp = 1500, Volume = 20 }] };
        var owner = WorkstationControlPointStateTests.Create(project, new());
        try
        {
            await WorkstationControlPointStateTests.Ready(owner); var initial = owner.ControlPointsState!;
            var original = owner.ControlPointTimestamps;
            Assert.That(original, Is.EqualTo(new[] { 500, 1500 })); Assert.That(((IList<int>)original).IsReadOnly, Is.True);
            owner.DeleteControlPointCommand.Execute(new DeleteControlPointRequestedMessage(initial.Context, initial.Points[0].Id));
            Assert.That(owner.ControlPointTimestamps, Is.SameAs(original));
            owner.DeleteControlPointCommand.Execute(new DeleteControlPointRequestedMessage(initial.Context, initial.Points[1].Id));
            Assert.That(owner.ControlPointTimestamps, Is.EqualTo(new[] { 1500 })); Assert.That(original, Is.EqualTo(new[] { 500, 1500 }));
            owner.SetControlPointCommand.Execute(new SetControlPointRequestedMessage(initial.Context, initial.Points[2].Id, 35, "Drum"));
            Assert.That(owner.ControlPointTimestamps, Is.EqualTo(new[] { 1500 }));
            owner.DeleteControlPointCommand.Execute(new DeleteControlPointRequestedMessage(initial.Context, initial.Points[2].Id));
            Assert.That(owner.ControlPointTimestamps, Is.Empty);
        }
        finally { await owner.DeactivateAsync(); }
    }

    [Test]
    public async Task ReloadFailureClearsPreviousTimestampPresentation()
    {
        var source = new StubWorkstationSourceService { Load = (path, _) => path == "old.json"
            ? Task.FromResult(new WorkstationSource("map", "song", new ProjectData { ControlPoints = [new() { Timestamp = 1000 }] }))
            : Task.FromException<WorkstationSource>(new IOException("Missing project.")) };
        var owner = new WorkstationPageVM(source, new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler());
        try
        {
            await owner.LoadAsync("old.json", default); Assert.That(owner.ControlPointTimestamps, Is.EqualTo(new[] { 1000 }));
            await owner.LoadAsync("missing.json", default); Assert.That(owner.ControlPointTimestamps, Is.Empty);
        }
        finally { await owner.DeactivateAsync(); }
    }
}
