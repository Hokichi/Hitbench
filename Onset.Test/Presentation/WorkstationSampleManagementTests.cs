using Onset.Services.ProjectFiles;
using CommunityToolkit.Mvvm.Messaging;
using Onset.Resources.Messages;
using Onset.Resources.DataModels;
using Onset.Services;
using Onset.ViewModels.Shell;

namespace Onset.Test.Presentation;

public sealed class WorkstationSampleManagementTests
{
    [Test]
    public async Task ScopedNotificationsKeepRootChoicesAndNestedInstrumentAvailabilitySeparate()
    {
        var instrument = new Onset.Core.Entities.InstrumentalTrack
        {
            KeyFiles = new() { ["C3"] = "Piano/C3.wav" }, Notes = [new() { Key = "C3", Timestamp = 125 }]
        };
        var sample = new Onset.Core.Entities.Track { SelectedSampleFileName = "Piano/C3.wav" };
        var project = new Onset.Core.Entities.ProjectData { Tracks = [sample, instrument], Offsets = [0], BPMs = [120] };
        var messenger = new WeakReferenceMessenger();
        var audio = new StubWorkstationAudioService();
        var manager = new StubSampleManagementDialogService();
        var dialogs = new StubInstrumentalTrackDialogService();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new Onset.Core.Entities.WorkstationSource("map", "song", project)) },
            new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler(), new StubSampleFileService(["kick.wav", "Piano/C3.wav"]),
            new StubSampleFileDialogService([]), messenger: messenger, sampleManagementDialog: manager, instrumentDialogs: dialogs);
        try
        {
            await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            Assert.That(vm.SampleFiles, Is.EqualTo(new[] { "kick.wav" }));
            Assert.That(sample.SelectedSampleFileName, Is.EqualTo("Piano/C3.wav"));
            Assert.That(vm.GetSampleAvailability(sample), Is.EqualTo(TrackSampleAvailability.Missing));
            manager.Present = (context, _) =>
            {
                messenger.Send(new ProjectSamplesChangedMessage(context, ["snare.wav", "Piano/C3.wav"]));
                return Task.CompletedTask;
            };
            await vm.ManageSamplesCommand.ExecuteAsync(null);
            Assert.That(vm.SampleFiles, Is.EqualTo(new[] { "snare.wav" }));
            manager.Present = (context, _) =>
            {
                messenger.Send(new ProjectSamplesChangedMessage(context, ["hat.wav"], includesSubfolders: false));
                return Task.CompletedTask;
            };
            await vm.ManageSamplesCommand.ExecuteAsync(null);
            Assert.That(vm.SampleFiles, Is.EqualTo(new[] { "hat.wav" }));
            dialogs.Mapping = (snapshot, _) =>
            {
                Assert.That(snapshot.AvailableFiles, Does.Contain("Piano/C3.wav"));
                return Task.CompletedTask;
            };
            await vm.KeysMappingCommand.ExecuteAsync(instrument);
            await vm.FlushPlaybackUpdatesAsync();
            Assert.That(audio.SampleUpdates.Last().Single().FileName, Is.EqualTo("Piano/C3.wav"));
        }
        finally { await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task OpeningManagerPreservesPlayingSnapshotAndTransport()
    {
        var audio = new StubWorkstationAudioService(); var closed = new TaskCompletionSource(); var presented = new TaskCompletionSource();
        var manager = new StubSampleManagementDialogService { Present = (_, token) => { presented.SetResult(); return closed.Task.WaitAsync(token); } };
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler(),
            new StubSampleFileService(["hit.wav"]), new StubSampleFileDialogService([]), sampleManagementDialog: manager);
        try
        {
            await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            await vm.SeekCommand.ExecuteAsync(TimeSpan.FromSeconds(3)); await vm.PlayPauseCommand.ExecuteAsync(null);
            var managing = vm.ManageSamplesCommand.ExecuteAsync(null); await presented.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(audio.Snapshot.IsPlaying, Is.True); Assert.That(audio.Snapshot.Position, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(vm.PlayPauseCommand.CanExecute(null), Is.True); Assert.That(vm.StopCommand.CanExecute(null), Is.True);
            Assert.That(vm.SeekCommand.CanExecute(TimeSpan.FromSeconds(4)), Is.True);
            Assert.That(vm.ManageSamplesCommand.CanExecute(null), Is.False); Assert.That(vm.CanImportSample, Is.False);
            await vm.PlayPauseCommand.ExecuteAsync(null); Assert.That(audio.Snapshot.IsPlaying, Is.False);
            await vm.PlayPauseCommand.ExecuteAsync(null); Assert.That(audio.Snapshot.IsPlaying, Is.True);
            closed.SetResult(); await managing;
            Assert.That(audio.Snapshot.IsPlaying, Is.True); Assert.That(audio.Snapshot.Position, Is.EqualTo(TimeSpan.FromSeconds(3)));
        }
        finally { closed.TrySetResult(); await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task OpeningManagerDoesNotCancelPendingPlay()
    {
        var audio = new StubWorkstationAudioService(); var closed = new TaskCompletionSource(); var presented = new TaskCompletionSource();
        var prepared = new TaskCompletionSource(); var preparing = new TaskCompletionSource();
        var manager = new StubSampleManagementDialogService { Present = (_, token) => { presented.SetResult(); return closed.Task.WaitAsync(token); } };
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler(),
            new StubSampleFileService(["hit.wav"]), new StubSampleFileDialogService([]), sampleManagementDialog: manager);
        try
        {
            await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration); await vm.FlushPlaybackUpdatesAsync();
            audio.PrepareSamplesAsync = token => { preparing.TrySetResult(); return prepared.Task.WaitAsync(token); };
            vm.AddSampleTrackCommand.Execute(null);
            Assert.That(vm.PlaceSample(vm.Tracks.Single(), 0), Is.True);
            await preparing.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var playing = vm.PlayPauseCommand.ExecuteAsync(null);
            var managing = vm.ManageSamplesCommand.ExecuteAsync(null); await presented.Task.WaitAsync(TimeSpan.FromSeconds(3));
            prepared.SetResult(); await playing;
            Assert.That(audio.Snapshot.IsPlaying, Is.True);
            closed.SetResult(); await managing; Assert.That(audio.Snapshot.IsPlaying, Is.True);
        }
        finally { prepared.TrySetResult(); closed.TrySetResult(); await vm.DeactivateAsync(); }
    }

    [Test] public async Task PreviousSessionCannotChangeReloadedProject()
    {
        var messenger=new WeakReferenceMessenger(); var manager=new StubSampleManagementDialogService();
        SampleManagementContext? previous=null;
        manager.Present=(context,_)=>{previous=context;return Task.CompletedTask;};
        var vm=new WorkstationPageVM(new StubWorkstationSourceService(),new MarkerPlaybackCoordinator(new StubWorkstationAudioService(),Task.Delay),new ImmediateUiScheduler(),
            new StubSampleFileService(["hit.wav"]),new StubSampleFileDialogService(Array.Empty<string>()),messenger:messenger,sampleManagementDialog:manager);
        await vm.LoadAsync("project.json",default); vm.AcknowledgePresentationReady(vm.LoadGeneration); await vm.ManageSamplesCommand.ExecuteAsync(null);
        await vm.LoadAsync("project.json",default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
        messenger.Send(new ProjectSamplesChangedMessage(previous!,["obsolete.wav"])); await vm.FlushSavesAsync();
        Assert.That(vm.SampleFiles,Is.EqualTo(new[]{"hit.wav"})); await vm.DeactivateAsync();
        messenger.Send(new ProjectSamplesChangedMessage(previous!,[])); Assert.That(vm.SampleFiles,Is.EqualTo(new[]{"hit.wav"}));
    }
    [Test] public async Task RenameAndDeletionKeepPlacementsAndRestoreAvailability()
    {
        var messenger = new WeakReferenceMessenger(); var files = new StubSampleFileService(["hit.wav", "replacement.wav"]);
        var manager = new StubSampleManagementDialogService();
        var saves = new DelayedProjectFileService(); saves.Release();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), MarkerPlaybackCoordinator.DelayAsync),
            new ImmediateUiScheduler(), files, new StubSampleFileDialogService(Array.Empty<string>()), projectFiles:saves, messenger: messenger, sampleManagementDialog: manager);
        await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
        vm.AddSampleTrackCommand.Execute(null); var track = vm.Tracks.Single(); vm.PlaceSample(track, 0); var node = track.Samples.Single();
        manager.Present = (context, _) => { messenger.Send(new ProjectSamplesChangedMessage(context, ["kick.wav", "replacement.wav"], "hit.wav", "kick.wav")); return Task.CompletedTask; };
        await vm.ManageSamplesCommand.ExecuteAsync(null);
        Assert.That(track.SelectedSampleFileName, Is.EqualTo("kick.wav")); Assert.That(node.FileName, Is.EqualTo("kick.wav"));
        await vm.FlushSavesAsync(); Assert.That(ProjectDataSerializer.Deserialize(saves.LatestJson!).Tracks.Single().Samples.Single().FileName,Is.EqualTo("kick.wav"));
        manager.Present = (context, _) => { messenger.Send(new ProjectSamplesChangedMessage(context, ["replacement.wav"], "kick.wav")); return Task.CompletedTask; };
        await vm.ManageSamplesCommand.ExecuteAsync(null);
        Assert.That(track.Samples.Single(), Is.SameAs(node)); Assert.That(vm.GetSampleAvailability(track), Is.EqualTo(TrackSampleAvailability.Missing));
        Assert.That(vm.PlaceSample(track, 200), Is.False);
        vm.SelectSample(track, "replacement.wav"); Assert.That(vm.GetSampleAvailability(track), Is.EqualTo(TrackSampleAvailability.Available));
        Assert.That(node.FileName, Is.EqualTo("replacement.wav")); await vm.DeactivateAsync();
    }
}
