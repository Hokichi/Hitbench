using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.DataModels;
using Hitbench.Services;
using Hitbench.ViewModels.Shell;

namespace Hitbench.Test.Presentation;

public class WorkstationHitNormalTests
{
    [TestCase(false)][TestCase(true)]
    public async Task SaveOrCancelDoesNotChangePlayback(bool save)
    {
        var audio = new StubWorkstationAudioService();
        var writes = new List<string>();
        var files = new StubProjectFileService((_, _, _) => Task.FromResult("unused")) {
            Save = (_, json, _) => { writes.Add(json); return Task.CompletedTask; }
        };
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler(),
            projectFiles: files, hitNormalDialog: new StubOHsHitNormalDialogService { Request = (_, _) =>
                Task.FromResult(save ? new OHsHitNormalConfiguration("n.wav", "s.wav", "d.wav") : null) });
        try
        {
            await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            var updates = audio.SampleUpdates.Count; var plays = audio.PlayCalls;
            await vm.ConfigHitNormalCommand.ExecuteAsync(null); await vm.FlushSavesAsync();
            Assert.That(vm.Project!.NormalHitNormalPath, Is.EqualTo(save ? "n.wav" : ""));
            Assert.That(writes.Count, Is.EqualTo(save ? 1 : 0));
            Assert.That(audio.SampleUpdates.Count, Is.EqualTo(updates));
            Assert.That(audio.PlayCalls, Is.EqualTo(plays));
            Assert.That(vm.Project.Tracks, Is.Empty);
        }
        finally { await vm.DeactivateAsync(); }
    }

    [TestCase(false)][TestCase(true)]
    public async Task CancelledLateCompletionCannotCommit(bool replaceProject)
    {
        var gate = new TaskCompletionSource<OHsHitNormalConfiguration?>();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(),
            hitNormalDialog: new StubOHsHitNormalDialogService { Request = (_, _) => gate.Task });
        await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
        var project = vm.Project!;
        var editing = vm.ConfigHitNormalCommand.ExecuteAsync(null);
        Assert.That(vm.ConfigHitNormalCommand.CanExecute(null), Is.False);
        var closing = replaceProject ? vm.LoadAsync("next.json", default) : vm.DeactivateAsync();
        gate.SetResult(new("late.wav"));
        await editing; await closing;
        Assert.That(project.NormalHitNormalPath, Is.Empty);
        if (replaceProject) { Assert.That(vm.Project!.NormalHitNormalPath, Is.Empty); await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task RenameAndDeletionPreserveConfiguredIntent()
    {
        var messenger = new CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger();
        var project = new Hitbench.Core.Entities.ProjectData { NormalHitNormalPath = "old.wav", SoftHitNormalPath = "old.wav", Offsets = [0], BPMs = [120], ControlPoints = [new()] };
        var vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new Hitbench.Core.Entities.WorkstationSource("map", "song", project)) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(),
            sampleFiles: new StubSampleFileService(["old.wav"]), messenger: messenger);
        try
        {
            await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            messenger.Send(new Hitbench.Resources.Messages.ProjectSamplesChangedMessage(vm.SampleManagementContext!, ["new.wav"], "old.wav", "new.wav"));
            await vm.FlushSavesAsync();
            Assert.That(project.NormalHitNormalPath, Is.EqualTo("new.wav")); Assert.That(project.SoftHitNormalPath, Is.EqualTo("new.wav"));
            messenger.Send(new Hitbench.Resources.Messages.ProjectSamplesChangedMessage(vm.SampleManagementContext!, []));
            await vm.FlushSavesAsync();
            Assert.That(project.NormalHitNormalPath, Is.EqualTo("new.wav"));
        }
        finally { await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task InstrumentFolderRenameRewritesConfiguredRelativePaths()
    {
        var track = new Hitbench.Core.Entities.InstrumentalTrack { Name = "Piano", FolderName = "Piano" };
        var project = new Hitbench.Core.Entities.ProjectData { DrumHitNormalPath = "Piano/C3.wav", Tracks = [track], Offsets = [0], BPMs = [120] };
        var vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new Hitbench.Core.Entities.WorkstationSource("map", "song", project)) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), sampleFiles: new StubSampleFileService(["Piano/C3.wav"]));
        try
        {
            await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            vm.SetTrackName(track, "Soft Piano"); await vm.FlushSavesAsync();
            Assert.That(project.DrumHitNormalPath, Is.EqualTo("Soft Piano/C3.wav"));
        }
        finally { await vm.DeactivateAsync(); }
    }

    [TestCase(false)][TestCase(true)]
    public async Task UnchangedSaveOrPresentationErrorDoesNotWrite(bool fail)
    {
        var writes = 0;
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(),
            projectFiles: new StubProjectFileService((_, _, _) => Task.FromResult("unused")) { Save = (_, _, _) => { writes++; return Task.CompletedTask; } },
            hitNormalDialog: new StubOHsHitNormalDialogService { Request = (current, _) => fail ? Task.FromException<OHsHitNormalConfiguration?>(new IOException("present failed")) : Task.FromResult<OHsHitNormalConfiguration?>(current) });
        try
        {
            await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            await vm.ConfigHitNormalCommand.ExecuteAsync(null); await vm.FlushSavesAsync();
            Assert.That(writes, Is.Zero); Assert.That(vm.Project!.NormalHitNormalPath, Is.Empty);
            Assert.That(vm.ConfigHitNormalCommand.CanExecute(null), Is.True);
            Assert.That(vm.ErrorMessage, Is.EqualTo(fail ? "present failed" : null));
        }
        finally { await vm.DeactivateAsync(); }
    }
}
