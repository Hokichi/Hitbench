using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Core.Entities;
using osuHitsounder.Services;
using osuHitsounder.Services.FileSystem;
using osuHitsounder.Services.ProjectFiles;
using osuHitsounder.ViewModels.Popups;
using osuHitsounder.ViewModels.Shell;

namespace osuHitsounder.Test.Presentation;

public sealed class WorkstationInstrumentalFolderTests
{
    [Test] public void FolderAndNestedMappingsRoundTripWithoutMigratingLegacy()
    {
        var project = new ProjectData { Tracks = [new InstrumentalTrack { FolderName = "RB", KeyFiles = new() { ["C3"] = "RB/C3.wav" } }] };
        var loaded = (InstrumentalTrack)ProjectDataSerializer.Deserialize(ProjectDataSerializer.Serialize(project)).Tracks.Single();
        Assert.That(loaded.FolderName, Is.EqualTo("RB")); Assert.That(loaded.KeyFiles["C3"], Is.EqualTo("RB/C3.wav"));
        var legacy = (InstrumentalTrack)ProjectDataSerializer.Deserialize("{\"Tracks\":[{\"Kind\":\"Instrumental\",\"KeyFiles\":{\"C3\":\"old.wav\"}}]}").Tracks.Single();
        Assert.That(legacy.FolderName, Is.Empty); Assert.That(legacy.KeyFiles["C3"], Is.EqualTo("old.wav"));
    }

    [TestCase("../outside")][TestCase("CON")][TestCase("Piano/Soft")]
    public void UnsafePersistedFolderIsRejected(string folder) => Assert.Throws<InvalidDataException>(() =>
        ProjectDataSerializer.Deserialize("{\"Tracks\":[{\"Kind\":\"Instrumental\",\"FolderName\":\"" + folder + "\"}]}"));

    [Test] public async Task FirstMappingCopiesRootSampleAndRenameRewritesOnlyOwnedReferences()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))).FullName;
        var track = new InstrumentalTrack { Name = "R&B" };
        var other = new Track { SelectedSampleFileName = "C3.wav" };
        var messenger = new WeakReferenceMessenger(); var dialogs = new StubInstrumentalTrackDialogService();
        var files = new SampleFileService();
        Directory.CreateDirectory(Path.Combine(root, "Samples")); File.WriteAllBytes(Path.Combine(root, "Samples", "C3.wav"), [3]);
        var owner = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", new ProjectData { Tracks = [track, other], Offsets = [0], BPMs = [120] })) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), sampleFiles: files, messenger: messenger, instrumentDialogs: dialogs);
        dialogs.Mapping = async (snapshot, _) =>
        {
            var vm = new KeysMappingDrawerVM(snapshot, messenger, new ImmediateUiScheduler(), files, new StubSampleFileDialogService((string?)null));
            try { Assert.That(await vm.CommitMappingAsync("C3", "C3.wav"), Is.True); }
            finally { await vm.DeactivateAsync(); }
        };
        try
        {
            await owner.LoadAsync(Path.Combine(root, "project.json"), default); owner.AcknowledgePresentationReady(owner.LoadGeneration);
            await owner.KeysMappingCommand.ExecuteAsync(track);
            Assert.That(track.FolderName, Is.EqualTo("RB")); Assert.That(track.KeyFiles["C3"], Is.EqualTo("RB/C3.wav"));
            Assert.That(other.SelectedSampleFileName, Is.EqualTo("C3.wav"));
            other.Samples.Add(new Sample { FileName = "RB/C3.wav" });
            owner.SetTrackName(track, "Piano"); owner.SetTrackName(track, "Soft Piano");
            await owner.FlushSavesAsync();
            Assert.That(track.Name, Is.EqualTo("Soft Piano")); Assert.That(track.FolderName, Is.EqualTo("Soft Piano"));
            Assert.That(track.KeyFiles["C3"], Is.EqualTo("Soft Piano/C3.wav"));
            Assert.That(other.Samples.Single().FileName, Is.EqualTo("Soft Piano/C3.wav"));
            Assert.That(owner.SampleFiles, Does.Contain("Soft Piano/C3.wav"));
            Assert.That(File.ReadAllBytes(Path.Combine(root, "Samples", "Soft Piano", "C3.wav")), Is.EqualTo(new byte[] { 3 }));
        }
        finally { await owner.DeactivateAsync(); Directory.Delete(root, true); }
    }

    [Test] public async Task FailedRenamePreservesAcceptedNameAndMapping()
    {
        var track = new InstrumentalTrack { Name = "Piano", FolderName = "Piano", KeyFiles = new() { ["C3"] = "Piano/C3.wav" } };
        var files = new StubSampleFileService(["Piano/C3.wav"]) { FolderRename = (_, _, _, _, _) => Task.FromException<string>(new IOException("move failed")) };
        var owner = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", new ProjectData { Tracks = [track], Offsets = [0], BPMs = [120] })) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), sampleFiles: files);
        try
        {
            await owner.LoadAsync("project.json", default); owner.AcknowledgePresentationReady(owner.LoadGeneration);
            owner.SetTrackName(track, "Soft"); await owner.FlushSavesAsync();
            Assert.That(track.Name, Is.EqualTo("Piano")); Assert.That(track.KeyFiles["C3"], Is.EqualTo("Piano/C3.wav"));
            Assert.That(owner.ErrorMessage, Does.Contain("move failed"));
        }
        finally { await owner.DeactivateAsync(); }
    }

    [Test] public async Task ReturningToAcceptedNameCancelsPendingRename()
    {
        var track = new InstrumentalTrack { Name = "Piano", FolderName = "Piano", KeyFiles = new() { ["C3"] = "Piano/C3.wav" } };
        var owner = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", new ProjectData { Tracks = [track], Offsets = [0], BPMs = [120] })) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), sampleFiles: new StubSampleFileService(["Piano/C3.wav"]));
        try
        {
            await owner.LoadAsync("project.json", default); owner.AcknowledgePresentationReady(owner.LoadGeneration);
            owner.SetTrackName(track, "Soft"); owner.SetTrackName(track, "Piano"); await owner.FlushSavesAsync();
            Assert.That(track.Name, Is.EqualTo("Piano")); Assert.That(track.KeyFiles["C3"], Is.EqualTo("Piano/C3.wav"));
        }
        finally { await owner.DeactivateAsync(); }
    }

    [Test] public async Task PendingFolderRenameGatesSampleOperationsUntilSettled()
    {
        var track = new InstrumentalTrack { Name = "Piano", FolderName = "Piano" };
        var owner = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", new ProjectData { Tracks = [track], Offsets = [0], BPMs = [120] })) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), sampleFiles: new StubSampleFileService([]), sampleDialog: new StubSampleFileDialogService((string?)null));
        try
        {
            await owner.LoadAsync("project.json", default); owner.AcknowledgePresentationReady(owner.LoadGeneration);
            Assert.That(owner.CanImportSample, Is.True);
            owner.SetTrackName(track, "Soft");
            Assert.That(owner.HasPendingSaves, Is.True); Assert.That(owner.CanImportSample, Is.False); Assert.That(owner.CanPlaceSamples, Is.False);
            await owner.FlushSavesAsync();
            Assert.That(owner.CanImportSample, Is.True); Assert.That(owner.CanPlaceSamples, Is.True);
        }
        finally { await owner.DeactivateAsync(); }
    }
}
