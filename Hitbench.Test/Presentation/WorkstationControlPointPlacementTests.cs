using Hitbench.Core.Entities;
using Hitbench.Resources.Messages;
using Hitbench.Services;

namespace Hitbench.Test.Presentation;

public class WorkstationControlPointPlacementTests
{
    [TestCase(0, "Drum")][TestCase(4, "Soft")][TestCase(75, "Drum")]
    public async Task AddCapturesMarkerAndInheritsBothPredecessorFields(int volume, string sampleset)
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120], ControlPoints =
            [new() { Timestamp = 2000, Volume = 25 }, new() { Timestamp = 900, Volume = volume, Sampleset = sampleset }] };
        var audio = new StubWorkstationAudioService(); var files = new DelayedProjectFileService();
        var owner = WorkstationControlPointStateTests.Create(project, audio, files);
        try
        {
            await WorkstationControlPointStateTests.Ready(owner);
            owner.Position = TimeSpan.FromMilliseconds(1030); owner.SelectedSubdivisionIndex = 3;
            await owner.AddControlPointCommand.ExecuteAsync(null); var intent = owner.PendingControlPointAdd!;
            Assert.That((intent.Timestamp, intent.Volume, intent.Sampleset), Is.EqualTo((1000, volume, sampleset)));
            Assert.That(project.ControlPoints, Has.Count.EqualTo(3)); Assert.That(files.Writes, Is.Zero);
            owner.Position = TimeSpan.FromMilliseconds(3000); owner.SelectedSubdivisionIndex = 0;
            owner.CommitControlPointAddCommand.Execute(intent);
            var added = owner.ControlPointsState!.Points.Single(p => p.Id == intent.PointId);
            Assert.That((added.Timestamp, added.Volume, added.Sampleset), Is.EqualTo((1000, volume, sampleset)));
            Assert.That(audio.ControlPoints.Select(p => p.Timestamp), Does.Contain(1000)); Assert.That(files.FirstWriteStarted, Is.True);
            owner.CommitControlPointAddCommand.Execute(intent); Assert.That(project.ControlPoints, Has.Count.EqualTo(4));
        }
        finally { files.Release(); await owner.DeactivateAsync(); }
    }

    [Test]
    public async Task PlayingAddUsesLatestPlaybackRatherThanOldPresentationPosition()
    {
        var audio = new StubWorkstationAudioService(); var owner = WorkstationControlPointStateTests.Create(new() { Offsets = [0], BPMs = [120] }, audio);
        try
        {
            await WorkstationControlPointStateTests.Ready(owner); owner.Position = TimeSpan.FromMilliseconds(250);
            audio.Snapshot = new(TimeSpan.FromMilliseconds(1030), false, null);
            await owner.PlayPauseCommand.ExecuteAsync(null);
            owner.Position = TimeSpan.FromMilliseconds(250);
            owner.IsPlaying = false; // Presentation may lag the coordinator by a frame.
            await owner.AddControlPointCommand.ExecuteAsync(null);
            Assert.That(owner.PendingControlPointAdd!.Timestamp, Is.EqualTo(1000));
            Assert.That((owner.PendingControlPointAdd.Volume, owner.PendingControlPointAdd.Sampleset), Is.EqualTo((100, "Normal")));
        }
        finally { await owner.DeactivateAsync(); }
    }

    [Test]
    public async Task DuplicateForgedAndStaleAddNeverReplaceOrPersist()
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120], ControlPoints = [new() { Timestamp = 0 }] };
        var files = new DelayedProjectFileService(); var audio = new StubWorkstationAudioService(); var owner = WorkstationControlPointStateTests.Create(project, audio, files);
        try
        {
            await WorkstationControlPointStateTests.Ready(owner); var state = owner.ControlPointsState!; var timeline = audio.ControlPoints;
            await owner.AddControlPointCommand.ExecuteAsync(null); var duplicate = owner.PendingControlPointAdd!;
            owner.CommitControlPointAddCommand.Execute(duplicate);
            Assert.That(project.ControlPoints, Has.Count.EqualTo(1)); Assert.That(owner.ControlPointError, Is.Not.Empty);
            owner.Position = TimeSpan.FromMilliseconds(2000); await owner.AddControlPointCommand.ExecuteAsync(null); var intent = owner.PendingControlPointAdd!;
            owner.CommitControlPointAddCommand.Execute(intent with { Timestamp = 3000 });
            Assert.That(project.ControlPoints, Has.Count.EqualTo(1)); Assert.That(files.Writes, Is.Zero); Assert.That(audio.ControlPoints, Is.SameAs(timeline));
            owner.SetControlPointCommand.Execute(new SetControlPointRequestedMessage(state.Context, state.Points[0].Id, 50, null));
            owner.CommitControlPointAddCommand.Execute(intent);
            Assert.That(project.ControlPoints, Has.Count.EqualTo(1));
        }
        finally { files.Release(); await owner.DeactivateAsync(); }
    }

    [Test]
    public async Task MovePreservesIdentityAndSettingsRejectsDuplicateStaleAndNoOp()
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120], ControlPoints =
            [new() { Timestamp = 500, Volume = 4, Sampleset = "Drum" }, new() { Timestamp = 1000 }] };
        var audio = new StubWorkstationAudioService(); var files = new DelayedProjectFileService(); var owner = WorkstationControlPointStateTests.Create(project, audio, files);
        try
        {
            await WorkstationControlPointStateTests.Ready(owner); var state = owner.ControlPointsState!; var point = state.Points.Single(p => p.Timestamp == 500); var markers = owner.ControlPointTimestamps;
            owner.MoveControlPointCommand.Execute(new MoveControlPointRequestedMessage(state.Context, point.Id, state.Revision, 1000));
            Assert.That(files.Writes, Is.Zero); Assert.That(owner.ControlPointTimestamps, Is.SameAs(markers));
            owner.MoveControlPointCommand.Execute(new MoveControlPointRequestedMessage(state.Context, point.Id, state.Revision, 2000));
            var moved = owner.ControlPointsState!.Points.Single(p => p.Id == point.Id);
            Assert.That((moved.Timestamp, moved.Volume, moved.Sampleset), Is.EqualTo((2000, 4, "Drum")));
            Assert.That(owner.ControlPointTimestamps, Is.EqualTo(new[] { 0, 1000, 2000 })); Assert.That(markers, Is.EqualTo(new[] { 0, 500, 1000 }));
            var timeline = audio.ControlPoints;
            owner.MoveControlPointCommand.Execute(new MoveControlPointRequestedMessage(state.Context, point.Id, state.Revision, 3000));
            owner.MoveControlPointCommand.Execute(new MoveControlPointRequestedMessage(state.Context, point.Id, owner.ControlPointsState.Revision, 2000));
            Assert.That(audio.ControlPoints, Is.SameAs(timeline)); Assert.That(files.Writes, Is.EqualTo(1));
        }
        finally { files.Release(); await owner.DeactivateAsync(); }
    }

    [Test]
    public async Task InvalidTimingAndInactiveOwnerCannotCreateIntentOrAcceptOldOne()
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120] }; var owner = WorkstationControlPointStateTests.Create(project, new());
        try
        {
            await WorkstationControlPointStateTests.Ready(owner); await owner.AddControlPointCommand.ExecuteAsync(null); var old = owner.PendingControlPointAdd!;
            await owner.SettleControlPointsAsync(); owner.CommitControlPointAddCommand.Execute(old);
            Assert.That(project.ControlPoints.Single().Timestamp, Is.Zero); Assert.That(owner.PendingControlPointAdd, Is.Null);
            await WorkstationControlPointStateTests.Ready(owner); project.BPMs = [];
            await owner.AddControlPointCommand.ExecuteAsync(null); Assert.That(owner.PendingControlPointAdd, Is.Null);
        }
        finally { await owner.DeactivateAsync(); }
    }
}
