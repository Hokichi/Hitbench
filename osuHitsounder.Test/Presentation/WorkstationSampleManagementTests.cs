using osuHitsounder.Services.ProjectFiles;
using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Resources.Messages;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Services;
using osuHitsounder.ViewModels.Shell;

namespace osuHitsounder.Test.Presentation;

public sealed class WorkstationSampleManagementTests
{
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
