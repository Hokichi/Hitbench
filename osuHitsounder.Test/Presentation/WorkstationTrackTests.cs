using osuHitsounder.Core.Entities;
using osuHitsounder.ViewModels.Shell;
using osuHitsounder.Services.FileSystem;
using osuHitsounder.Services.ProjectFiles;
using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Resources.Messages;

namespace osuHitsounder.Test.Presentation;

public class WorkstationTrackTests
{
    [Test]
    public async Task ConfirmedRemovalDeletesOnlyChosenTrackAndPersistsSnapshot()
    {
        var first = new Track { Samples = [new Sample { FileName = "a.wav", Timestamps = [125] }] };
        var second = new Track { Samples = [new Sample { FileName = "b.wav" }] };
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav",
                new ProjectData { Tracks = [first, second] }))
        };
        var saves = new DelayedProjectFileService();
        saves.Release();
        var dialog = new StubTrackRemovalDialogService { Confirm = _ => Task.FromResult(true) };
        var vm = new WorkstationPageVM(source, new StubWorkstationAudioService(),
            new ImmediateUiScheduler(), null, null, saves, null, dialog);
        await vm.LoadAsync("project.json", CancellationToken.None);
        await vm.RemoveTrackCommand.ExecuteAsync(first);
        await vm.FlushSavesAsync();
        Assert.That(vm.Tracks, Is.EqualTo(new[] { second }));
        Assert.That(vm.Project!.Tracks, Is.EqualTo(new[] { second }));
        Assert.That(ProjectDataSerializer.Deserialize(saves.LatestJson!).Tracks.Single().Samples.Single().FileName,
            Is.EqualTo("b.wav"));
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task CancelledRemovalKeepsTrackAndDoesNotSave()
    {
        var track = new Track { Samples = [new Sample { FileName = "a.wav" }] };
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav",
                new ProjectData { Tracks = [track] }))
        };
        var dialog = new StubTrackRemovalDialogService();
        var vm = new WorkstationPageVM(source, new StubWorkstationAudioService(),
            new ImmediateUiScheduler(), trackRemovalDialog: dialog);
        await vm.LoadAsync("project.json", CancellationToken.None);
        await vm.RemoveTrackCommand.ExecuteAsync(track);
        Assert.That(vm.Tracks, Is.EqualTo(new[] { track }));
        Assert.That(dialog.Calls, Is.EqualTo(1));
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task DuplicateRequestWhileConfirmationPendingIsIgnored()
    {
        var track = new Track();
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav",
                new ProjectData { Tracks = [track] }))
        };
        var response = new TaskCompletionSource<bool>();
        var dialog = new StubTrackRemovalDialogService { Confirm = _ => response.Task };
        var vm = new WorkstationPageVM(source, new StubWorkstationAudioService(),
            new ImmediateUiScheduler(), trackRemovalDialog: dialog);
        await vm.LoadAsync("project.json", CancellationToken.None);
        var first = vm.RemoveTrackCommand.ExecuteAsync(track);
        await vm.RemoveTrackCommand.ExecuteAsync(track);
        Assert.That(dialog.Calls, Is.EqualTo(1));
        Assert.That(vm.CanRemoveTrack, Is.False);
        response.SetResult(false);
        await first;
        Assert.That(vm.CanRemoveTrack, Is.True);
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task OldConfirmationCannotRemoveTrackAfterReload()
    {
        var original = new Track();
        var replacement = new Track();
        var source = new StubWorkstationSourceService
        {
            Load = (path, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav",
                new ProjectData { Tracks = [path == "old.json" ? original : replacement] }))
        };
        var response = new TaskCompletionSource<bool>();
        var dialog = new StubTrackRemovalDialogService { Confirm = _ => response.Task };
        var vm = new WorkstationPageVM(source, new StubWorkstationAudioService(),
            new ImmediateUiScheduler(), trackRemovalDialog: dialog);
        await vm.LoadAsync("old.json", CancellationToken.None);
        var pending = vm.RemoveTrackCommand.ExecuteAsync(original);
        await vm.LoadAsync("new.json", CancellationToken.None);
        response.SetResult(true);
        await pending;
        Assert.That(vm.Tracks, Is.EqualTo(new[] { replacement }));
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task RemovalDisablesWhenWorkstationDeactivates()
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(),
            new StubWorkstationAudioService(), new ImmediateUiScheduler(),
            trackRemovalDialog: new StubTrackRemovalDialogService());
        await vm.LoadAsync("project.json", CancellationToken.None);
        Assert.That(vm.CanRemoveTrack, Is.True);
        await vm.DeactivateAsync();
        Assert.That(vm.CanRemoveTrack, Is.False);
    }
    [Test]
    public async Task SelectionUpdatesEverySampleAndPreservesItsData()
    {
        var one = new Sample { FileName = "old.wav", Volume = 12, Timestamps = [125] };
        var two = new Sample { FileName = "old.wav", Volume = 25, Timestamps = [375] };
        var track = new Track { Samples = [one, two] };
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav", new ProjectData { Tracks = [track] }))
        };
        var vm = new WorkstationPageVM(source, new StubWorkstationAudioService(), new ImmediateUiScheduler());
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.SelectSample(track, "new.wav");
        Assert.That(track.Samples, Is.EqualTo(new[] { one, two }));
        Assert.That(track.Samples.Select(sample => sample.FileName), Is.All.EqualTo("new.wav"));
        Assert.That(track.Samples.Select(sample => sample.Volume), Is.EqualTo(new[] { 12, 25 }));
        Assert.That(track.Samples.Select(sample => sample.Timestamps.Single()), Is.EqualTo(new[] { 125, 375 }));
    }

    [Test]
    public async Task ImportedSelectionAndVolumePersistToProjectJson()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        try
        {
            var projectPath = Path.Combine(root.FullName, "project.json");
            await File.WriteAllTextAsync(projectPath, "{}");
            var samples = Directory.CreateDirectory(Path.Combine(root.FullName, "Samples"));
            await File.WriteAllBytesAsync(Path.Combine(samples.FullName, "a.wav"), [1]);
            var sourcePath = Path.Combine(root.FullName, "b.mp3");
            await File.WriteAllBytesAsync(sourcePath, [2]);
            var source = new StubWorkstationSourceService();
            var vm = new WorkstationPageVM(source, new StubWorkstationAudioService(), new ImmediateUiScheduler(),
                new SampleFileService(), new StubSampleFileDialogService(sourcePath),
                new ProjectFileService(root.FullName), new WeakReferenceMessenger());
            await vm.LoadAsync(projectPath, CancellationToken.None);
            vm.AddSampleTrackCommand.Execute(null);
            var track = vm.Tracks.Single();
            Assert.That(track.Samples.Single().FileName, Is.EqualTo("a.wav"));
            Assert.That(await vm.ImportSampleAsync(track), Is.EqualTo("b.mp3"));
            vm.SetTrackVolume(track, 70);
            await vm.FlushSavesAsync();
            var restored = ProjectDataSerializer.Deserialize(await File.ReadAllTextAsync(projectPath));
            Assert.That(restored.Tracks.Single().Samples.Single().FileName, Is.EqualTo("b.mp3"));
            Assert.That(restored.Tracks.Single().Volume, Is.EqualTo(70));
            Assert.That(await File.ReadAllBytesAsync(Path.Combine(samples.FullName, "b.mp3")), Is.EqualTo(new byte[] { 2 }));
            await vm.DeactivateAsync();
        }
        finally { Directory.Delete(root.FullName, true); }
    }

    [Test]
    public async Task PendingOlderSaveCannotReplaceLatestVolume()
    {
        var saves = new DelayedProjectFileService();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new StubWorkstationAudioService(),
            new ImmediateUiScheduler(), null, null, saves, new WeakReferenceMessenger());
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AddSampleTrackCommand.Execute(null);
        Assert.That(saves.FirstWriteStarted, Is.True);
        var track = vm.Tracks.Single();
        vm.SetTrackVolume(track, 80);
        vm.SetTrackVolume(track, 70);
        Assert.That(vm.HasPendingSaves, Is.True);
        saves.Release();
        await vm.FlushSavesAsync();
        Assert.That(ProjectDataSerializer.Deserialize(saves.LatestJson!).Tracks.Single().Volume, Is.EqualTo(70));
        Assert.That(saves.Writes, Is.EqualTo(3));
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task HeaderMessageAddsTrackOnlyWhileWorkstationActive()
    {
        var messenger = new WeakReferenceMessenger();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new StubWorkstationAudioService(),
            new ImmediateUiScheduler(), null, null, null, messenger);
        await vm.LoadAsync("project.json", CancellationToken.None);
        messenger.Send(new AddSampleTrackRequestedMessage());
        Assert.That(vm.Tracks, Has.Count.EqualTo(1));
        await vm.DeactivateAsync();
        messenger.Send(new AddSampleTrackRequestedMessage());
        Assert.That(vm.Tracks, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task FailedSaveIsReportedAndRetryPersistsLatestState()
    {
        var saves = new FailOnceProjectFileService();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new StubWorkstationAudioService(),
            new ImmediateUiScheduler(), null, null, saves, new WeakReferenceMessenger());
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AddSampleTrackCommand.Execute(null);
        Assert.CatchAsync<IOException>(() => vm.FlushSavesAsync());
        Assert.That(vm.SaveError, Does.Contain("Disk full"));
        vm.SetTrackVolume(vm.Tracks.Single(), 60);
        vm.RetrySaveCommand.Execute(null);
        await vm.FlushSavesAsync();
        Assert.That(vm.SaveError, Is.Null);
        Assert.That(ProjectDataSerializer.Deserialize(saves.SavedJson!).Tracks.Single().Volume, Is.EqualTo(60));
        await vm.DeactivateAsync();
    }
}
