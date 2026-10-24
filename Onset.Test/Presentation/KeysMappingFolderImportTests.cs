using CommunityToolkit.Mvvm.Messaging;
using Onset.Core.Entities;
using Onset.Resources.DataModels;
using Onset.Services;
using Onset.Services.FileSystem;
using Onset.ViewModels.Popups;
using Onset.ViewModels.Shell;

namespace Onset.Test.Presentation;

public sealed class KeysMappingFolderImportTests
{
    private string root = null!, source = null!, project = null!;
    [SetUp] public void Setup()
    {
        root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))).FullName;
        source = Directory.CreateDirectory(Path.Combine(root, "Source")).FullName;
        project = Path.Combine(Directory.CreateDirectory(Path.Combine(root, "Project")).FullName, "project.json");
        Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(project)!, "Samples"));
        File.WriteAllBytes(Path.Combine(source, "C3.wav"), [3]); File.WriteAllBytes(Path.Combine(source, "c#4.ogg"), [4]);
        File.WriteAllBytes(Path.Combine(source, "unknown.wav"), [5]);
    }
    [TearDown] public void Cleanup() => Directory.Delete(root, true);

    [TestCase("incoming")][TestCase("existing")][TestCase("cancel")]
    public async Task ConflictChoiceControlsCopiedFilesAndPreservesOtherKeys(string choice)
    {
        var track = new InstrumentalTrack { Name = "R&B", KeyFiles = new() { ["C3"] = "old.wav", ["B9"] = "unrelated.wav" } };
        var conflicts = new StubMappingConflictDialogService { Select = (rows, _) => Task.FromResult<IReadOnlyDictionary<string, string>?>(choice == "cancel" ? null : new Dictionary<string, string> { ["C3"] = choice == "existing" ? rows[0].ExistingPath! : rows[0].IncomingPaths.Single() }) };
        var files = new SampleFileService(); var picker = new StubSampleFileDialogService(source);
        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(project)!, "Samples", "old.wav"), [1]);
        await RunOwnerAsync(track, files, async vm =>
        {
            await vm.OpenFolderAsync(default);
            Assert.That(picker.Mode, Is.EqualTo(SampleSelectionMode.Folder));
            Assert.That(conflicts.Conflicts!.Single().Key, Is.EqualTo("C3"));
            Assert.That(track.KeyFiles["B9"], Is.EqualTo("unrelated.wav"));
            if (choice == "cancel") { Assert.That(track.FolderName, Is.Empty); Assert.That(track.KeyFiles["C3"], Is.EqualTo("old.wav")); Assert.That(Directory.Exists(Path.Combine(Path.GetDirectoryName(project)!, "Samples", "RB")), Is.False); }
            else
            {
                Assert.That(track.KeyFiles["C#4"], Is.EqualTo("RB/c#4.ogg"));
                Assert.That(track.KeyFiles["C3"], Is.EqualTo(choice == "existing" ? "old.wav" : "RB/C3.wav"));
                Assert.That(File.Exists(Path.Combine(Path.GetDirectoryName(project)!, "Samples", "RB", "C3.wav")), Is.EqualTo(choice != "existing"));
                Assert.That(vm.ImportStatus, Does.Contain("ignored"));
            }
        }, picker, conflicts);
    }

    [Test] public async Task DuplicateKeysReachChooserAndNoCandidatesAllocateNothing()
    {
        File.WriteAllBytes(Path.Combine(source, "C3.mp3"), [6]);
        var track = new InstrumentalTrack { Name = "Piano" }; var conflicts = new StubMappingConflictDialogService();
        await RunOwnerAsync(track, new SampleFileService(), async vm =>
        {
            await vm.OpenFolderAsync(default);
            Assert.That(conflicts.Conflicts!.Single().IncomingPaths, Has.Count.EqualTo(2));
            Assert.That(track.KeyFiles["C3"], Is.EqualTo("Piano/C3.mp3"));
        }, new(source), conflicts);
        foreach (var path in Directory.GetFiles(source)) File.Delete(path);
        var emptyTrack = new InstrumentalTrack { Name = "Empty" };
        await RunOwnerAsync(emptyTrack, new SampleFileService(), async vm =>
        {
            await vm.OpenFolderAsync(default); Assert.That(emptyTrack.FolderName, Is.Empty);
            Assert.That(vm.ImportStatus, Does.Contain("No matching"));
        }, new(source), new());
    }

    [Test] public async Task CopyFailureRetainsOldMappingButOtherKeysSucceed()
    {
        var track = new InstrumentalTrack { Name = "Piano", KeyFiles = new() { ["C3"] = "old.wav" } };
        var files = new StubSampleFileService(["old.wav"]) { Import = (_, path, _) => Path.GetFileName(path) == "C3.wav" ? Task.FromException<string>(new IOException("read failed")) : Task.FromResult("Piano/c#4.ogg") };
        await RunOwnerAsync(track, files, async vm =>
        {
            await vm.OpenFolderAsync(default);
            Assert.That(track.KeyFiles["C3"], Is.EqualTo("old.wav")); Assert.That(track.KeyFiles["C#4"], Is.EqualTo("Piano/c#4.ogg"));
            Assert.That(vm.ErrorMessage, Does.Contain("read failed"));
        }, new(source), new());
    }

    [Test] public async Task LateConflictCompletionAfterCloseCannotImport()
    {
        var track = new InstrumentalTrack { Name = "Piano", KeyFiles = new() { ["C3"] = "old.wav" } };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IReadOnlyDictionary<string, string>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var conflicts = new StubMappingConflictDialogService { Select = (_, _) => { entered.SetResult(); return release.Task; } };
        await RunOwnerAsync(track, new SampleFileService(), async vm =>
        {
            var importing = vm.OpenFolderAsync(default); await entered.Task;
            Assert.That(vm.CanClose, Is.False); Assert.That(vm.OpenFolderCommand.CanExecute(null), Is.False);
            var closing = vm.DeactivateAsync(); Assert.That(closing.IsCompleted, Is.False);
            release.SetResult(new Dictionary<string, string> { ["C3"] = Path.Combine(source, "C3.wav") });
            await Task.WhenAll(importing, closing);
            Assert.That(track.KeyFiles["C3"], Is.EqualTo("old.wav")); Assert.That(track.FolderName, Is.Empty);
        }, new(source), conflicts);
    }

    [Test] public async Task AcceptedSuccessfulKeysSurviveCancellationDuringLaterCopy()
    {
        var track = new InstrumentalTrack { Name = "Piano" };
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var files = new StubSampleFileService(["Piano/c#4.ogg"]) { Import = async (_, path, token) =>
        {
            if (Path.GetFileName(path) == "C3.wav") { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); }
            return "Piano/" + Path.GetFileName(path);
        } };
        await RunOwnerAsync(track, files, async vm =>
        {
            var importing = vm.OpenFolderAsync(cancellation.Token); await entered.Task;
            cancellation.Cancel(); await importing;
            Assert.That(track.KeyFiles["C#4"], Is.EqualTo("Piano/c#4.ogg"));
            Assert.That(track.KeyFiles.ContainsKey("C3"), Is.False);
            Assert.That(vm.ImportStatus, Does.Contain("interrupted")); Assert.That(vm.IsBusy, Is.False);
        }, new(source), new());
    }

    [Test] public async Task ClosingQueuedManualEditsFlushesDirtyDrafts()
    {
        var track = new InstrumentalTrack { Name = "Piano" };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var files = new StubSampleFileService(["new.wav"]) { Import = async (_, _, token) =>
        { if (Interlocked.Increment(ref calls) == 1) { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); } return "Piano/new.wav"; } };
        await RunOwnerAsync(track, files, async vm =>
        {
            vm.Rows.Single(r => r.Key == "C3").DraftFileName = "new.wav";
            vm.Rows.Single(r => r.Key == "D3").DraftFileName = "new.wav";
            var first = vm.CommitMappingAsync("C3", "new.wav"); await entered.Task;
            var second = vm.CommitMappingAsync("D3", "new.wav");
            var closing = vm.DeactivateAsync();
            await first;
            try { await second; } catch (OperationCanceledException) { }
            await closing;
            Assert.That(track.KeyFiles["C3"], Is.EqualTo("Piano/new.wav")); Assert.That(track.KeyFiles["D3"], Is.EqualTo("Piano/new.wav"));
        }, new(source), new());
    }

    [Test] public async Task MissingManualMappingStaysMissingWhenFolderRefreshFails()
    {
        var track = new InstrumentalTrack { Name = "Piano" };
        var files = new StubSampleFileService([]);
        await RunOwnerAsync(track, files, async vm =>
        {
            files.List = (_, _) => Task.FromException<IReadOnlyList<string>>(new IOException("scan failed"));
            Assert.That(await vm.CommitMappingAsync("C3", "missing.wav"), Is.True);
            Assert.That(track.KeyFiles["C3"], Is.EqualTo("Piano/missing.wav"));
            Assert.That(vm.Rows.Single(row => row.Key == "C3").IsMissing, Is.True);
        }, new(source), new());
    }

    private async Task RunOwnerAsync(InstrumentalTrack track, ISampleFileService files, Func<KeysMappingDrawerVM, Task> work, StubSampleFileDialogService picker, StubMappingConflictDialogService conflicts)
    {
        var messenger = new WeakReferenceMessenger(); var dialogs = new StubInstrumentalTrackDialogService();
        var owner = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", new ProjectData { Tracks = [track], Offsets = [0], BPMs = [120] })) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), sampleFiles: files, messenger: messenger, instrumentDialogs: dialogs);
        dialogs.Mapping = async (snapshot, _) =>
        {
            var vm = new KeysMappingDrawerVM(snapshot, messenger, new ImmediateUiScheduler(), files, picker, conflicts);
            try { await work(vm); } finally { await vm.DeactivateAsync(); }
        };
        try { await owner.LoadAsync(project, default); owner.AcknowledgePresentationReady(owner.LoadGeneration); await owner.KeysMappingCommand.ExecuteAsync(track); }
        finally { await owner.DeactivateAsync(); }
    }
}
