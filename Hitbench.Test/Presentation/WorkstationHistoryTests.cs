using Hitbench.Core.Entities;
using Hitbench.Services;
using Hitbench.Services.ProjectFiles;
using Hitbench.ViewModels.Shell;

namespace Hitbench.Test.Presentation;

public class WorkstationHistoryTests
{
    private WorkstationPageVM vm = null!;
    private StubWorkstationSourceService source = null!;
    private StubWorkstationAudioService audio = null!;
    private StubSampleFileService files = null!;
    private StubTrackRemovalDialogService removal = null!;
    private DelayedProjectFileService saves = null!;
    private Track first = null!;
    private Track second = null!;

    [SetUp]
    public async Task Setup()
    {
        first = new Track { SelectedSampleFileName = "a.wav" };
        second = new Track { SelectedSampleFileName = "b.wav" };
        source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav",
                new ProjectData { Offsets = [0], BPMs = [120], ControlPoints = [new()], Tracks = [first, second] }))
        };
        audio = new StubWorkstationAudioService();
        files = new StubSampleFileService(["a.wav", "b.wav"]);
        removal = new StubTrackRemovalDialogService { Confirm = _ => Task.FromResult(true) };
        saves = new DelayedProjectFileService();
        saves.Release();
        vm = new WorkstationPageVM(source, new MarkerPlaybackCoordinator(audio, Task.Delay),
            new ImmediateUiScheduler(), files, new StubSampleFileDialogService("a.wav"), saves,
            trackRemovalDialog: removal, saveDelay: (_, _) => Task.CompletedTask);
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;
    }

    [TearDown]
    public async Task Cleanup()
    {
        saves.Release();
        audio.PrepareSamplesAsync = null;
        await vm.DeactivateAsync();
        audio.Dispose();
    }

    [Test]
    public void GlobalChronologyIgnoresTrackFocus()
    {
        Assert.That(vm.CanUndo, Is.False);
        Assert.That(vm.CanRedo, Is.False);
        vm.PlaceSample(first, 125);
        var a = first.Samples.Single();
        vm.PlaceSample(second, 250);
        var b = second.Samples.Single();
        vm.RemoveSample(first, a);
        vm.UndoCommand.Execute(null);
        Assert.That(first.Samples.Single(), Is.SameAs(a));
        vm.UndoCommand.Execute(null);
        Assert.That(second.Samples, Is.Empty);
        vm.UndoCommand.Execute(null);
        Assert.That(first.Samples, Is.Empty);
        Assert.That(vm.CanUndo, Is.False);
        vm.RedoCommand.Execute(null);
        Assert.That(first.Samples.Single(), Is.SameAs(a));
        vm.RedoCommand.Execute(null);
        Assert.That(second.Samples.Single(), Is.SameAs(b));
        vm.RedoCommand.Execute(null);
        Assert.That(first.Samples, Is.Empty);
        Assert.That(vm.CanRedo, Is.False);
        Assert.That(vm.SamplesRevision, Is.EqualTo(9));
    }

    [Test]
    public async Task UndoRedoUpdatesPlaybackAndSavedProject()
    {
        var node = new Sample { FileName = "a.wav", Timestamp = 125 };
        first.Samples.Add(node);
        first.Volume = 42;
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;
        Assert.That(vm.CanUndo, Is.False);
        Assert.That(vm.RemoveSample(first, node), Is.True);
        vm.UndoCommand.Execute(null);
        await vm.FlushPlaybackUpdatesAsync();
        await vm.FlushSavesAsync();
        Assert.That(first.Samples.Single(), Is.SameAs(node));
        var saved = ProjectDataSerializer.Deserialize(saves.LatestJson!).Tracks[0].Samples.Single();
        Assert.That(saved.Timestamp, Is.EqualTo(125));
        var scheduled = audio.SampleUpdates.Last().Single();
        Assert.That(scheduled.Timestamp, Is.EqualTo(125));
        Assert.That(scheduled.TrackVolume, Is.EqualTo(42));
        Assert.That(vm.SamplesRevision, Is.EqualTo(2));
        vm.RedoCommand.Execute(null);
        await vm.FlushPlaybackUpdatesAsync();
        await vm.FlushSavesAsync();
        Assert.That(ProjectDataSerializer.Deserialize(saves.LatestJson!).Tracks[0].Samples, Is.Empty);
        Assert.That(audio.SampleUpdates.Last(), Is.Empty);
        Assert.That(vm.SamplesRevision, Is.EqualTo(3));
    }

    [Test]
    public void RejectedEditsKeepRedo()
    {
        vm.PlaceSample(first, 125);
        var node = first.Samples.Single();
        vm.UndoCommand.Execute(null);
        Assert.That(vm.PlaceSample(first, double.NaN), Is.False);
        Assert.That(vm.PlaceSample(new Track(), 125), Is.False);
        Assert.That(vm.RemoveSample(first, node), Is.False);
        first.SelectedSampleFileName = "missing.wav";
        Assert.That(vm.PlaceSample(first, 125), Is.False);
        Assert.That(vm.RedoCommand.CanExecute(null), Is.True);
        Assert.That(vm.SamplesRevision, Is.EqualTo(2));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void NewSuccessfulEditClearsRedo(bool placement)
    {
        vm.PlaceSample(first, 125);
        vm.PlaceSample(second, 250);
        vm.UndoCommand.Execute(null);
        if (placement) vm.PlaceSample(first, 375);
        else vm.RemoveSample(first, first.Samples.Single());
        Assert.That(vm.RedoCommand.CanExecute(null), Is.False);
    }

    [Test]
    public async Task ExcludedEditsDoNotCreateHistoryOrClearRedo()
    {
        vm.SetTrackVolume(first, 70);
        vm.SelectSample(first, "b.wav");
        vm.AddSampleTrackCommand.Execute(null);
        Assert.That(vm.CanUndo, Is.False);
        vm.PlaceSample(first, 125);
        vm.UndoCommand.Execute(null);
        vm.SelectSample(first, "a.wav");
        vm.SetTrackVolume(first, 40);
        vm.ZoomInCommand.Execute(null);
        vm.IncreaseSubdivisionCommand.Execute(null);
        await vm.SeekCommand.ExecuteAsync(TimeSpan.FromMilliseconds(250));
        await vm.PlayPauseCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        Assert.That(await vm.ImportSamplesAsync(), Is.EqualTo(new[] { "a.wav" }));
        vm.AddSampleTrackCommand.Execute(null);
        Assert.That(vm.CanUndo, Is.False);
        Assert.That(vm.CanRedo, Is.True);
        vm.RedoCommand.Execute(null);
        Assert.That(first.Samples.Single().Timestamp, Is.EqualTo(125));
        Assert.That(first.Volume, Is.EqualTo(40));
    }

    [Test]
    public void RestorationUsesCurrentSampleSelection()
    {
        vm.PlaceSample(first, 125);
        var node = first.Samples.Single();
        vm.RemoveSample(first, node);
        vm.SelectSample(first, "b.wav");
        vm.UndoCommand.Execute(null);
        Assert.That(first.Samples.Single(), Is.SameAs(node));
        Assert.That(node.FileName, Is.EqualTo("b.wav"));
        Assert.That(node.Timestamp, Is.EqualTo(125));
    }

    [Test]
    public async Task TrackRemovalPrunesOnlyItsEntries()
    {
        vm.PlaceSample(first, 125);
        vm.PlaceSample(second, 250);
        vm.PlaceSample(first, 375);
        vm.PlaceSample(second, 500);
        vm.UndoCommand.Execute(null);
        vm.UndoCommand.Execute(null);
        await vm.RemoveTrackCommand.ExecuteAsync(first);
        Assert.That(vm.Tracks, Does.Not.Contain(first));
        vm.UndoCommand.Execute(null);
        Assert.That(second.Samples, Is.Empty);
        Assert.That(vm.CanUndo, Is.False);
        vm.RedoCommand.Execute(null);
        Assert.That(second.Samples.Single().Timestamp, Is.EqualTo(250));
        vm.RedoCommand.Execute(null);
        Assert.That(second.Samples.Select(n => n.Timestamp), Is.EqualTo(new[] { 250, 500 }));
        Assert.That(vm.CanRedo, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CancelledOrFailedTrackRemovalRetainsHistory(bool throws)
    {
        vm.PlaceSample(first, 125);
        removal.Confirm = _ => throws ? Task.FromException<bool>(new IOException("confirmation failed")) : Task.FromResult(false);
        await vm.RemoveTrackCommand.ExecuteAsync(first);
        Assert.That(vm.CanUndo, Is.True);
        vm.UndoCommand.Execute(null);
        Assert.That(first.Samples, Is.Empty);
        Assert.That(vm.Tracks, Does.Contain(first));
        if (throws) Assert.That(vm.ErrorMessage, Is.EqualTo("confirmation failed"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task BusyOperationsDisableAndNotifyHistory(bool import)
    {
        vm.PlaceSample(first, 125);
        vm.PlaceSample(second, 250);
        vm.UndoCommand.Execute(null);
        var undoChanges = 0;
        var redoChanges = 0;
        vm.UndoCommand.CanExecuteChanged += (_, _) => undoChanges++;
        vm.RedoCommand.CanExecuteChanged += (_, _) => redoChanges++;
        var imported = new TaskCompletionSource<string>();
        var confirmed = new TaskCompletionSource<bool>();
        files.Import = (_, _, _) => imported.Task;
        removal.Confirm = _ => confirmed.Task;
        Task pending = import ? vm.ImportSamplesAsync() : vm.RemoveTrackCommand.ExecuteAsync(first);
        Assert.That(vm.CanUndo, Is.False);
        Assert.That(vm.CanRedo, Is.False);
        vm.UndoCommand.Execute(null);
        vm.RedoCommand.Execute(null);
        Assert.That(first.Samples, Has.Count.EqualTo(1));
        Assert.That(second.Samples, Is.Empty);
        if (import) imported.SetResult("a.wav");
        else confirmed.SetResult(false);
        await pending;
        Assert.That(vm.CanUndo, Is.True);
        Assert.That(vm.CanRedo, Is.True);
        Assert.That(undoChanges, Is.GreaterThanOrEqualTo(2));
        Assert.That(redoChanges, Is.GreaterThanOrEqualTo(2));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ReloadClearsBothStacksEvenOnFailure(bool fail)
    {
        vm.PlaceSample(first, 125);
        vm.PlaceSample(second, 250);
        vm.UndoCommand.Execute(null);
        var pending = new TaskCompletionSource<WorkstationSource>();
        source.Load = (_, _) => pending.Task;
        var load = vm.LoadAsync("replacement.json", CancellationToken.None);
        Assert.That(vm.CanUndo, Is.False);
        Assert.That(vm.CanRedo, Is.False);
        if (fail) pending.SetException(new IOException("load failed"));
        else pending.SetResult(new WorkstationSource("map.osu", "audio.wav", new ProjectData { Tracks = [first] }));
        await load;
        vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;
        Assert.That(vm.CanUndo, Is.False);
        Assert.That(vm.CanRedo, Is.False);
    }

    [Test]
    public async Task ReturnAndDeactivationDisableHistory()
    {
        vm.PlaceSample(first, 125);
        vm.PlaceSample(second, 250);
        vm.UndoCommand.Execute(null);
        Assert.That(await vm.PrepareToReturnAsync(CancellationToken.None), Is.True);
        Assert.That(vm.CanUndo, Is.False);
        Assert.That(vm.CanRedo, Is.False);
        vm.CancelReturn();
        Assert.That(vm.CanUndo, Is.True);
        Assert.That(vm.CanRedo, Is.True);
        await vm.DeactivateAsync();
        vm.UndoCommand.Execute(null);
        vm.RedoCommand.Execute(null);
        Assert.That(first.Samples, Has.Count.EqualTo(1));
        Assert.That(second.Samples, Is.Empty);
        Assert.That(vm.CanUndo, Is.False);
        Assert.That(vm.CanRedo, Is.False);
    }

    [Test]
    public async Task DelayedSaveFinishesWithLatestHistoryState()
    {
        await vm.DeactivateAsync();
        saves = new DelayedProjectFileService();
        vm = new WorkstationPageVM(source, new MarkerPlaybackCoordinator(audio = new StubWorkstationAudioService(), Task.Delay),
            new ImmediateUiScheduler(), files, projectFiles: saves, saveDelay: (_, _) => Task.CompletedTask);
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;
        vm.PlaceSample(first, 125);
        Assert.That(saves.FirstWriteStarted, Is.True);
        vm.UndoCommand.Execute(null);
        vm.RedoCommand.Execute(null);
        vm.UndoCommand.Execute(null);
        saves.Release();
        await vm.FlushSavesAsync();
        Assert.That(ProjectDataSerializer.Deserialize(saves.LatestJson!).Tracks[0].Samples, Is.Empty);
        Assert.That(vm.CanRedo, Is.True);
    }

    [Test]
    public async Task PlaybackFailureDoesNotRollbackHistory()
    {
        vm.PlaceSample(first, 125);
        await vm.FlushPlaybackUpdatesAsync();
        audio.PrepareSamplesAsync = _ => Task.FromException(new IOException("playback preparation failed"));
        vm.UndoCommand.Execute(null);
        await vm.FlushPlaybackUpdatesAsync();
        Assert.That(first.Samples, Is.Empty);
        Assert.That(vm.ErrorMessage, Is.EqualTo("playback preparation failed"));
        Assert.That(vm.CanRedo, Is.True);
        audio.PrepareSamplesAsync = null;
        vm.RedoCommand.Execute(null);
        await vm.FlushPlaybackUpdatesAsync();
        Assert.That(first.Samples.Single().Timestamp, Is.EqualTo(125));
        Assert.That(vm.CanUndo, Is.True);
    }

    [Test]
    public async Task SaveFailureDoesNotRollbackHistory()
    {
        await vm.DeactivateAsync();
        var failingSaves = new FailOnceProjectFileService();
        vm = new WorkstationPageVM(source, new MarkerPlaybackCoordinator(audio = new StubWorkstationAudioService(), Task.Delay),
            new ImmediateUiScheduler(), files, projectFiles: failingSaves, saveDelay: (_, _) => Task.CompletedTask);
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;
        vm.PlaceSample(first, 125);
        var node = first.Samples.Single();
        Assert.ThrowsAsync<IOException>(async () => await vm.FlushSavesAsync());
        Assert.That(vm.SaveError, Is.EqualTo("Disk full"));
        Assert.That(first.Samples.Single(), Is.SameAs(node));
        Assert.That(vm.CanUndo, Is.True);
        vm.UndoCommand.Execute(null);
        await vm.FlushSavesAsync();
        Assert.That(first.Samples, Is.Empty);
        Assert.That(vm.CanRedo, Is.True);
        Assert.That(ProjectDataSerializer.Deserialize(failingSaves.SavedJson!).Tracks[0].Samples, Is.Empty);
    }
}
