using Hitbench.Test;
using Hitbench.Core.Entities;
using Hitbench.ViewModels.Shell;

namespace Hitbench.Test.Presentation;

public class WorkstationMarkerPlaybackTests
{
    private StubWorkstationAudioService audio = null!;
    [SetUp]
    public void Setup() => audio = new StubWorkstationAudioService();
    private Hitbench.Services.MarkerPlaybackCoordinator playback = null!;
    private WorkstationPageVM vm = null!;

    private async Task Load(params int[] timestamps)
    {
        var project = new ProjectData { UninheritedPoints = TimingTestData.Points([0, 100], [120, 240]), Tracks = [new Track
        {
            Samples = timestamps.Select(time => new Sample { FileName = "hit.wav", Timestamp = time }).ToList()
        }] };
        vm = new WorkstationPageVM(new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "song.wav", project))
        }, playback = new Hitbench.Services.MarkerPlaybackCoordinator(audio, (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration);
    }

    private async Task Tick(double milliseconds)
    {
        audio.Snapshot = audio.Snapshot with { Position = TimeSpan.FromMilliseconds(milliseconds) };
        if (audio.Snapshot.IsPlaying && !audio.Disposed)
        {
            var expected = new Hitbench.Services.PlaybackMarkerClock(TimingTestData.Points([0,100], [120,240]))
                .GetPosition(audio.Snapshot.Position, TimeSpan.FromSeconds(10));
            await StubWorkstationAudioService.WaitUntilAsync(() => playback.Latest.Playback.Position >= expected);
        }
        vm.RefreshPlayback();
    }

    [TearDown]
    public async Task Cleanup() { if (vm is not null) await vm.DeactivateAsync(); audio.Dispose(); }

    [Test]
    public async Task MarkerAdvancesAndPreservesPausedAndEndedPositions()
    {
        await Load(0, 10, 10, 31);
        await vm.PlayPauseCommand.ExecuteAsync(null);
        await Tick(12.5);
        Assert.That(vm.Position, Is.EqualTo(TimeSpan.FromMilliseconds(12.5)));
        await Tick(90);
        Assert.That(vm.Position, Is.EqualTo(TimeSpan.FromMilliseconds(87.5)));
        await vm.PlayPauseCommand.ExecuteAsync(null);
        await Tick(100);
        Assert.That(vm.IsPlaying, Is.False);
        audio.Snapshot = new AudioPlaybackSnapshot(TimeSpan.FromSeconds(10), false, null);
        await playback.PauseAsync(default);
        vm.RefreshPlayback();
        Assert.That(vm.Position, Is.EqualTo(TimeSpan.FromSeconds(10)));
        await vm.SeekCommand.ExecuteAsync(TimeSpan.FromMilliseconds(10));
        await vm.PlayPauseCommand.ExecuteAsync(null);
        await Tick(12.5);
        Assert.That(vm.Position, Is.EqualTo(TimeSpan.FromMilliseconds(12.5)));
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
        }
        finally { prepared.TrySetResult(); await vm.FlushPlaybackUpdatesAsync(); await playing; }
        Assert.That(vm.IsPlaying, Is.True);
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
            if (action == "stop") await vm.StopCommand.ExecuteAsync(null);
            if (action == "seek") await vm.SeekCommand.ExecuteAsync(TimeSpan.FromMilliseconds(5));
            if (action == "deactivate") changing = vm.DeactivateAsync();
            if (action == "reload") { audio.PrepareSamplesAsync = null; changing = vm.LoadAsync("other.json", CancellationToken.None); }
            if (action == "failure") prepared.SetException(new IOException("Preparation failed"));
            else prepared.SetResult();
            await playing;
            if (changing is not null) await changing;
            Assert.That(vm.IsPlaying, Is.False);
            if (action == "failure") Assert.That(vm.ErrorMessage, Does.Contain("Preparation failed"));
        }
        finally { prepared.TrySetResult(); await vm.FlushPlaybackUpdatesAsync(); }
    }

    [Test]
    public async Task DeviceFailureAtZeroCanRestart()
    {
        await Load(0);
        await vm.PlayPauseCommand.ExecuteAsync(null);
        audio.Snapshot = new AudioPlaybackSnapshot(TimeSpan.Zero, false, "device lost");
        vm.RefreshPlayback();
        await vm.PlayPauseCommand.ExecuteAsync(null);
        Assert.That(vm.IsPlaying, Is.True);
    }

    [Test]
    public async Task PauseAtZeroCanResume()
    {
        await Load(0);
        await vm.PlayPauseCommand.ExecuteAsync(null);
        await vm.PlayPauseCommand.ExecuteAsync(null);
        Assert.That(vm.IsPlaying, Is.False);
        await vm.PlayPauseCommand.ExecuteAsync(null);
        Assert.That(vm.IsPlaying, Is.True);
        Assert.That(vm.Position, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public async Task MarkerQuantizesDuplicateTicksAndTimingTransitions()
    {
        await Load(0, 10, 10, 31);
        await vm.PlayPauseCommand.ExecuteAsync(null);
        await Tick(10);
        Assert.That(vm.Position.TotalMilliseconds, Is.EqualTo(8.3333).Within(.0001));
        await Tick(12.5);
        await Tick(12.5);
        await Tick(90);
        Assert.That(vm.Position.TotalMilliseconds, Is.EqualTo(87.5));
        await Tick(100);
        Assert.That(vm.PlaybackInterval.TotalMilliseconds, Is.EqualTo(2.0833).Within(.0001));
    }

    [Test]
    public async Task PauseAndResumeRetainMarkerPosition()
    {
        await Load(0, 3, 6);
        await vm.PlayPauseCommand.ExecuteAsync(null);
        audio.Snapshot = audio.Snapshot with { Position = TimeSpan.FromMilliseconds(3.5) };
        await vm.PlayPauseCommand.ExecuteAsync(null);
        await Tick(3.5);
        Assert.That(vm.Position.TotalMilliseconds, Is.EqualTo(3.5));
        await vm.PlayPauseCommand.ExecuteAsync(null);
        await Tick(8.5);
        Assert.That(vm.Position.TotalMilliseconds, Is.EqualTo(8.3333).Within(.0001));
    }

    [Test]
    public async Task SeekingResetsMarkerWithoutMovingBackwards()
    {
        await Load(0, 5, 10, 11, 50);
        await vm.PlayPauseCommand.ExecuteAsync(null);
        await vm.SeekCommand.ExecuteAsync(TimeSpan.FromMilliseconds(10));
        Assert.That(vm.Position.TotalMilliseconds, Is.EqualTo(10));
        await Tick(11);
        Assert.That(vm.Position.TotalMilliseconds, Is.EqualTo(10));
        await Tick(12.5);
        await vm.PlayPauseCommand.ExecuteAsync(null);
        await vm.SeekCommand.ExecuteAsync(TimeSpan.FromMilliseconds(5));
        await vm.PlayPauseCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        await vm.PlayPauseCommand.ExecuteAsync(null);
    }

    [Test]
    public async Task RemovalAndVolumeRefreshPublishUpdatedSchedule()
    {
        await Load(0, 10, 20);
        await vm.PlayPauseCommand.ExecuteAsync(null);
        var track = vm.Tracks[0];
        vm.RemoveSample(track, track.Samples[1]);
        vm.SetTrackVolume(track, 50);
        await vm.FlushPlaybackUpdatesAsync();
        await Tick(25);
        Assert.That(audio.SampleUpdates[^1].Select(x => x.Timestamp), Is.EqualTo(new[]{0,20}));
        Assert.That(audio.SampleUpdates[^1].Select(x => x.TrackVolume), Is.All.EqualTo(50));
    }

    [Test]
    public async Task NaturalEndShowsEndpointAndDeactivationStopsRefresh()
    {
        await Load(9999);
        await vm.PlayPauseCommand.ExecuteAsync(null);
        audio.Snapshot = new AudioPlaybackSnapshot(TimeSpan.FromSeconds(10), false, null);
        await StubWorkstationAudioService.WaitUntilAsync(() => !playback.Latest.Playback.IsPlaying);
        vm.RefreshPlayback();
        Assert.That(vm.IsPlaying, Is.False);
        await vm.DeactivateAsync();
        await Tick(10);
        Assert.That(vm.Position, Is.EqualTo(TimeSpan.FromSeconds(10)));
    }

    [Test]
    public async Task EditsPublishPastAndFuturePlacements()
    {
        await Load(0);
        await vm.PlayPauseCommand.ExecuteAsync(null);
        await Tick(25);
        vm.Tracks[0].Samples.Add(new Sample { FileName = "hit.wav", Timestamp = 10 });
        vm.Tracks[0].Samples.Add(new Sample { FileName = "hit.wav", Timestamp = 50 });
        vm.SetTrackVolume(vm.Tracks[0], 50);
        await vm.FlushPlaybackUpdatesAsync();
        await Tick(75);
        Assert.That(audio.SampleUpdates[^1].Select(x => x.Timestamp), Is.EquivalentTo(new[]{0,10,50}));
    }

    [Test]
    public async Task DeviceErrorIsPresentedAndRestartResetsMarker()
    {
        await Load(0, 10);
        await vm.PlayPauseCommand.ExecuteAsync(null);
        await Tick(25);
        audio.Snapshot = new AudioPlaybackSnapshot(TimeSpan.Zero, false, "device lost");
        await StubWorkstationAudioService.WaitUntilAsync(() => vm.ErrorMessage == "device lost");
        vm.RefreshPlayback();
        Assert.That(vm.ErrorMessage, Is.EqualTo("device lost"));
        await vm.PlayPauseCommand.ExecuteAsync(null);
    }

    [Test]
    public async Task InvalidTimingPreventsPlaybackWithActionableError()
    {
        vm = new WorkstationPageVM(new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", new ProjectData()))
        }, playback = new Hitbench.Services.MarkerPlaybackCoordinator(audio, (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration);
        await vm.PlayPauseCommand.ExecuteAsync(null);
        Assert.That(vm.IsPlaying, Is.False);
        Assert.That(vm.ErrorMessage, Does.Contain("timing"));
    }
}
