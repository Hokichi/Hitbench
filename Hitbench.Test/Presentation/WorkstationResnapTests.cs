using Hitbench.Test;
using Hitbench.Core.Entities;
using Hitbench.Services;
using Hitbench.Services.ProjectFiles;
using Hitbench.ViewModels.Shell;

namespace Hitbench.Test.Presentation;

public class WorkstationResnapTests
{
    private WorkstationPageVM vm = null!;
    private StubWorkstationAudioService audio = null!;
    private DelayedProjectFileService saves = null!;
    private StubTrackRemovalDialogService removal = null!;
    private StubSampleFileDialogService importDialog = null!;
    private Track first = null!, second = null!;
    private Sample a = null!, b = null!, c = null!;

    [SetUp]
    public async Task Setup()
    {
        a = new Sample { Timestamp = 1100, FileName = "a.wav" };
        b = new Sample { Timestamp = 1200, FileName = "a.wav" };
        c = new Sample { Timestamp = 1188, FileName = "b.wav" };
        first = new Track { SelectedSampleFileName = "a.wav", Samples = [a, b] };
        second = new Track { SelectedSampleFileName = "b.wav", Samples = [c] };
        var project = new ProjectData { UninheritedPoints = TimingTestData.Points([0], [120]), Tracks = [first, second], ControlPoints = [new()] };
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav", project))
        };
        audio = new StubWorkstationAudioService();
        saves = new DelayedProjectFileService();
        saves.Release();
        removal = new StubTrackRemovalDialogService { Confirm = _ => Task.FromResult(true) };
        importDialog = new StubSampleFileDialogService(Array.Empty<string>());
        vm = new WorkstationPageVM(source, new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler(),
            new StubSampleFileService(["a.wav", "b.wav"]), sampleDialog: importDialog,
            projectFiles: saves, trackRemovalDialog: removal);
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;
        await vm.FlushPlaybackUpdatesAsync();
        await vm.FlushSavesAsync();
    }

    [TearDown]
    public async Task Cleanup()
    {
        await vm.DeactivateAsync();
        audio.Dispose();
    }

    [Test]
    public async Task ControlPointCollisionsAndInvalidTimingPreserveEveryPoint()
    {
        var firstPoint = new ControlPoint { Timestamp = 1100 };
        var secondPoint = new ControlPoint { Timestamp = 1120 };
        vm.Project!.ControlPoints.AddRange([firstPoint, secondPoint]);
        vm.ResnapAllSampleNodes();
        Assert.That(new[] { firstPoint.Timestamp, secondPoint.Timestamp }, Is.EqualTo(new[] { 1125, 1120 }));
        Assert.That(vm.Project.ControlPoints, Has.Count.EqualTo(3));
        vm.Project.UninheritedPoints = TimingTestData.Replace(vm.Project.UninheritedPoints, [0], false);
        firstPoint.Timestamp = 1100;
        Assert.That(vm.ResnapAllSampleNodes(), Is.False);
        Assert.That(firstPoint.Timestamp, Is.EqualTo(1100));
        await vm.FlushSavesAsync();
    }

    [Test]
    public void ResnapWorksWhenOnlyControlPointsExist()
    {
        first.Samples.Clear(); second.Samples.Clear();
        var point = new ControlPoint { Timestamp = 1100 };
        vm.Project!.ControlPoints.Add(point);
        Assert.That(vm.ResnapAllSamplesCommand.CanExecute(null), Is.True);
        Assert.That(vm.ResnapAllSampleNodes(), Is.True);
        Assert.That(point.Timestamp, Is.EqualTo(1125));
    }

    [Test]
    public async Task ResnapIncludesControlPointsAndPersistsTheirIdentity()
    {
        var point = new ControlPoint { Timestamp = 1100, Volume = 70, IsDisabled = true };
        vm.Project!.ControlPoints.Add(point);
        Assert.That(vm.ResnapAllSampleNodes(), Is.True);
        Assert.That(point.Timestamp, Is.EqualTo(1125));
        Assert.That(point.Volume, Is.EqualTo(70));
        Assert.That(point.IsDisabled, Is.True);
        await vm.FlushSavesAsync();
        Assert.That(ProjectDataSerializer.Deserialize(saves.LatestJson!).ControlPoints.Single(p => p.Timestamp != 0).Timestamp, Is.EqualTo(1125));
    }

    [Test]
    public async Task CommandResnapsAllTracksIndependentlyAndPublishesOneUndoableBatch()
    {
        vm.SetNodeSelection([a]);
        var writes = saves.Writes;
        var updates = audio.SampleUpdates.Count;
        var revision = vm.SamplesRevision;

        vm.ResnapAllSamplesCommand.Execute(null);

        Assert.That(new[] { a.Timestamp, b.Timestamp, c.Timestamp }, Is.EqualTo(new[] { 1125, 1250, 1250 }));
        Assert.That(first.Samples, Is.EqualTo(new[] { a, b }));
        Assert.That(second.Samples.Single(), Is.SameAs(c));
        Assert.That(vm.SelectedSamples, Is.EqualTo(new[] { a }));
        Assert.That(new[] { a.FileName, b.FileName, c.FileName }, Is.EqualTo(new[] { "a.wav", "a.wav", "b.wav" }));
        Assert.That(vm.SamplesRevision, Is.EqualTo(revision + 1));
        await vm.FlushSavesAsync();
        await vm.FlushPlaybackUpdatesAsync();
        Assert.That(saves.Writes, Is.EqualTo(writes + 1));
        Assert.That(audio.SampleUpdates.Count, Is.EqualTo(updates + 1));
        var persisted = ProjectDataSerializer.Deserialize(saves.LatestJson!);
        Assert.That(persisted.Tracks.SelectMany(t => t.Samples).Select(n => n.Timestamp),
            Is.EqualTo(new[] { 1125, 1250, 1250 }));
        Assert.That(audio.SampleUpdates.Last().Select(n => n.Timestamp), Is.EqualTo(new[] { 1125, 1250, 1250 }));

        vm.UndoCommand.Execute(null);
        Assert.That(new[] { a.Timestamp, b.Timestamp, c.Timestamp }, Is.EqualTo(new[] { 1100, 1200, 1188 }));
        Assert.That(vm.CanUndo, Is.False);
        vm.RedoCommand.Execute(null);
        Assert.That(new[] { a.Timestamp, b.Timestamp, c.Timestamp }, Is.EqualTo(new[] { 1125, 1250, 1250 }));
    }

    [Test]
    public void CurrentSubdivisionReevaluatesPreviouslySnappedNodesAndKeepsCollisions()
    {
        a.Timestamp = 1125;
        b.Timestamp = 1375;
        c.Timestamp = 1500;
        vm.SelectedSubdivisionIndex = 0;
        Assert.That(vm.ResnapAllSampleNodes(), Is.True);
        Assert.That(new[] { a.Timestamp, b.Timestamp, c.Timestamp }, Is.EqualTo(new[] { 1000, 1500, 1500 }));
        Assert.That(second.Samples.Single(), Is.SameAs(c));
    }

    [TestCase(0, 0, 250, 0, 9999, 9500)]
    [TestCase(1, 0, 125, 0, 9999, 9750)]
    [TestCase(3, 0, 188, 250, 9999, 9875)]
    public void NearestBeatsHonorTiesRoundingAndExclusiveSongEnd(int subdivisionIndex,
        int firstTime, int middleTime, int middleExpected, int lastTime, int lastExpected)
    {
        vm.SelectedSubdivisionIndex = subdivisionIndex;
        a.Timestamp = firstTime;
        b.Timestamp = middleTime;
        c.Timestamp = lastTime;
        vm.ResnapAllSampleNodes();
        Assert.That(new[] { a.Timestamp, b.Timestamp, c.Timestamp },
            Is.EqualTo(new[] { 0, middleExpected, lastExpected }));
    }

    [Test]
    public void TimingChangesAndFractionalBeatsUseExistingGridRules()
    {
        vm.Project!.UninheritedPoints = TimingTestData.Replace(vm.Project!.UninheritedPoints, [0, 700], true);
        vm.Project.UninheritedPoints = TimingTestData.Replace(vm.Project.UninheritedPoints, [120, 123], false);
        a.Timestamp = 690;
        b.Timestamp = 820;
        c.Timestamp = 970;
        Assert.That(vm.ResnapAllSampleNodes(), Is.True);
        Assert.That(new[] { a.Timestamp, b.Timestamp, c.Timestamp }, Is.EqualTo(new[] { 700, 822, 944 }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task NoMovementOrInvalidGridPreservesRedoWithoutPartialChanges(bool invalid)
    {
        a.Timestamp = 1000;
        b.Timestamp = 1250;
        c.Timestamp = 1125;
        vm.PlaceSample(first, 500);
        vm.UndoCommand.Execute(null);
        await vm.FlushSavesAsync();
        await vm.FlushPlaybackUpdatesAsync();
        var writes = saves.Writes;
        var updates = audio.SampleUpdates.Count;
        var revision = vm.SamplesRevision;
        if (invalid) vm.Project!.UninheritedPoints = TimingTestData.Replace(vm.Project!.UninheritedPoints, [0], false);

        Assert.That(vm.ResnapAllSampleNodes(), Is.False);
        Assert.That(new[] { a.Timestamp, b.Timestamp, c.Timestamp }, Is.EqualTo(new[] { 1000, 1250, 1125 }));
        Assert.That(vm.CanRedo, Is.True);
        Assert.That(vm.CanUndo, Is.False);
        Assert.That(vm.SamplesRevision, Is.EqualTo(revision));
        await vm.FlushSavesAsync();
        await vm.FlushPlaybackUpdatesAsync();
        Assert.That(saves.Writes, Is.EqualTo(writes));
        Assert.That(audio.SampleUpdates.Count, Is.EqualTo(updates));
        if (invalid) Assert.That(vm.ErrorMessage, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public async Task ActiveDragUsesCommittedTimestampsAndCancelsPreview()
    {
        vm.SetNodeSelection([a]);
        vm.BeginNodeDrag(first, a);
        vm.PreviewNodeDrag(500);
        Assert.That(vm.ResnapAllSampleNodes(), Is.True);
        Assert.That(a.Timestamp, Is.EqualTo(1125));
        Assert.That(vm.NodePreviewTimestamps, Is.Empty);
        Assert.That(vm.IsNodeDragActive, Is.False);
        Assert.That(await vm.CommitNodeDragAsync(), Is.False);
    }

    [Test]
    public void UnchangedResnapCancelsDragWithoutHistory()
    {
        a.Timestamp = 1000;
        b.Timestamp = 1250;
        c.Timestamp = 1125;
        vm.BeginNodeDrag(first, a);
        vm.PreviewNodeDrag(500);
        Assert.That(vm.ResnapAllSampleNodes(), Is.False);
        Assert.That(vm.NodePreviewTimestamps, Is.Empty);
        Assert.That(vm.IsNodeDragActive, Is.False);
        Assert.That(vm.CanUndo, Is.False);
    }

    [Test]
    public void GridWithNoBeatInsideSongRejectsEntireBatch()
    {
        vm.Project!.UninheritedPoints = TimingTestData.Replace(vm.Project!.UninheritedPoints, [10000], true);
        Assert.That(vm.ResnapAllSampleNodes(), Is.False);
        Assert.That(new[] { a.Timestamp, b.Timestamp, c.Timestamp }, Is.EqualTo(new[] { 1100, 1200, 1188 }));
        Assert.That(vm.CanUndo, Is.False);
        Assert.That(vm.ErrorMessage, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void EmptyNodeTransitionsAndReturnStateUpdateCommandAvailability()
    {
        var changes = 0;
        vm.ResnapAllSamplesCommand.CanExecuteChanged += (_, _) => changes++;
        vm.SetNodeSelection([a, b, c]);
        vm.RemoveSelectedNodes();
        Assert.That(vm.ResnapAllSamplesCommand.CanExecute(null), Is.False);
        Assert.That(vm.ResnapAllSampleNodes(), Is.False);
        Assert.That(changes, Is.GreaterThan(0));
        vm.UndoCommand.Execute(null);
        Assert.That(vm.ResnapAllSamplesCommand.CanExecute(null), Is.True);
        vm.IsReturning = true;
        Assert.That(vm.ResnapAllSamplesCommand.CanExecute(null), Is.False);
        Assert.That(vm.ResnapAllSampleNodes(), Is.False);
        vm.IsReturning = false;
        Assert.That(vm.ResnapAllSamplesCommand.CanExecute(null), Is.True);
    }

    [Test]
    public async Task RemovalBusyStateDisablesThenRestoresCommandWithoutResnapping()
    {
        var confirmation = new TaskCompletionSource<bool>();
        removal.Confirm = _ => confirmation.Task;
        var changes = 0;
        vm.ResnapAllSamplesCommand.CanExecuteChanged += (_, _) => changes++;
        var pending = vm.RemoveTrackCommand.ExecuteAsync(second);
        try
        {
            Assert.That(vm.ResnapAllSamplesCommand.CanExecute(null), Is.False);
            Assert.That(vm.ResnapAllSampleNodes(), Is.False);
            Assert.That(a.Timestamp, Is.EqualTo(1100));
            Assert.That(changes, Is.GreaterThan(0));
        }
        finally
        {
            confirmation.TrySetResult(false);
            await pending;
        }
        Assert.That(vm.ResnapAllSamplesCommand.CanExecute(null), Is.True);
        Assert.That(changes, Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public async Task LoadingAndDeactivationRejectResnap()
    {
        await vm.LoadAsync("project.json", CancellationToken.None);
        Assert.That(vm.ResnapAllSamplesCommand.CanExecute(null), Is.False);
        Assert.That(vm.ResnapAllSampleNodes(), Is.False);
        vm.AcknowledgePresentationReady(vm.LoadGeneration);
        Assert.That(vm.ResnapAllSamplesCommand.CanExecute(null), Is.True);
        await vm.DeactivateAsync();
        Assert.That(vm.ResnapAllSamplesCommand.CanExecute(null), Is.False);
        Assert.That(vm.ResnapAllSampleNodes(), Is.False);
    }

    [Test]
    public async Task ImportBusyStateDisablesThenRestoresCommand()
    {
        var entered = new TaskCompletionSource();
        var selection = new TaskCompletionSource<IReadOnlyList<string>>();
        importDialog.Selection = _ =>
        {
            entered.TrySetResult();
            return selection.Task;
        };
        var changes = 0;
        vm.ResnapAllSamplesCommand.CanExecuteChanged += (_, _) => changes++;
        var pending = vm.LoadSampleCommand.ExecuteAsync(null);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(vm.ResnapAllSamplesCommand.CanExecute(null), Is.False);
            Assert.That(vm.ResnapAllSampleNodes(), Is.False);
            Assert.That(a.Timestamp, Is.EqualTo(1100));
            Assert.That(changes, Is.GreaterThan(0));
        }
        finally
        {
            selection.TrySetResult(Array.Empty<string>());
            await pending;
        }
        Assert.That(vm.ResnapAllSamplesCommand.CanExecute(null), Is.True);
        Assert.That(changes, Is.GreaterThanOrEqualTo(2));
    }
}
