using Newtonsoft.Json.Linq;
using Onset.Core.Entities;
using Onset.Services;
using Onset.Services.ProjectFiles;
using Onset.ViewModels.Shell;

namespace Onset.Test.Presentation;

public class WorkstationZoomTests
{
    [Test]
    public async Task CollapsePreservesExpandedGrowthAcrossMinimumChanges()
    {
        var (vm, saves) = Create((_, token) => Task.Delay(Timeout.Infinite, token));
        await Load(vm);
        vm.Project!.Tracks.Add(new Track()); vm.Tracks.Add(vm.Project.Tracks.Last());
        vm.SetTrackHeightMinimum(200);
        vm.IncreaseTrackHeightCommand.Execute(null);
        vm.IncreaseTrackHeightCommand.Execute(null);
        vm.ToggleTracksCollapsedCommand.Execute(null);
        vm.SetTrackHeightMinimum(56);
        Assert.Multiple(() =>
        {
            Assert.That(vm.AreTracksCollapsed, Is.True);
            Assert.That(vm.TrackHeight, Is.EqualTo(56));
            Assert.That(vm.MaximumTrackHeight, Is.EqualTo(56));
            Assert.That(vm.IncreaseTrackHeightCommand.CanExecute(null), Is.False);
            Assert.That(vm.DecreaseTrackHeightCommand.CanExecute(null), Is.False);
        });
        vm.IncreaseTrackHeightCommand.Execute(null);
        vm.DecreaseTrackHeightCommand.Execute(null);
        Assert.That(vm.TrackHeight, Is.EqualTo(56));
        vm.ToggleTracksCollapsedCommand.Execute(null);
        vm.SetTrackHeightMinimum(240);
        Assert.That(vm.TrackHeight, Is.EqualTo(252));
        vm.ToggleTracksCollapsedCommand.Execute(null);
        Assert.That(vm.TrackHeight, Is.EqualTo(240));
        vm.ToggleTracksCollapsedCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(vm.AreTracksCollapsed, Is.False);
            Assert.That(vm.TrackHeight, Is.EqualTo(252));
            Assert.That(vm.HasPendingSaves, Is.False);
            Assert.That(saves.Writes, Is.EqualTo(4));
        });
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task CollapseCommandFollowsLifecycle()
    {
        var (vm, _) = Create((_, token) => Task.Delay(Timeout.Infinite, token));
        Assert.That(vm.AreTracksCollapsed, Is.False);
        Assert.That(vm.ToggleTracksCollapsedCommand.CanExecute(null), Is.False);
        vm.ToggleTracksCollapsedCommand.Execute(null);
        Assert.That(vm.AreTracksCollapsed, Is.False);
        await Load(vm);
        Assert.That(vm.Tracks, Is.Empty);
        Assert.That(vm.ToggleTracksCollapsedCommand.CanExecute(null), Is.False);
        vm.AddPianoTrackCommand.Execute(null);
        Assert.That(vm.ToggleTracksCollapsedCommand.CanExecute(null), Is.True);
        vm.IsReturning = true;
        Assert.That(vm.ToggleTracksCollapsedCommand.CanExecute(null), Is.False);
        vm.ToggleTracksCollapsedCommand.Execute(null);
        Assert.That(vm.AreTracksCollapsed, Is.False);
        vm.IsReturning = false;
        vm.ToggleTracksCollapsedCommand.Execute(null);
        Assert.That(vm.AreTracksCollapsed, Is.True);
        await vm.DeactivateAsync();
        Assert.That(vm.ToggleTracksCollapsedCommand.CanExecute(null), Is.False);
        vm.ToggleTracksCollapsedCommand.Execute(null);
        Assert.That(vm.AreTracksCollapsed, Is.True);
    }

    [Test]
    public async Task TrackHeightUsesConfiguredMinimumAndSixUnitSteps()
    {
        var (vm, _) = Create((_, token) => Task.Delay(Timeout.Infinite, token));
        await Load(vm);
        vm.SetTrackHeightMinimum(200);
        Assert.That(vm.TrackHeight, Is.EqualTo(200));
        vm.IncreaseTrackHeightCommand.Execute(null);
        Assert.That(vm.TrackHeight, Is.EqualTo(206));
        for (var i = 0; i < 100; i++) vm.DecreaseTrackHeightCommand.Execute(null);
        Assert.That(vm.TrackHeight, Is.EqualTo(200));
        Assert.That(vm.DecreaseTrackHeightCommand.CanExecute(null), Is.False);
        for (var i = 0; i < 100; i++) vm.IncreaseTrackHeightCommand.Execute(null);
        Assert.That(vm.TrackHeight, Is.EqualTo(308));
        Assert.That(vm.IncreaseTrackHeightCommand.CanExecute(null), Is.False);
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task ChangingMinimumPreservesUserGrowthAndRejectsInvalidInputs()
    {
        var (vm, _) = Create((_, token) => Task.Delay(Timeout.Infinite, token));
        await Load(vm);
        vm.SetTrackHeightMinimum(200);
        vm.IncreaseTrackHeightCommand.Execute(null); vm.IncreaseTrackHeightCommand.Execute(null);
        vm.SetTrackHeightMinimum(240);
        Assert.That(vm.TrackHeight, Is.EqualTo(252));
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, 0d, -1d }) vm.SetTrackHeightMinimum(value);
        Assert.That(vm.MinimumTrackHeight, Is.EqualTo(240));
        Assert.That(vm.TrackHeight, Is.EqualTo(252));
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task ScrollingCommitsOnlyLatestValueAfterIdle()
    {
        var delays = new List<TaskCompletionSource>();
        var (vm, saves) = Create((duration, _) =>
        {
            Assert.That(duration, Is.EqualTo(TimeSpan.FromMilliseconds(300)));
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            delays.Add(gate);
            return gate.Task;
        });
        await Load(vm);
        for (var i = 0; i < 3; i++) vm.ZoomInCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(vm.ZoomFactor, Is.EqualTo(35));
            Assert.That(vm.Project!.WorkstationZoom, Is.EqualTo(32));
            Assert.That(saves.Writes, Is.Zero);
            Assert.That(vm.HasPendingSaves, Is.True);
        });
        delays[0].SetResult();
        delays[1].SetResult();
        delays[2].SetResult();
        await StubWorkstationAudioService.WaitUntilAsync(() => saves.Writes == 1);
        await vm.FlushSavesAsync();
        Assert.That(ProjectDataSerializer.Deserialize(saves.LatestJson!).WorkstationZoom, Is.EqualTo(35));
        Assert.That(vm.HasPendingSaves, Is.False);
        await vm.DeactivateAsync();
    }

    [TestCase("flush")]
    [TestCase("return")]
    [TestCase("deactivate")]
    [TestCase("reload")]
    public async Task LifecycleCommitsPendingZoomWithoutWaiting(string action)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (vm, saves) = Create((_, _) => gate.Task);
        await Load(vm);
        vm.ZoomInCommand.Execute(null);
        switch (action)
        {
            case "flush": await vm.FlushSavesAsync(); break;
            case "return": Assert.That(await vm.PrepareToReturnAsync(CancellationToken.None), Is.True); break;
            case "deactivate": await vm.DeactivateAsync(); break;
            case "reload": await vm.LoadAsync("second.json", CancellationToken.None); break;
        }
        Assert.That(saves.Paths, Is.EqualTo(new[] { "project.json" }));
        Assert.That(ProjectDataSerializer.Deserialize(saves.LatestJson!).WorkstationZoom, Is.EqualTo(33));
        gate.SetResult();
        await vm.DeactivateAsync();
        Assert.That(saves.Writes, Is.EqualTo(1));
    }

    [Test]
    public async Task ReturningToSavedZoomDoesNotWrite()
    {
        var (vm, saves) = Create((_, token) => Task.Delay(Timeout.Infinite, token));
        await Load(vm);
        vm.ZoomInCommand.Execute(null);
        vm.ZoomOutCommand.Execute(null);
        await vm.FlushSavesAsync();
        Assert.That(saves.Writes, Is.Zero);
        Assert.That(vm.HasPendingSaves, Is.False);
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task ZoomSaveFailureCanBeRetried()
    {
        var saves = new FailOnceProjectFileService();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), projectFiles: saves,
            zoomDelay: (_, token) => Task.Delay(Timeout.Infinite, token));
        await Load(vm);
        vm.ZoomInCommand.Execute(null);
        Assert.ThrowsAsync<IOException>(() => vm.FlushSavesAsync());
        Assert.That(vm.SaveError, Is.Not.Null);
        vm.RetrySaveCommand.Execute(null);
        await vm.FlushSavesAsync();
        Assert.That(ProjectDataSerializer.Deserialize(saves.SavedJson!).WorkstationZoom, Is.EqualTo(33));
        await vm.DeactivateAsync();
    }

    private static (WorkstationPageVM Vm, DelayedProjectFileService Saves) Create(Func<TimeSpan, CancellationToken, Task> delay)
    {
        var saves = new DelayedProjectFileService();
        saves.Release();
        return (new WorkstationPageVM(new StubWorkstationSourceService(), new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), projectFiles: saves, zoomDelay: delay), saves);
    }

    private static async Task Load(WorkstationPageVM vm)
    {
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration);
    }

    [Test]
    public void MissingZoomDefaultsTo32InSerializedProject()
    {
        var json = JObject.Parse(ProjectDataSerializer.Serialize(ProjectDataSerializer.Deserialize("{}")));
        Assert.That(json.Value<int?>("WorkstationZoom"), Is.EqualTo(32));
    }

    [TestCase(64, 64)]
    [TestCase(0, 8)]
    [TestCase(200, 128)]
    public async Task RestoresZoomBeforePresentation(int stored, int expected)
    {
        var project = ProjectDataSerializer.Deserialize($"{{\"WorkstationZoom\":{stored}}}");
        var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav", project)) };
        var vm = new WorkstationPageVM(source, new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler());
        await vm.LoadAsync("project.json", CancellationToken.None);
        Assert.That(vm.ZoomFactor, Is.EqualTo(expected));
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task ZoomCommandsClampAt8And128()
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler());
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration);
        Assert.That(vm.ZoomFactor, Is.EqualTo(32));
        for (var i = 0; i < 100; i++) vm.ZoomOutCommand.Execute(null);
        Assert.That(vm.ZoomFactor, Is.EqualTo(8));
        Assert.That(vm.ZoomOutCommand.CanExecute(null), Is.False);
        for (var i = 0; i < 200; i++) vm.ZoomInCommand.Execute(null);
        Assert.That(vm.ZoomFactor, Is.EqualTo(128));
        Assert.That(vm.ZoomInCommand.CanExecute(null), Is.False);
        await vm.DeactivateAsync();
    }
}
