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
    public async Task ImportIsUnavailableWithoutRequiredServices()
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(),
            new StubWorkstationAudioService(), new ImmediateUiScheduler());

        await vm.LoadAsync("project.json", CancellationToken.None);

        Assert.That(vm.CanImportSample, Is.False);
        Assert.That(vm.LoadSampleCommand.CanExecute(null), Is.False);
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task EmptyProjectCanImportFirstSampleBeforeAddingTrack()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        try
        {
            var projectPath = Path.Combine(root.FullName, "project.json");
            await File.WriteAllTextAsync(projectPath, "{}");
            var sourcePath = Path.Combine(root.FullName, "kick.wav");
            await File.WriteAllBytesAsync(sourcePath, [1, 2, 3]);
            var vm = new WorkstationPageVM(new StubWorkstationSourceService(),
                new StubWorkstationAudioService(), new ImmediateUiScheduler(), new SampleFileService(),
                new StubSampleFileDialogService(sourcePath));

            await vm.LoadAsync(projectPath, CancellationToken.None);
            Assert.That(vm.CanAddTrack, Is.False);
            Assert.That(vm.AddSampleTrackCommand.CanExecute(null), Is.False);

            Assert.That(await vm.ImportSampleAsync(), Is.EqualTo("kick.wav"));
            Assert.That(vm.SampleFiles, Is.EqualTo(new[] { "kick.wav" }));
            Assert.That(vm.Tracks, Is.Empty);
            Assert.That(vm.CanAddTrack, Is.True);

            vm.AddSampleTrackCommand.Execute(null);
            Assert.That(vm.Tracks.Single().SelectedSampleFileName, Is.EqualTo("kick.wav"));
            Assert.That(vm.Tracks.Single().Samples, Is.Empty);
            await vm.DeactivateAsync();
        }
        finally { Directory.Delete(root.FullName, true); }
    }

    [Test]
    public async Task ConfirmedRemovalDeletesOnlyChosenTrackAndPersistsSnapshot()
    {
        var first = new Track { Samples = [new Sample { FileName = "a.wav", Timestamp = 125 }] };
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
    public async Task ConfirmationPendingBlocksImportAndDeactivationPreventsRemoval()
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
        var removal = vm.RemoveTrackCommand.ExecuteAsync(track);
        Assert.That(vm.CanImportSample, Is.False);
        var deactivate = vm.DeactivateAsync();
        response.SetResult(true);
        await Task.WhenAll(removal, deactivate);
        Assert.That(vm.Project!.Tracks, Is.EqualTo(new[] { track }));
    }

    [Test]
    public async Task RemovalSaveFailureCanBeRetriedWithDeletedTrackSnapshot()
    {
        var track = new Track();
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav",
                new ProjectData { Tracks = [track] }))
        };
        var saves = new FailOnceProjectFileService();
        var dialog = new StubTrackRemovalDialogService { Confirm = _ => Task.FromResult(true) };
        var vm = new WorkstationPageVM(source, new StubWorkstationAudioService(),
            new ImmediateUiScheduler(), null, null, saves, null, dialog);
        await vm.LoadAsync("project.json", CancellationToken.None);
        await vm.RemoveTrackCommand.ExecuteAsync(track);
        Assert.CatchAsync<IOException>(() => vm.FlushSavesAsync());
        Assert.That(vm.SaveError, Does.Contain("Disk full"));
        vm.RetrySaveCommand.Execute(null);
        await vm.FlushSavesAsync();
        Assert.That(ProjectDataSerializer.Deserialize(saves.SavedJson!).Tracks, Is.Empty);
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task DialogFailurePreservesTrackAndRestoresRemovalAvailability()
    {
        var track = new Track();
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav",
                new ProjectData { Tracks = [track] }))
        };
        var dialog = new StubTrackRemovalDialogService
        {
            Confirm = _ => Task.FromException<bool>(new IOException("Dialog unavailable"))
        };
        var vm = new WorkstationPageVM(source, new StubWorkstationAudioService(),
            new ImmediateUiScheduler(), trackRemovalDialog: dialog);
        await vm.LoadAsync("project.json", CancellationToken.None);
        await vm.RemoveTrackCommand.ExecuteAsync(track);
        Assert.That(vm.Tracks, Is.EqualTo(new[] { track }));
        Assert.That(vm.CanRemoveTrack, Is.True);
        Assert.That(vm.ErrorMessage, Does.Contain("Dialog unavailable"));
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task RemovingTrackKeepsSampleFileAvailable()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        try
        {
            var path = Path.Combine(root.FullName, "project.json");
            await File.WriteAllTextAsync(path, "{}");
            var samples = Directory.CreateDirectory(Path.Combine(root.FullName, "Samples"));
            var samplePath = Path.Combine(samples.FullName, "a.wav");
            await File.WriteAllBytesAsync(samplePath, [1, 2, 3]);
            var track = new Track { Samples = [new Sample { FileName = "a.wav" }] };
            var source = new StubWorkstationSourceService
            {
                Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav",
                    new ProjectData { Tracks = [track] }))
            };
            var dialog = new StubTrackRemovalDialogService { Confirm = _ => Task.FromResult(true) };
            var vm = new WorkstationPageVM(source, new StubWorkstationAudioService(),
                new ImmediateUiScheduler(), new SampleFileService(), null,
                new ProjectFileService(root.FullName), null, dialog);
            await vm.LoadAsync(path, CancellationToken.None);
            await vm.RemoveTrackCommand.ExecuteAsync(track);
            await vm.FlushSavesAsync();
            Assert.That(await File.ReadAllBytesAsync(samplePath), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(vm.SampleFiles, Does.Contain("a.wav"));
            await vm.DeactivateAsync();
        }
        finally { Directory.Delete(root.FullName, true); }
    }
    [Test]
    public async Task SelectionChangesFutureSampleWithoutMutatingPlacedSamples()
    {
        var one = new Sample { FileName = "old.wav", Volume = 12, Timestamp = 125 };
        var two = new Sample { FileName = "old.wav", Volume = 25, Timestamp = 375 };
        var track = new Track { SelectedSampleFileName = "old.wav", Samples = [one, two] };
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav", new ProjectData { Tracks = [track] }))
        };
        var vm = new WorkstationPageVM(source, new StubWorkstationAudioService(), new ImmediateUiScheduler());
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.SelectSample(track, "new.wav");
        Assert.That(track.Samples, Is.EqualTo(new[] { one, two }));
        Assert.That(track.SelectedSampleFileName, Is.EqualTo("new.wav"));
        Assert.That(track.Samples.Select(sample => sample.FileName), Is.All.EqualTo("old.wav"));
        Assert.That(track.Samples.Select(sample => sample.Volume), Is.EqualTo(new[] { 12, 25 }));
        Assert.That(track.Samples.Select(sample => sample.Timestamp), Is.EqualTo(new[] { 125, 375 }));
    }

    [Test]
    public async Task PlacementCapturesCurrentSelectionVolumeAndRoundedTimestamp()
    {
        var project = new ProjectData { Offsets = [0], BPMs = [60] };
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav", project))
        };
        var vm = new WorkstationPageVM(source, new StubWorkstationAudioService(),
            new ImmediateUiScheduler(), new StubSampleFileService(["a.wav", "b.wav"]));
        await vm.LoadAsync("project.json", CancellationToken.None);

        vm.AddSampleTrackCommand.Execute(null);
        var track = vm.Tracks.Single();
        Assert.That(track.Samples, Is.Empty);
        Assert.That(track.SelectedSampleFileName, Is.EqualTo("a.wav"));

        vm.SetTrackVolume(track, 70);
        Assert.That(vm.PlaceSample(track, 125.5), Is.True);
        vm.SelectSample(track, "b.wav");
        vm.SetTrackVolume(track, 40);
        Assert.That(vm.PlaceSample(track, 125.5), Is.True);

        Assert.That(track.Samples.Select(sample => sample.FileName), Is.EqualTo(new[] { "a.wav", "b.wav" }));
        Assert.That(track.Samples.Select(sample => sample.Volume), Is.EqualTo(new[] { 70, 40 }));
        Assert.That(track.Samples.Select(sample => sample.Timestamp), Is.EqualTo(new[] { 126, 126 }));
        Assert.That(vm.SamplesRevision, Is.EqualTo(2));
    }

    [Test]
    public async Task RemovalDeletesExactSampleAndUpdatesRevision()
    {
        var project = new ProjectData { Offsets = [0], BPMs = [60] };
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav", project))
        };
        var vm = new WorkstationPageVM(source, new StubWorkstationAudioService(),
            new ImmediateUiScheduler(), new StubSampleFileService(["a.wav"]));
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AddSampleTrackCommand.Execute(null);
        var track = vm.Tracks.Single();
        Assert.That(vm.PlaceSample(track, 125), Is.True);
        var sample = track.Samples.Single();

        Assert.That(vm.RemoveSample(track, sample), Is.True);

        Assert.That(track.Samples, Is.Empty);
        Assert.That(vm.SamplesRevision, Is.EqualTo(2));
        Assert.That(vm.RemoveSample(track, sample), Is.False);
    }

    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(-1d)]
    [TestCase(10001d)]
    public async Task InvalidPlacementDoesNotMutateTrack(double timestamp)
    {
        var project = new ProjectData();
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav", project))
        };
        var vm = new WorkstationPageVM(source, new StubWorkstationAudioService(),
            new ImmediateUiScheduler(), new StubSampleFileService(["a.wav"]));
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AddSampleTrackCommand.Execute(null);

        Assert.That(vm.PlaceSample(vm.Tracks.Single(), timestamp), Is.False);
        Assert.That(vm.Tracks.Single().Samples, Is.Empty);
        Assert.That(vm.SamplesRevision, Is.Zero);
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
            Assert.That(track.SelectedSampleFileName, Is.EqualTo("a.wav"));
            Assert.That(track.Samples, Is.Empty);
            Assert.That(await vm.ImportSampleAsync(), Is.EqualTo("b.mp3"));
            vm.SelectSample(track, "b.mp3");
            vm.SetTrackVolume(track, 70);
            Assert.That(vm.PlaceSample(track, 0), Is.True);
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
            new ImmediateUiScheduler(), new StubSampleFileService(["a.wav"]), null, saves,
            new WeakReferenceMessenger());
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
            new ImmediateUiScheduler(), new StubSampleFileService(["a.wav"]), null, null, messenger);
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
            new ImmediateUiScheduler(), new StubSampleFileService(["a.wav"]), null, saves,
            new WeakReferenceMessenger());
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
