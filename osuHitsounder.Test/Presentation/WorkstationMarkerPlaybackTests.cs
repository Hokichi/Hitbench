using osuHitsounder.Core.Entities;
using osuHitsounder.ViewModels.Shell;

namespace osuHitsounder.Test.Presentation;

public class WorkstationMarkerPlaybackTests
{
    private StubWorkstationAudioService audio = null!;
    [SetUp]
    public void Setup() => audio = new StubWorkstationAudioService();
    private WorkstationPageVM vm = null!;

    private async Task Load(params int[] timestamps)
    {
        var project = new ProjectData { Offsets = [0, 100], BPMs = [120, 240], Tracks = [new Track
        {
            Samples = timestamps.Select(time => new Sample { FileName = "hit.wav", Timestamp = time }).ToList()
        }] };
        vm = new WorkstationPageVM(new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "song.wav", project))
        }, audio, new ImmediateUiScheduler());
        await vm.LoadAsync("project.json", CancellationToken.None);
    }

    private void Tick(double milliseconds)
    {
        audio.Snapshot = audio.Snapshot with { Position = TimeSpan.FromMilliseconds(milliseconds) };
        vm.RefreshPlayback();
    }

    [TearDown]
    public async Task Cleanup() { if (vm is not null) await vm.DeactivateAsync(); audio.Dispose(); }

    [Test]
    public async Task CrossingsTriggerEachPlacementOnceAndNeverWhilePausedOrEnded()
    {
        await Load(0, 10, 10, 31);
        vm.PlayPauseCommand.Execute(null);
        Assert.That(audio.TriggeredSamples.SelectMany(ids => ids), Is.EqualTo(new long[] { 1 }));
        Tick(10);
        Assert.That(audio.TriggeredSamples, Has.Count.EqualTo(1));
        Tick(12.5);
        Tick(12.5);
        Tick(90);
        Assert.That(audio.TriggeredSamples.SelectMany(ids => ids), Is.EqualTo(new long[] { 1,2,3,4 }));
        vm.PlayPauseCommand.Execute(null);
        Tick(100);
        audio.Snapshot = new AudioPlaybackSnapshot(TimeSpan.FromSeconds(10), false, null);
        vm.RefreshPlayback();
        Assert.That(audio.TriggeredSamples.SelectMany(ids => ids), Is.EqualTo(new long[] { 1,2,3,4 }));
        vm.SeekCommand.Execute(TimeSpan.FromMilliseconds(10));
        vm.PlayPauseCommand.Execute(null);
        Tick(12.5);
        Assert.That(audio.TriggeredSamples.SelectMany(ids => ids), Is.EqualTo(new long[] { 1,2,3,4,2,3 }));
    }

    [Test]
    public async Task PlayWaitsForPendingPreparation()
    {
        await Load(0);
        var prepared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        audio.PrepareSamplesAsync = _ => prepared.Task;
        vm.SetTrackVolume(vm.Tracks[0], 50);
        var playing = vm.PlayPauseCommand.ExecuteAsync(null);
        try
        {
            Assert.That(vm.IsPlaying, Is.False);
            Assert.That(audio.TriggeredSamples, Is.Empty);
        }
        finally { prepared.TrySetResult(); await vm.FlushPlaybackUpdatesAsync(); await playing; }
        Assert.That(vm.IsPlaying, Is.True);
        Assert.That(audio.TriggeredSamples.SelectMany(ids => ids), Is.EqualTo(new long[] { 1 }));
    }

    [Test]
    public async Task NewPreparationSupersedesWhatPlayWasWaitingFor()
    {
        await Load(0);
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        audio.PrepareSamplesAsync = _ => first.Task;
        vm.SetTrackVolume(vm.Tracks[0], 50);
        var firstUpdate = vm.FlushPlaybackUpdatesAsync();
        var playing = vm.PlayPauseCommand.ExecuteAsync(null);
        audio.PrepareSamplesAsync = _ => second.Task;
        vm.SetTrackVolume(vm.Tracks[0], 60);
        try
        {
            first.SetResult();
            await firstUpdate;
            await Task.WhenAny(playing, Task.Delay(50));
            Assert.That(playing.IsCompleted, Is.False);
            Assert.That(vm.IsPlaying, Is.False);
        }
        finally { first.TrySetResult(); second.TrySetResult(); await playing; }
        Assert.That(vm.IsPlaying, Is.True);
    }

    [TestCase("stop")]
    [TestCase("seek")]
    [TestCase("deactivate")]
    [TestCase("reload")]
    [TestCase("failure")]
    public async Task PendingPlayCannotOutliveTransportIntent(string action)
    {
        await Load(0);
        var prepared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        audio.PrepareSamplesAsync = _ => prepared.Task;
        vm.SetTrackVolume(vm.Tracks[0], 50);
        var playing = vm.PlayPauseCommand.ExecuteAsync(null);
        Task? changing = null;
        try
        {
            if (action == "stop") vm.StopCommand.Execute(null);
            if (action == "seek") vm.SeekCommand.Execute(TimeSpan.FromMilliseconds(5));
            if (action == "deactivate") changing = vm.DeactivateAsync();
            if (action == "reload") { audio.PrepareSamplesAsync = null; changing = vm.LoadAsync("other.json", CancellationToken.None); }
            if (action == "failure") prepared.SetException(new IOException("Preparation failed"));
            else prepared.SetResult();
            await playing;
            if (changing is not null) await changing;
            Assert.That(vm.IsPlaying, Is.False);
            Assert.That(audio.TriggeredSamples, Is.Empty);
            if (action == "failure") Assert.That(vm.ErrorMessage, Does.Contain("Preparation failed"));
        }
        finally { prepared.TrySetResult(); await vm.FlushPlaybackUpdatesAsync(); }
    }

    [Test]
    public async Task DeviceFailureAtZeroResetsStartingBoundary()
    {
        await Load(0);
        await vm.PlayPauseCommand.ExecuteAsync(null);
        audio.Snapshot = new AudioPlaybackSnapshot(TimeSpan.Zero, false, "device lost");
        vm.RefreshPlayback();
        await vm.PlayPauseCommand.ExecuteAsync(null);
        Assert.That(audio.TriggeredSamples.SelectMany(ids => ids), Is.EqualTo(new long[] { 1, 1 }));
    }

    [Test]
    public async Task PauseAtZeroDoesNotRetriggerStartingBoundary()
    {
        await Load(0);
        await vm.PlayPauseCommand.ExecuteAsync(null);
        await vm.PlayPauseCommand.ExecuteAsync(null);
        await vm.PlayPauseCommand.ExecuteAsync(null);
        Assert.That(audio.TriggeredSamples.SelectMany(ids => ids), Is.EqualTo(new long[] { 1 }));
    }

    [Test]
    public async Task MarkerQuantizesDuplicateTicksAndTimingTransitions()
    {
        await Load(0, 10, 10, 31);
        vm.PlayPauseCommand.Execute(null);
        Tick(10);
        Assert.That(vm.Position.TotalMilliseconds, Is.EqualTo(8.3333).Within(.0001));
        Tick(12.5);
        Tick(12.5);
        Tick(90);
        Assert.That(vm.Position.TotalMilliseconds, Is.EqualTo(87.5));
        Tick(100);
        Assert.That(vm.PlaybackInterval.TotalMilliseconds, Is.EqualTo(2.0833).Within(.0001));
    }

    [Test]
    public async Task PauseAndResumeRetainMarkerPosition()
    {
        await Load(0, 3, 6);
        vm.PlayPauseCommand.Execute(null);
        audio.Snapshot = audio.Snapshot with { Position = TimeSpan.FromMilliseconds(3.5) };
        vm.PlayPauseCommand.Execute(null);
        Tick(3.5);
        Assert.That(vm.Position.TotalMilliseconds, Is.EqualTo(3.5));
        vm.PlayPauseCommand.Execute(null);
        Tick(8.5);
        Assert.That(vm.Position.TotalMilliseconds, Is.EqualTo(8.3333).Within(.0001));
    }

    [Test]
    public async Task SeekingResetsMarkerWithoutMovingBackwards()
    {
        await Load(0, 5, 10, 11, 50);
        vm.PlayPauseCommand.Execute(null);
        vm.SeekCommand.Execute(TimeSpan.FromMilliseconds(10));
        Assert.That(vm.Position.TotalMilliseconds, Is.EqualTo(10));
        Tick(11);
        Assert.That(vm.Position.TotalMilliseconds, Is.EqualTo(10));
        Tick(12.5);
        vm.PlayPauseCommand.Execute(null);
        vm.SeekCommand.Execute(TimeSpan.FromMilliseconds(5));
        vm.PlayPauseCommand.Execute(null);
        vm.StopCommand.Execute(null);
        vm.PlayPauseCommand.Execute(null);
    }

    [Test]
    public async Task RemovalAndVolumeRefreshPublishUpdatedSchedule()
    {
        await Load(0, 10, 20);
        vm.PlayPauseCommand.Execute(null);
        var track = vm.Tracks[0];
        vm.RemoveSample(track, track.Samples[1]);
        vm.SetTrackVolume(track, 50);
        await vm.FlushPlaybackUpdatesAsync();
        Tick(25);
        Assert.That(audio.SampleUpdates[^1].Select(x => x.Timestamp), Is.EqualTo(new[]{0,20}));
        Assert.That(audio.SampleUpdates[^1].Select(x => x.TrackVolume), Is.All.EqualTo(50));
    }

    [Test]
    public async Task NaturalEndShowsEndpointAndDeactivationStopsRefresh()
    {
        await Load(9999);
        vm.PlayPauseCommand.Execute(null);
        audio.Snapshot = new AudioPlaybackSnapshot(TimeSpan.FromSeconds(10), false, null);
        vm.RefreshPlayback();
        Assert.That(vm.IsPlaying, Is.False);
        await vm.DeactivateAsync();
        Tick(10);
        Assert.That(vm.Position, Is.EqualTo(TimeSpan.FromSeconds(10)));
    }

    [Test]
    public async Task EditsPublishPastAndFuturePlacements()
    {
        await Load(0);
        vm.PlayPauseCommand.Execute(null);
        Tick(25);
        vm.Tracks[0].Samples.Add(new Sample { FileName = "hit.wav", Timestamp = 10 });
        vm.Tracks[0].Samples.Add(new Sample { FileName = "hit.wav", Timestamp = 50 });
        vm.SetTrackVolume(vm.Tracks[0], 50);
        await vm.FlushPlaybackUpdatesAsync();
        Tick(75);
        Assert.That(audio.SampleUpdates[^1].Select(x => x.Timestamp), Is.EquivalentTo(new[]{0,10,50}));
    }

    [Test]
    public async Task DeviceErrorIsPresentedAndRestartResetsMarker()
    {
        await Load(0, 10);
        vm.PlayPauseCommand.Execute(null);
        Tick(25);
        audio.Snapshot = new AudioPlaybackSnapshot(TimeSpan.Zero, false, "device lost");
        vm.RefreshPlayback();
        Assert.That(vm.ErrorMessage, Is.EqualTo("device lost"));
        vm.PlayPauseCommand.Execute(null);
    }

    [Test]
    public async Task InvalidTimingPreventsPlaybackWithActionableError()
    {
        vm = new WorkstationPageVM(new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", new ProjectData()))
        }, audio, new ImmediateUiScheduler());
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.PlayPauseCommand.Execute(null);
        Assert.That(vm.IsPlaying, Is.False);
        Assert.That(vm.ErrorMessage, Does.Contain("timing"));
    }
}
