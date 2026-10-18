using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.Messages;
using osuHitsounder.Services;

namespace osuHitsounder.Test.Presentation;

public class WorkstationControlPointZoneTests
{
    [Test]
    public async Task DisabledEditsRetainStoredValueAndOneSavePerAcceptedMutation()
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120], ControlPoints =
            [new() { Volume = 80 }, new() { Timestamp = 1000, Volume = 20 }] };
        var files = new DelayedProjectFileService(); files.Release();
        var audio = new StubWorkstationAudioService(); var owner = WorkstationControlPointStateTests.Create(project, audio, files);
        try
        {
            await WorkstationControlPointStateTests.Ready(owner); var state = owner.ControlPointsState!; var id = state.Points[1].Id;
            var disable = new SetControlPointDisabledRequestedMessage(state.Context, id, state.Revision, true);
            owner.SetControlPointDisabledCommand.Execute(disable); await owner.FlushSavesAsync();
            Assert.That(owner.ControlPointsState!.Points[1].IsDisabled, Is.True);
            Assert.That(owner.ControlPointsState.Revision, Is.EqualTo(state.Revision + 1));
            Assert.That(files.Writes, Is.EqualTo(1));
            owner.SetControlPointCommand.Execute(new(state.Context, id, 60, null)); await owner.FlushSavesAsync();
            Assert.That(project.ControlPoints[1].Volume, Is.EqualTo(60));
            Assert.That(new ControlPointVolumeTimeline(audio.ControlPoints).GetGain(88200), Is.EqualTo(.8f));
            var current = owner.ControlPointsState!;
            owner.SetControlPointDisabledCommand.Execute(new(current.Context, id, current.Revision, false)); await owner.FlushSavesAsync();
            Assert.That(new ControlPointVolumeTimeline(audio.ControlPoints).GetGain(88200), Is.EqualTo(.6f));
            current = owner.ControlPointsState!;
            owner.SetControlPointDisabledCommand.Execute(new(current.Context, id, current.Revision, false)); await owner.FlushSavesAsync();
            Assert.That(files.Writes, Is.EqualTo(3));
            Assert.That((audio.PlayCalls, audio.PauseCalls, audio.SeekCalls), Is.EqualTo((0, 0, 0)));
        }
        finally { await owner.DeactivateAsync(); }
    }

    [TestCase(false, false)][TestCase(true, false)][TestCase(false, true)][TestCase(true, true)]
    public async Task SplitUsesCurrentMarkerCopiesSettingsAndKeepsOriginalIdentity(bool disabled, bool playing)
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120], ControlPoints =
            [new() { Timestamp = 1000, Volume = 4, Sampleset = "Drum", IsDisabled = disabled }, new() { Timestamp = 3000 }] };
        var audio = new StubWorkstationAudioService(); var files = new DelayedProjectFileService(); files.Release();
        var owner = WorkstationControlPointStateTests.Create(project, audio, files);
        try
        {
            await WorkstationControlPointStateTests.Ready(owner);
            owner.Position = TimeSpan.FromMilliseconds(1750.5);
            if (playing)
            {
                audio.Snapshot = new(owner.Position, false, null); await owner.PlayPauseCommand.ExecuteAsync(null);
                owner.Position = TimeSpan.FromMilliseconds(250);
            }
            var state = owner.ControlPointsState!; var original = state.Points[0]; var transport = (audio.PlayCalls, audio.PauseCalls, audio.SeekCalls);
            var before = new ControlPointVolumeTimeline(audio.ControlPoints).GetGain(88200);
            var request = new SplitControlPointRequestedMessage(state.Context, original.Id, state.Revision);
            Assert.That(owner.SplitControlPointCommand.CanExecute(request), Is.True);
            owner.SplitControlPointCommand.Execute(request); await owner.FlushSavesAsync();
            var points = owner.ControlPointsState!.Points;
            Assert.That(points[0].Id, Is.EqualTo(original.Id)); Assert.That(points[1].Id, Is.Not.EqualTo(original.Id));
            // The existing playing marker is quantized by its clock to 1750; paused marker retains the half millisecond.
            Assert.That((points[1].Timestamp, points[1].Volume, points[1].Sampleset, points[1].IsDisabled), Is.EqualTo((playing ? 1750 : 1751, 4, "Drum", disabled)));
            Assert.That(owner.ControlPointsState.Revision, Is.EqualTo(state.Revision + 1)); Assert.That(files.Writes, Is.EqualTo(1));
            Assert.That(new ControlPointVolumeTimeline(audio.ControlPoints).GetGain(88200), Is.EqualTo(before));
            Assert.That((audio.PlayCalls, audio.PauseCalls, audio.SeekCalls), Is.EqualTo(transport));
        }
        finally { await owner.DeactivateAsync(); }
    }

    [TestCase(999)][TestCase(1000)][TestCase(1000.4)][TestCase(2999.6)][TestCase(3000)][TestCase(4000)]
    public async Task SplitRejectsOutsideAndRoundedBoundaries(double marker)
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120], ControlPoints = [new() { Timestamp = 1000 }, new() { Timestamp = 3000 }] };
        var audio = new StubWorkstationAudioService(); var owner = WorkstationControlPointStateTests.Create(project, audio);
        try
        {
            await WorkstationControlPointStateTests.Ready(owner); owner.Position = TimeSpan.FromMilliseconds(marker); var state = owner.ControlPointsState!;
            var request = new SplitControlPointRequestedMessage(state.Context, state.Points[0].Id, state.Revision);
            Assert.That(owner.SplitControlPointCommand.CanExecute(request), Is.False); owner.SplitControlPointCommand.Execute(request);
            Assert.That(project.ControlPoints, Has.Count.EqualTo(2)); Assert.That(owner.ControlPointsState!.Revision, Is.EqualTo(state.Revision));
        }
        finally { await owner.DeactivateAsync(); }
    }

    [Test]
    public async Task OpenSplitRequestTracksMarkerCrossingAndRevalidatesExecute()
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120], ControlPoints = [new() { Timestamp = 1000 }, new() { Timestamp = 3000 }] };
        var audio = new StubWorkstationAudioService(); var owner = WorkstationControlPointStateTests.Create(project, audio);
        try
        {
            await WorkstationControlPointStateTests.Ready(owner); var state = owner.ControlPointsState!; var id = state.Points[0].Id;
            var request = new SplitControlPointRequestedMessage(state.Context, id, state.Revision); var notifications = 0;
            owner.SplitControlPointCommand.CanExecuteChanged += (_, _) => notifications++;
            owner.Position = TimeSpan.FromMilliseconds(2000); Assert.That(owner.SplitControlPointCommand.CanExecute(request), Is.True);
            owner.Position = TimeSpan.FromMilliseconds(3500); Assert.That(owner.SplitControlPointCommand.CanExecute(request), Is.False);
            owner.SplitControlPointCommand.Execute(request); Assert.That(project.ControlPoints, Has.Count.EqualTo(2));
            owner.Position = TimeSpan.FromMilliseconds(2000); Assert.That(owner.SplitControlPointCommand.CanExecute(request), Is.True);
            Assert.That(notifications, Is.GreaterThanOrEqualTo(3));
            owner.SetControlPointCommand.Execute(new(state.Context, id, 50, null));
            owner.SetControlPointCommand.Execute(new(state.Context, id, 25, null, state.Revision));
            owner.SetControlPointDisabledCommand.Execute(new(state.Context, id, state.Revision, true));
            owner.SplitControlPointCommand.Execute(request);
            Assert.That((project.ControlPoints[0].Volume, project.ControlPoints[0].IsDisabled, project.ControlPoints.Count), Is.EqualTo((50, false, 2)));
            await WorkstationControlPointStateTests.Ready(owner);
            owner.SplitControlPointCommand.Execute(request); Assert.That(project.ControlPoints, Has.Count.EqualTo(2));
        }
        finally { await owner.DeactivateAsync(); }
    }

    [Test]
    public async Task EarlierDuplicateCannotSplit()
    {
        var owner = WorkstationControlPointStateTests.Create(new() { Offsets = [0], BPMs = [120], ControlPoints =
            [new() { Timestamp = 1000 }, new() { Timestamp = 1000 }, new() { Timestamp = 3000 }] }, new());
        try
        {
            await WorkstationControlPointStateTests.Ready(owner); owner.Position = TimeSpan.FromMilliseconds(2000); var state = owner.ControlPointsState!;
            Assert.That(owner.SplitControlPointCommand.CanExecute(new(state.Context, state.Points[0].Id, state.Revision)), Is.False);
            Assert.That(owner.SplitControlPointCommand.CanExecute(new(state.Context, state.Points[1].Id, state.Revision)), Is.True);
        }
        finally { await owner.DeactivateAsync(); }
    }
}
