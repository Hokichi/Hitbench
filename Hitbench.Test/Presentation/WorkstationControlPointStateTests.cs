using Hitbench.Core.Entities;
using Hitbench.Resources.Messages;
using Hitbench.Services;
using Hitbench.ViewModels.Shell;

namespace Hitbench.Test.Presentation;

public class WorkstationControlPointStateTests
{
    internal static WorkstationPageVM Create(ProjectData project, StubWorkstationAudioService audio,
        Hitbench.Services.ProjectFiles.IProjectFileService? files = null) =>
        new(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", project)) },
            new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler(), projectFiles: files);

    internal static async Task Ready(WorkstationPageVM owner)
    {
        await owner.LoadAsync("project.json", default);
        owner.AcknowledgePresentationReady(owner.LoadGeneration);
    }

    [Test]
    public async Task ProjectContextAllowsEditingWithoutOpeningUiAndPreservesLegacyVolumes()
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120], ControlPoints =
            [new() { Volume = 0 }, new() { Timestamp = 500, Volume = 4 }, new() { Timestamp = 1000, Volume = 5 }, new() { Timestamp = 1500, Volume = 100 }] };
        var audio = new StubWorkstationAudioService(); var files = new DelayedProjectFileService();
        var owner = Create(project, audio, files);
        try
        {
            await Ready(owner);
            var state = owner.ControlPointsState!;
            Assert.That(state.Points.Select(p => p.Volume), Is.EqualTo(new[] { 0, 4, 5, 100 }));
            Assert.That(files.Writes, Is.Zero);
            var originalAudio = audio.ControlPoints;
            owner.SetControlPointCommand.Execute(new SetControlPointRequestedMessage(state.Context, state.Points[1].Id, null, "Drum"));
            Assert.That((project.ControlPoints[1].Volume, project.ControlPoints[1].Sampleset), Is.EqualTo((4, "Drum")));
            Assert.That(audio.ControlPoints, Is.SameAs(originalAudio));
            Assert.That(files.Writes, Is.EqualTo(1));
            Assert.That(state.Points[1].Sampleset, Is.EqualTo("Normal"), "Prior snapshot must stay immutable.");
        }
        finally { files.Release(); await owner.DeactivateAsync(); }
    }

    [TestCase(-1)][TestCase(0)][TestCase(4)][TestCase(101)][TestCase(50.5)]
    public async Task InvalidExplicitEditsDoNotPublishOrSave(decimal volume)
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120], ControlPoints = [new() { Volume = 50 }] };
        var audio = new StubWorkstationAudioService(); var files = new DelayedProjectFileService(); var owner = Create(project, audio, files);
        try
        {
            await Ready(owner); var state = owner.ControlPointsState!; var timeline = audio.ControlPoints;
            owner.SetControlPointCommand.Execute(new SetControlPointRequestedMessage(state.Context, state.Points[0].Id, volume, "Drum"));
            Assert.That((project.ControlPoints[0].Volume, project.ControlPoints[0].Sampleset), Is.EqualTo((50, "Normal")));
            Assert.That(owner.ControlPointError, Is.Not.Empty);
            Assert.That(audio.ControlPoints, Is.SameAs(timeline)); Assert.That(files.Writes, Is.Zero);
        }
        finally { files.Release(); await owner.DeactivateAsync(); }
    }

    [Test]
    public async Task AcceptedEditsAndDeletePublishBeforeBlockedSaveAndDrainInOrder()
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120], ControlPoints = [new() { Volume = 50 }] };
        var audio = new StubWorkstationAudioService(); var files = new DelayedProjectFileService(); var owner = Create(project, audio, files);
        try
        {
            await Ready(owner); var state = owner.ControlPointsState!; var id = state.Points[0].Id;
            foreach (var volume in new[] { 5, 100 })
            {
                owner.SetControlPointCommand.Execute(new SetControlPointRequestedMessage(state.Context, id, volume, null));
                Assert.That(audio.ControlPoints.Single().Volume, Is.EqualTo(volume));
            }
            Assert.That(files.FirstWriteStarted, Is.True); Assert.That(files.LatestJson, Is.Null);
            owner.DeleteControlPointCommand.Execute(new DeleteControlPointRequestedMessage(state.Context, id));
            Assert.That(project.ControlPoints, Is.Empty); Assert.That(audio.ControlPoints, Is.Empty);
            Assert.That(new ControlPointVolumeTimeline(audio.ControlPoints).GetGain(88200), Is.EqualTo(1f));
            var revision = owner.ControlPointsState!.Revision;
            owner.DeleteControlPointCommand.Execute(new DeleteControlPointRequestedMessage(state.Context, id));
            Assert.That(owner.ControlPointsState.Revision, Is.EqualTo(revision));
            files.Release(); await owner.FlushSavesAsync();
            Assert.That(files.Writes, Is.EqualTo(2), "In-flight snapshot plus latest pending deletion");
            Assert.That(Hitbench.Services.ProjectFiles.ProjectDataSerializer.Deserialize(files.LatestJson!).ControlPoints, Is.Empty);
        }
        finally { files.Release(); await owner.DeactivateAsync(); }
    }

    [Test]
    public async Task SaveFailureRetryKeepsAcceptedStateAndDoesNotAdvancePointRevision()
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120], ControlPoints = [new()] };
        var files = new FailOnceProjectFileService(); var audio = new StubWorkstationAudioService(); var owner = Create(project, audio, files);
        try
        {
            await Ready(owner); var state = owner.ControlPointsState!;
            owner.SetControlPointCommand.Execute(new SetControlPointRequestedMessage(state.Context, state.Points[0].Id, 5, "Soft"));
            Assert.That(owner.SaveError, Does.Contain("Disk full"));
            Assert.That(audio.ControlPoints.Single().Volume, Is.EqualTo(5));
            var revision = owner.ControlPointsState!.Revision;
            owner.RetryControlPointsSaveCommand.Execute(new RetryControlPointsSaveRequestedMessage(state.Context));
            await owner.FlushSavesAsync();
            Assert.That(owner.SaveError, Is.Null); Assert.That(files.SavedJson, Is.Not.Null);
            Assert.That(owner.ControlPointsState!.Revision, Is.EqualTo(revision));
        }
        finally { await owner.DeactivateAsync(); }
    }

    [Test]
    public async Task ReloadAndDeactivateRejectOldContext()
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120], ControlPoints = [new() { Volume = 50 }] };
        var owner = Create(project, new());
        try
        {
            await Ready(owner); var old = owner.ControlPointsState!;
            await Ready(owner);
            owner.SetControlPointCommand.Execute(new SetControlPointRequestedMessage(old.Context, old.Points[0].Id, 5, "Drum"));
            Assert.That(project.ControlPoints[0].Volume, Is.EqualTo(50));
            var current = owner.ControlPointsState!;
            await owner.DeactivateAsync();
            owner.SetControlPointCommand.Execute(new SetControlPointRequestedMessage(current.Context, current.Points[0].Id, 5, "Drum"));
            Assert.That(project.ControlPoints[0].Volume, Is.EqualTo(50)); Assert.That(owner.ControlPointsState, Is.Null);
        }
        finally { await owner.DeactivateAsync(); }
    }

    [Test]
    public async Task CancelledReturnReactivatesEditingWithNewContextAndRejectsOldEditor()
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120], ControlPoints = [new() { Volume = 50 }] };
        var owner = Create(project, new());
        try
        {
            await Ready(owner); var old = owner.ControlPointsState!;
            Assert.That(await owner.PrepareToReturnAsync(default), Is.True);
            owner.CancelReturn();
            Assert.That(owner.ControlPointsState, Is.Not.Null);
            var current = owner.ControlPointsState!; Assert.That(current.Context, Is.Not.EqualTo(old.Context));
            owner.SetControlPointCommand.Execute(new SetControlPointRequestedMessage(old.Context, old.Points[0].Id, 5, null));
            Assert.That(project.ControlPoints[0].Volume, Is.EqualTo(50));
            owner.SetControlPointCommand.Execute(new SetControlPointRequestedMessage(current.Context, current.Points[0].Id, 75, null));
            Assert.That(project.ControlPoints[0].Volume, Is.EqualTo(75));
        }
        finally { await owner.DeactivateAsync(); }
    }
}
