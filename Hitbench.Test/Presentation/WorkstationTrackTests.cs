using Hitbench.Test;
using Hitbench.Core.Entities;
using Hitbench.ViewModels.Shell;
using Hitbench.Services.FileSystem;
using Hitbench.Services.ProjectFiles;
using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.Messages;

namespace Hitbench.Test.Presentation;

public class WorkstationTrackTests
{
    [Test]
    public async Task CreationAfterRemovalDoesNotRecolorExistingTracks()
    {
        var saved = new Track { Name = "Saved", Color = "#123456" };
        var vm = await WorkstationSampleConfigurationTests.CreateAsync(saved);
        try
        {
            vm.AddSampleTrackCommand.Execute(null);
        { if (vm.SampleFiles.FirstOrDefault() is { } selectedFile) vm.SelectSample(vm.Tracks.Last(), selectedFile); }
            var added = vm.Tracks.Last();
            vm.AddPianoTrackCommand.Execute(null);
            await vm.RemoveTrackCommand.ExecuteAsync(added);
            vm.AddSampleTrackCommand.Execute(null);
        { if (vm.SampleFiles.FirstOrDefault() is { } selectedFile) vm.SelectSample(vm.Tracks.Last(), selectedFile); }
            Assert.That(vm.Tracks[0], Is.SameAs(saved));
            Assert.That(saved.Color, Is.EqualTo("#123456"));
            Assert.That(vm.Tracks.Skip(1).Select(track => track.Color), Is.All.Null);
        }
        finally { await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task NewTrackNamesShareSequenceAcrossSampleAndPianoTracks()
    {
        var vm = await WorkstationSampleConfigurationTests.CreateAsync();
        try
        {
            vm.AddSampleTrackCommand.Execute(null);
        { if (vm.SampleFiles.FirstOrDefault() is { } selectedFile) vm.SelectSample(vm.Tracks.Last(), selectedFile); }
            vm.AddPianoTrackCommand.Execute(null);
            vm.AddSampleTrackCommand.Execute(null);
        { if (vm.SampleFiles.FirstOrDefault() is { } selectedFile) vm.SelectSample(vm.Tracks.Last(), selectedFile); }
            Assert.That(vm.Tracks.Select(t => t.Color), Is.All.Null);
            Assert.That(vm.Tracks.Select(t => t.Name), Is.EqualTo(new[] { "New Track", "New Track (1)", "New Track (2)" }));
        }
        finally { await vm.DeactivateAsync(); }
    }

    [TestCase("New Track (7)", "New Track (8)")]
    [TestCase("new track (2147483647)", "New Track (2147483648)")]
    public async Task NewTrackNamesAdvancePastExistingSuffixes(string occupied, string expected)
    {
        var first = new Track { Name = "new track" };
        var other = new InstrumentalTrack { Name = occupied };
        var saved = new Track { Name = "" };
        var vm = await WorkstationSampleConfigurationTests.CreateAsync(first, other, saved);
        try
        {
            vm.AddSampleTrackCommand.Execute(null);
        { if (vm.SampleFiles.FirstOrDefault() is { } selectedFile) vm.SelectSample(vm.Tracks.Last(), selectedFile); }
            Assert.That(vm.Tracks.Last().Name, Is.EqualTo(expected));
            Assert.That(first.Name, Is.EqualTo("new track"));
            Assert.That(other.Name, Is.EqualTo(occupied));
            Assert.That(saved.Name, Is.Empty);
        }
        finally { await vm.DeactivateAsync(); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task TrackGainReachesTwoHundredAndPersists(bool instrumental)
    {
        Track track = instrumental ? new InstrumentalTrack() : new Track();
        var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav", new ProjectData { Tracks = [track] })) };
        var saves = new DelayedProjectFileService(); saves.Release();
        var vm = new WorkstationPageVM(source, new Hitbench.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), projectFiles: saves, saveDelay: (_, _) => Task.CompletedTask);
        await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;
        vm.SetTrackVolume(track, 201);
        Assert.That(track.Volume, Is.EqualTo(200));
        await vm.FlushSavesAsync();
        Assert.That(ProjectDataSerializer.Deserialize(saves.LatestJson!).Tracks.Single().Volume, Is.EqualTo(200));
        vm.SetTrackVolume(track, 0); Assert.That(track.Volume, Is.EqualTo(5));
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task MetadataEditsPersistOnlyReadyLiveTracks()
    {
        var track = new Track { SelectedSampleFileName = "kick.wav" };
        var other = new Track();
        var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(
            new WorkstationSource("map.osu", "audio.wav", new ProjectData { Tracks = [track] })) };
        using var audio = new StubWorkstationAudioService();
        var saves = new DelayedProjectFileService(); saves.Release();
        var vm = new WorkstationPageVM(source, new Hitbench.Services.MarkerPlaybackCoordinator(audio, Task.Delay),
            new ImmediateUiScheduler(), projectFiles: saves, saveDelay: (_, _) => Task.CompletedTask);
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.SetTrackAddition(track, "Drum");
        Assert.That(track.Addition, Is.EqualTo("None"));
        vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;
        var updates = audio.SampleUpdates.Count;
        vm.SetTrackAddition(other, "Drum");
        vm.SetTrackAddition(track, "unknown");
        vm.SetTrackAddition(track, "Soft"); vm.SetTrackAddition(track, "Soft");
        vm.SetTrackAddition(track, "Drum"); vm.SetTrackAddition(track, "Drum");
        await vm.FlushSavesAsync();
        Assert.That(saves.Snapshots.Select(json => ProjectDataSerializer.Deserialize(json).Tracks.Single().Addition),
            Is.EqualTo(new[] { "Soft", "Drum" }));
        Assert.That(other.Addition, Is.EqualTo("None"));
        Assert.That(track.SelectedSampleFileName, Is.EqualTo("kick.wav"));
        Assert.That(audio.SampleUpdates, Has.Count.EqualTo(updates));
        Assert.That(vm.CanUndo, Is.False);
        await vm.DeactivateAsync();
        vm.SetTrackAddition(track, "Normal");
        Assert.That(track.Addition, Is.EqualTo("Drum"));
    }

    [Test]
    public async Task RenamingPersistsOrderedSnapshotsWithoutChangingAudioOrNodeHistory()
    {
        var track = new Track { SelectedSampleFileName = "kick.wav" };
        var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(
            new WorkstationSource("map.osu", "audio.wav", new ProjectData { Tracks = [track] })) };
        var audio = new StubWorkstationAudioService();
        var saves = new DelayedProjectFileService();
        var vm = new WorkstationPageVM(source, new Hitbench.Services.MarkerPlaybackCoordinator(audio, Task.Delay),
            new ImmediateUiScheduler(), projectFiles: saves, saveDelay: (_, _) => Task.CompletedTask);
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;
        var audioUpdates = audio.SampleUpdates.Count;
        vm.SetTrackName(track, "Kick");
        vm.SetTrackName(track, "Kick");
        vm.SetTrackName(track, "Snare");
        vm.SetTrackName(track, "");
        Assert.That(audio.SampleUpdates, Has.Count.EqualTo(audioUpdates));
        Assert.That(vm.CanUndo, Is.False);
        vm.SelectSample(track, "snare.wav");
        vm.SetTrackVolume(track, 70);
        saves.Release();
        await vm.FlushSavesAsync();
        Assert.That(saves.Snapshots.Select(json => ProjectDataSerializer.Deserialize(json).Tracks.Single().Name),
            Is.EqualTo(new[] { "Kick", "" }));
        var restored = ProjectDataSerializer.Deserialize(saves.LatestJson!).Tracks.Single();
        Assert.That(restored.Name, Is.Empty);
        Assert.That(restored.SelectedSampleFileName, Is.EqualTo("snare.wav"));
        Assert.That(restored.Volume, Is.EqualTo(70));
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task RenameRequiresReadyMemberTrackAndStopsAfterDeactivation()
    {
        var track = new Track { Name = "Original" };
        var stranger = new Track { Name = "Other" };
        var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(
            new WorkstationSource("map.osu", "audio.wav", new ProjectData { Tracks = [track] })) };
        var saves = new DelayedProjectFileService();
        saves.Release();
        var vm = new WorkstationPageVM(source, new Hitbench.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay),
            new ImmediateUiScheduler(), projectFiles: saves, saveDelay: (_, _) => Task.CompletedTask);
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.SetTrackName(track, "Too early");
        Assert.That(track.Name, Is.EqualTo("Original"));
        vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;
        vm.SetTrackName(stranger, "Wrong track");
        Assert.That(stranger.Name, Is.EqualTo("Other"));
        vm.SetTrackName(track, "Ready");
        await vm.FlushSavesAsync();
        var writes = saves.Writes;
        await vm.DeactivateAsync();
        vm.SetTrackName(track, "Too late");
        Assert.That(track.Name, Is.EqualTo("Ready"));
        Assert.That(saves.Writes, Is.EqualTo(writes));
    }

    [Test]
    public async Task EmptyTrackRemovalSkipsDialogAndPersists()
    {
        var track = new Track { SelectedSampleFileName = "selected.wav" };
        var sibling = new Track();
        var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav", new ProjectData { Tracks = [track, sibling] })) };
        var saves = new DelayedProjectFileService();
        saves.Release();
        var dialog = new StubTrackRemovalDialogService();
        var vm = new WorkstationPageVM(source, new Hitbench.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), projectFiles: saves, trackRemovalDialog: dialog, saveDelay: (_, _) => Task.CompletedTask);
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;
        await vm.RemoveTrackCommand.ExecuteAsync(track);
        await vm.FlushSavesAsync();
        Assert.That(dialog.Calls, Is.Zero);
        Assert.That(vm.Tracks, Is.EqualTo(new[] { sibling }));
        Assert.That(ProjectDataSerializer.Deserialize(saves.LatestJson!).Tracks, Has.Count.EqualTo(1));
        await vm.DeactivateAsync();
    }
    [Test]
    public async Task ImportIsUnavailableWithoutRequiredServices()
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new Hitbench.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler(), saveDelay: (_, _) => Task.CompletedTask);

        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;

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
            var audio = new StubWorkstationAudioService();
            var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new Hitbench.Services.MarkerPlaybackCoordinator(audio, (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler(), new SampleFileService(),
                new StubSampleFileDialogService(sourcePath), saveDelay: (_, _) => Task.CompletedTask);

            await vm.LoadAsync(projectPath, CancellationToken.None);
            vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;
            Assert.That(vm.CanAddTrack, Is.True);
            Assert.That(vm.AddSampleTrackCommand.CanExecute(null), Is.True);

            Assert.That(await vm.ImportSamplesAsync(), Is.EqualTo(new[] { "kick.wav" }));
            await vm.FlushPlaybackUpdatesAsync();
            Assert.That(vm.SampleFiles, Is.EqualTo(new[] { "kick.wav" }));
            Assert.That(audio.SampleUpdates, Has.Count.EqualTo(2));
            Assert.That(vm.Tracks, Is.Empty);
            Assert.That(vm.CanAddTrack, Is.True);

            vm.AddSampleTrackCommand.Execute(null);
        { if (vm.SampleFiles.FirstOrDefault() is { } selectedFile) vm.SelectSample(vm.Tracks.Last(), selectedFile); }
            Assert.That(vm.Tracks.Single().SelectedSampleFileName, Is.EqualTo("kick.wav"));
            Assert.That(vm.Tracks.Single().Samples, Is.Empty);
            await vm.DeactivateAsync();
        }
        finally { Directory.Delete(root.FullName, true); }
    }

    [Test]
    public async Task SuccessfulPlaybackRefreshClearsItsResolvedError()
    {
        var track = new Track { Volume = 50, Samples = [new Sample { FileName = "missing.wav" }] };
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav",
                new ProjectData { Tracks = [track] }))
        };
        var audio = new StubWorkstationAudioService
        {
            Snapshot = new AudioPlaybackSnapshot(TimeSpan.Zero, false, "Unable to load sample 'missing.wav'.")
        };
        var vm = new WorkstationPageVM(source, new Hitbench.Services.MarkerPlaybackCoordinator(audio, (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler(), saveDelay: (_, _) => Task.CompletedTask);
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;
        Assert.That(vm.ErrorMessage, Does.Contain("missing.wav"));

        audio.Snapshot = audio.Snapshot with { Error = null };
        vm.SetTrackVolume(track, 60);
        await vm.FlushPlaybackUpdatesAsync();

        Assert.That(vm.ErrorMessage, Is.Null);
        await vm.DeactivateAsync();
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
        var vm = new WorkstationPageVM(source, new Hitbench.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)),
            new ImmediateUiScheduler(), null, null, saves, null, dialog, saveDelay: (_, _) => Task.CompletedTask);
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;
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
        var vm = new WorkstationPageVM(source, new Hitbench.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)),
            new ImmediateUiScheduler(), trackRemovalDialog: dialog, saveDelay: (_, _) => Task.CompletedTask);
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;
        await vm.RemoveTrackCommand.ExecuteAsync(track);
        Assert.That(vm.Tracks, Is.EqualTo(new[] { track }));
        Assert.That(dialog.Calls, Is.EqualTo(1));
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task DuplicateRequestWhileConfirmationPendingIsIgnored()
    {
        var track = new Track { Samples = [new Sample { FileName = "a.wav" }] };
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav",
                new ProjectData { Tracks = [track] }))
        };
        var response = new TaskCompletionSource<bool>();
        var dialog = new StubTrackRemovalDialogService { Confirm = _ => response.Task };
        var vm = new WorkstationPageVM(source, new Hitbench.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)),
            new ImmediateUiScheduler(), trackRemovalDialog: dialog, saveDelay: (_, _) => Task.CompletedTask);
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;
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
        var original = new Track { Samples = [new Sample { FileName = "a.wav" }] };
        var replacement = new Track();
        var source = new StubWorkstationSourceService
        {
            Load = (path, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav",
                new ProjectData { Tracks = [path == "old.json" ? original : replacement] }))
        };
        var response = new TaskCompletionSource<bool>();
        var dialog = new StubTrackRemovalDialogService { Confirm = _ => response.Task };
        var vm = new WorkstationPageVM(source, new Hitbench.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)),
            new ImmediateUiScheduler(), trackRemovalDialog: dialog, saveDelay: (_, _) => Task.CompletedTask);
        await vm.LoadAsync("old.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;
        var pending = vm.RemoveTrackCommand.ExecuteAsync(original);
        await vm.LoadAsync("new.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;
        response.SetResult(true);
        await pending;
        Assert.That(vm.Tracks, Is.EqualTo(new[] { replacement }));
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task RemovalDisablesWhenWorkstationDeactivates()
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new Hitbench.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler(),
            trackRemovalDialog: new StubTrackRemovalDialogService(), saveDelay: (_, _) => Task.CompletedTask);
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;
        Assert.That(vm.CanRemoveTrack, Is.True);
        await vm.DeactivateAsync();
        Assert.That(vm.CanRemoveTrack, Is.False);
    }

    [Test]
    public async Task ConfirmationPendingBlocksImportAndDeactivationPreventsRemoval()
    {
        var track = new Track { Samples = [new Sample { FileName = "a.wav" }] };
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav",
                new ProjectData { Tracks = [track] }))
        };
        var response = new TaskCompletionSource<bool>();
        var dialog = new StubTrackRemovalDialogService { Confirm = _ => response.Task };
        var vm = new WorkstationPageVM(source, new Hitbench.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)),
            new ImmediateUiScheduler(), trackRemovalDialog: dialog, saveDelay: (_, _) => Task.CompletedTask);
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;
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
        var vm = new WorkstationPageVM(source, new Hitbench.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)),
            new ImmediateUiScheduler(), null, null, saves, null, dialog, saveDelay: (_, _) => Task.CompletedTask);
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;
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
        var track = new Track { Samples = [new Sample { FileName = "a.wav" }] };
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav",
                new ProjectData { Tracks = [track] }))
        };
        var dialog = new StubTrackRemovalDialogService
        {
            Confirm = _ => Task.FromException<bool>(new IOException("Dialog unavailable"))
        };
        var vm = new WorkstationPageVM(source, new Hitbench.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)),
            new ImmediateUiScheduler(), trackRemovalDialog: dialog, saveDelay: (_, _) => Task.CompletedTask);
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;
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
            var vm = new WorkstationPageVM(source, new Hitbench.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)),
                new ImmediateUiScheduler(), new SampleFileService(), null,
                new ProjectFileService(root.FullName), null, dialog, saveDelay: (_, _) => Task.CompletedTask);
            await vm.LoadAsync(path, CancellationToken.None);
            vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;
            await vm.RemoveTrackCommand.ExecuteAsync(track);
            await vm.FlushSavesAsync();
            Assert.That(await File.ReadAllBytesAsync(samplePath), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(vm.SampleFiles, Does.Contain("a.wav"));
            await vm.DeactivateAsync();
        }
        finally { Directory.Delete(root.FullName, true); }
    }
    [Test]
    public async Task SelectionUpdatesPlacedSamplesAndPlaybackWithoutMovingNodes()
    {
        var one = new Sample { FileName = "old.wav", Timestamp = 125 };
        var two = new Sample { FileName = "old.wav", Timestamp = 375 };
        var track = new Track { SelectedSampleFileName = "old.wav", Samples = [one, two] };
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav", new ProjectData { Tracks = [track] }))
        };
        var audio = new StubWorkstationAudioService();
        var saves = new DelayedProjectFileService();
        saves.Release();
        var vm = new WorkstationPageVM(source, new Hitbench.Services.MarkerPlaybackCoordinator(audio, (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler(), projectFiles: saves, saveDelay: (_, _) => Task.CompletedTask);
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;
        vm.SelectSample(track, "new.wav");
        Assert.That(track.Samples, Is.EqualTo(new[] { one, two }));
        Assert.That(track.SelectedSampleFileName, Is.EqualTo("new.wav"));
        Assert.That(track.Samples.Select(sample => sample.FileName), Is.All.EqualTo("new.wav"));
        Assert.That(track.Samples.Select(sample => sample.Timestamp), Is.EqualTo(new[] { 125, 375 }));
        await vm.FlushPlaybackUpdatesAsync();
        await vm.FlushSavesAsync();
        Assert.That(audio.SampleUpdates.Last().Select(item => item.FileName), Is.All.EqualTo("new.wav"));
        Assert.That(ProjectDataSerializer.Deserialize(saves.LatestJson!).Tracks.Single().Samples.Select(item => item.FileName),
            Is.All.EqualTo("new.wav"));
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task RapidSelectionUpdatesOnlyChosenTrackAndPreservesEventIdentity()
    {
        var first = new Track { SelectedSampleFileName = "a.wav", Samples = [new Sample { FileName = "a.wav", Timestamp = 125 }] };
        var second = new Track { Samples = [new Sample { FileName = "a.wav", Timestamp = 250 }] };
        var empty = new Track { SelectedSampleFileName = "a.wav" };
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", new ProjectData { Tracks = [first, second, empty] }))
        };
        var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(source, new Hitbench.Services.MarkerPlaybackCoordinator(audio, (duration, token) => Task.Delay(duration, token)),
            new ImmediateUiScheduler(), new StubSampleFileService(["a.wav", "B.wav", "c.wav"]), saveDelay: (_, _) => Task.CompletedTask);
        await vm.LoadAsync("project.json", default);
        vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;
        var originalId = audio.SampleUpdates.Last()[0].Id;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        audio.PrepareSamplesAsync = _ => release.Task;
        vm.SelectSample(first, "b.wav");
        vm.SelectSample(first, "c.wav");
        vm.SelectSample(empty, "b.wav");
        vm.SelectSample(first, "invalid.wav");
        release.SetResult();
        await vm.FlushPlaybackUpdatesAsync();
        Assert.That(first.Samples.Single().FileName, Is.EqualTo("c.wav"));
        Assert.That(second.Samples.Single().FileName, Is.EqualTo("a.wav"));
        Assert.That(empty.SelectedSampleFileName, Is.EqualTo("B.wav"));
        Assert.That(empty.Samples, Is.Empty);
        Assert.That(audio.SampleUpdates.Last()[0].Id, Is.EqualTo(originalId));
        Assert.That(audio.SampleUpdates.Last()[0].FileName, Is.EqualTo("c.wav"));
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task PlacementUsesSelectedFileAndRoundedTimestamp()
    {
        var project = new ProjectData { UninheritedPoints = TimingTestData.Points([0], [60]) };
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav", project))
        };
        var vm = new WorkstationPageVM(source, new Hitbench.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)),
            new ImmediateUiScheduler(), new StubSampleFileService(["a.wav", "b.wav"]), saveDelay: (_, _) => Task.CompletedTask);
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;

        vm.AddSampleTrackCommand.Execute(null);
        { if (vm.SampleFiles.FirstOrDefault() is { } selectedFile) vm.SelectSample(vm.Tracks.Last(), selectedFile); }
        var track = vm.Tracks.Single();
        var revisionBefore = vm.SamplesRevision;
        Assert.That(track.Samples, Is.Empty);
        Assert.That(track.SelectedSampleFileName, Is.EqualTo("a.wav"));

        vm.SetTrackVolume(track, 70);
        Assert.That(vm.PlaceSample(track, 125.5), Is.True);
        vm.SelectSample(track, "b.wav");
        vm.SetTrackVolume(track, 40);
        Assert.That(vm.PlaceSample(track, 125.5), Is.True);

        Assert.That(track.Samples.Select(sample => sample.FileName), Is.EqualTo(new[] { "b.wav" }));
        Assert.That(track.Samples.Select(sample => sample.Timestamp), Is.EqualTo(new[] { 126 }));
        Assert.That(vm.SamplesRevision, Is.EqualTo(revisionBefore + 2));
    }

    [Test]
    public async Task PlaybackScheduleUsesCurrentTrackVolume()
    {
        var sample = new Sample { FileName = "hit.wav", Timestamp = 250 };
        var track = new Track { Volume = 50, Samples = [sample] };
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav",
                new ProjectData { Tracks = [track] }))
        };
        var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(source, new Hitbench.Services.MarkerPlaybackCoordinator(audio, (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler(), saveDelay: (_, _) => Task.CompletedTask);

        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;

        var scheduled = audio.SampleUpdates.Last().Single();
        Assert.Multiple(() =>
        {
            Assert.That(scheduled.FileName, Is.EqualTo("hit.wav"));
            Assert.That(scheduled.Timestamp, Is.EqualTo(250));
            Assert.That(scheduled.TrackVolume, Is.EqualTo(50));
        });
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task SongVolumeIsSessionOnlyAndClamped()
    {
        var audio = new StubWorkstationAudioService();
        var saves = new DelayedProjectFileService();
        saves.Release();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new Hitbench.Services.MarkerPlaybackCoordinator(audio, (duration, token) => Task.Delay(duration, token)),
            new ImmediateUiScheduler(), projectFiles: saves, saveDelay: (_, _) => Task.CompletedTask);
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;

        vm.SongVolume = -10;
        Assert.That(vm.SongVolume, Is.Zero);
        vm.SongVolume = 120;

        Assert.Multiple(() =>
        {
            Assert.That(vm.SongVolume, Is.EqualTo(100));
            Assert.That(audio.SongVolume, Is.EqualTo(100));
            Assert.That(saves.Writes, Is.Zero);
        });
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task RemovalDeletesExactSampleAndUpdatesRevision()
    {
        var project = new ProjectData { UninheritedPoints = TimingTestData.Points([0], [60]) };
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav", project))
        };
        var vm = new WorkstationPageVM(source, new Hitbench.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)),
            new ImmediateUiScheduler(), new StubSampleFileService(["a.wav"]), saveDelay: (_, _) => Task.CompletedTask);
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;
        vm.AddSampleTrackCommand.Execute(null);
        { if (vm.SampleFiles.FirstOrDefault() is { } selectedFile) vm.SelectSample(vm.Tracks.Last(), selectedFile); }
        var track = vm.Tracks.Single();
        var revisionBefore = vm.SamplesRevision;
        Assert.That(vm.PlaceSample(track, 125), Is.True);
        var sample = track.Samples.Single();

        Assert.That(vm.RemoveSample(track, sample), Is.True);

        Assert.That(track.Samples, Is.Empty);
        Assert.That(vm.SamplesRevision, Is.EqualTo(revisionBefore + 2));
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
        var vm = new WorkstationPageVM(source, new Hitbench.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)),
            new ImmediateUiScheduler(), new StubSampleFileService(["a.wav"]), saveDelay: (_, _) => Task.CompletedTask);
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;
        vm.AddSampleTrackCommand.Execute(null);
        { if (vm.SampleFiles.FirstOrDefault() is { } selectedFile) vm.SelectSample(vm.Tracks.Last(), selectedFile); }

        var revisionBefore = vm.SamplesRevision;
        Assert.That(vm.PlaceSample(vm.Tracks.Single(), timestamp), Is.False);
        Assert.That(vm.Tracks.Single().Samples, Is.Empty);
        Assert.That(vm.SamplesRevision, Is.EqualTo(revisionBefore));
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
            var vm = new WorkstationPageVM(source, new Hitbench.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler(),
                new SampleFileService(), new StubSampleFileDialogService(sourcePath),
                new ProjectFileService(root.FullName), new WeakReferenceMessenger(), saveDelay: (_, _) => Task.CompletedTask);
            await vm.LoadAsync(projectPath, CancellationToken.None);
            vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;
            vm.AddSampleTrackCommand.Execute(null);
        { if (vm.SampleFiles.FirstOrDefault() is { } selectedFile) vm.SelectSample(vm.Tracks.Last(), selectedFile); }
            var track = vm.Tracks.Single();
            Assert.That(track.SelectedSampleFileName, Is.EqualTo("a.wav"));
            Assert.That(track.Addition, Is.EqualTo("None"));
            Assert.That(track.Samples, Is.Empty);
            Assert.That(await vm.ImportSamplesAsync(), Is.EqualTo(new[] { "b.mp3" }));
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
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new Hitbench.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)),
            new ImmediateUiScheduler(), new StubSampleFileService(["a.wav"]), null, saves,
            new WeakReferenceMessenger(), saveDelay: (_, _) => Task.CompletedTask);
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;
        vm.AddSampleTrackCommand.Execute(null);
        { if (vm.SampleFiles.FirstOrDefault() is { } selectedFile) vm.SelectSample(vm.Tracks.Last(), selectedFile); }
        Assert.That(saves.FirstWriteStarted, Is.True);
        var track = vm.Tracks.Single();
        vm.SetTrackVolume(track, 80);
        vm.SetTrackVolume(track, 70);
        Assert.That(vm.HasPendingSaves, Is.True);
        saves.Release();
        await vm.FlushSavesAsync();
        Assert.That(ProjectDataSerializer.Deserialize(saves.LatestJson!).Tracks.Single().Volume, Is.EqualTo(70));
        Assert.That(saves.Writes, Is.EqualTo(2));
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task HeaderMessageAddsTrackOnlyWhileWorkstationActive()
    {
        var messenger = new WeakReferenceMessenger();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new Hitbench.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)),
            new ImmediateUiScheduler(), new StubSampleFileService(["a.wav"]), null, null, messenger, saveDelay: (_, _) => Task.CompletedTask);
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;
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
        var vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", new ProjectData { Tracks = [new Track { SelectedSampleFileName = "a.wav" }] })) }, new Hitbench.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)),
            new ImmediateUiScheduler(), new StubSampleFileService(["a.wav"]), null, saves,
            new WeakReferenceMessenger(), saveDelay: (_, _) => Task.CompletedTask);
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;
        vm.SetTrackVolume(vm.Tracks.Single(), 80);
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
