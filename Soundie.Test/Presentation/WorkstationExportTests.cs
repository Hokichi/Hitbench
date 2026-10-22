using CommunityToolkit.Mvvm.Messaging;
using Soundie.Core.Entities;
using Soundie.Resources.Messages;
using Soundie.Services;
using Soundie.Services.ProjectFiles;
using Soundie.ViewModels.Shell;
namespace Soundie.Test.Presentation;
public class WorkstationExportTests
{
    [TestCase(false)] [TestCase(true)]
    public async Task PendingSaveSettlesBeforeSnapshotAndSaveFailurePreventsExport(bool fail)
    {
        var release = new TaskCompletionSource();
        var files = new StubProjectFileService((_, _, _) => Task.FromResult("project.json"))
        { Save = async (_, _, _) => { await release.Task; if (fail) throw new IOException("save failed"); } };
        var track = new Track { Volume = 70 };
        var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "song.wav", new ProjectData { Tracks = [track], Offsets = [0], BPMs = [120] })) };
        var dialog = new StubOHsExportDialogService();
        var vm = new WorkstationPageVM(source, new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), projectFiles: files, exportDialog: dialog);
        await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
        vm.SetTrackVolume(track, 80);
        var export = vm.ExportCommand.ExecuteAsync(null);
        Assert.That(dialog.Calls, Is.Zero); Assert.That(vm.IsExporting, Is.True);
        release.SetResult(); await export;
        if (fail)
        {
            Assert.That(dialog.Calls, Is.Zero); Assert.That(vm.CanExport, Is.False);
            files.Save = (_, _, _) => Task.CompletedTask; vm.RetrySaveCommand.Execute(null); await vm.FlushSavesAsync();
        }
        else Assert.That(ProjectDataSerializer.Deserialize(dialog.LastRequest!.ProjectJson).Tracks.Single().Volume, Is.EqualTo(80));
        await vm.DeactivateAsync();
    }
    [Test]
    public async Task ReadyExportPausesAndSnapshotsActualSourceAndRejectsCompetingEdits()
    {
        var track = new Track { Volume = 70 }; var data = new ProjectData { Tracks = [track], Offsets = [0], BPMs = [120] };
        var audio = new StubWorkstationAudioService(); var dialog = new StubOHsExportDialogService();
        var release = new TaskCompletionSource(); dialog.Run = (_, _) => release.Task;
        var vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("chosen.osu", "actual.wav", data)) }, new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler(), exportDialog: dialog);
        Assert.That(vm.CanExport, Is.False); await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
        Assert.That(vm.CanExport, Is.True);
        var export = vm.ExportCommand.ExecuteAsync(null);
        Assert.That(vm.IsExporting, Is.True); Assert.That(vm.CanEditTracks, Is.False); Assert.That(dialog.Calls, Is.EqualTo(1));
        vm.SetTrackVolume(track, 25); Assert.That(track.Volume, Is.EqualTo(70));
        Assert.That((dialog.LastRequest!.BeatmapPath, dialog.LastRequest.AudioPath), Is.EqualTo(("chosen.osu", "actual.wav")));
        Assert.That(ProjectDataSerializer.Deserialize(dialog.LastRequest.ProjectJson).Tracks.Single().Volume, Is.EqualTo(70));
        await vm.ExportCommand.ExecuteAsync(null); Assert.That(dialog.Calls, Is.EqualTo(1));
        release.SetResult(); await export; Assert.That(vm.IsExporting, Is.False); Assert.That(audio.PauseCalls, Is.EqualTo(1)); Assert.That(audio.PlayCalls, Is.Zero);
        await vm.DeactivateAsync();
    }
    [TestCase(false)] [TestCase(true)]
    public async Task ReturnOrDeactivateCancelsAndWaitsForExport(bool deactivate)
    {
        var release = new TaskCompletionSource(); CancellationToken observed = default;
        var dialog = new StubOHsExportDialogService { Run = (_, token) => { observed = token; return release.Task; } };
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), exportDialog: dialog);
        await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
        var export = vm.ExportCommand.ExecuteAsync(null);
        Task closing = deactivate ? vm.DeactivateAsync() : vm.PrepareToReturnAsync(default);
        Assert.That(observed.IsCancellationRequested, Is.True); Assert.That(closing.IsCompleted, Is.False);
        release.SetResult(); await export; await closing;
        if (!deactivate) await vm.DeactivateAsync();
    }
    [Test]
    public async Task ResultsFromPreviousOperationCannotChangeReloadedProject()
    {
        var messenger = new WeakReferenceMessenger(); var dialog = new StubOHsExportDialogService();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), messenger: messenger, exportDialog: dialog);
        await vm.LoadAsync("first.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration); await vm.ExportCommand.ExecuteAsync(null);
        var old = dialog.LastRequest!;
        await vm.LoadAsync("second.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.ErrorMessage = "current";
        messenger.Send(new OHsExportResultMessage(old.OperationId, old.ProjectPath, false, "stale"));
        Assert.That(vm.ErrorMessage, Is.EqualTo("current")); await vm.DeactivateAsync();
    }
}
