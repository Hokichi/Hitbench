using osuHitsounder.Core.Entities;
using osuHitsounder.Services;
using osuHitsounder.ViewModels.Shell;
namespace osuHitsounder.Test.Presentation;
public sealed class WorkstationTrackRenameTests
{
    [TestCase(true)][TestCase(false)]
    public async Task InstrumentalCommitWaitsForFolderResult(bool succeeds)
    {
        var track = new InstrumentalTrack { Name = "Piano", FolderName = "Piano", KeyFiles = new() { ["C3"] = "Piano/C3.wav" } };
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var files = new StubSampleFileService(["Piano/C3.wav"]) { FolderRename = (_, _, _, _, _) => { calls++; requested.TrySetResult(); return completion.Task; } };
        var saves = new DelayedProjectFileService(); saves.Release();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", new ProjectData { Tracks = [track], Offsets = [0], BPMs = [120] })) }, new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), sampleFiles: files, projectFiles: saves);
        try
        {
            await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            vm.BeginTrackRenameCommand.Execute(track); vm.TrackNameDraft = "Soft";
            Assert.That(track.Name, Is.EqualTo("Piano")); Assert.That(calls, Is.Zero);
            var commit = vm.CommitTrackRenameAsync(track);
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await vm.CommitTrackRenameAsync(track);
            Assert.That(track.Name, Is.EqualTo("Piano")); Assert.That(saves.Writes, Is.Zero);
            if (succeeds) completion.SetResult("Soft"); else completion.SetException(new IOException("move failed"));
            await commit; await vm.FlushSavesAsync();
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(track.Name, Is.EqualTo(succeeds ? "Soft" : "Piano"));
            Assert.That(track.FolderName, Is.EqualTo(succeeds ? "Soft" : "Piano"));
            Assert.That(track.KeyFiles["C3"], Is.EqualTo(succeeds ? "Soft/C3.wav" : "Piano/C3.wav"));
            Assert.That(saves.Writes, Is.EqualTo(succeeds ? 1 : 0));
            if (!succeeds) Assert.That(vm.ErrorMessage, Does.Contain("move failed"));
        }
        finally { completion.TrySetResult("Soft"); await vm.DeactivateAsync(); }
    }
}
