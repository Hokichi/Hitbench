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
    public async Task StartCrossedRangeAndDuplicateTicksTriggerEachPlacementOnce()
    {
        await Load(0, 10, 10, 31);
        vm.PlayPauseCommand.Execute(null);
        Assert.That(audio.TriggerBatches.SelectMany(batch => batch), Is.EqualTo(new long[] { 1 }));
        Tick(10);
        Assert.That(vm.Position.TotalMilliseconds, Is.EqualTo(8.3333).Within(.0001));
        Tick(12.5);
        Tick(12.5);
        Tick(90);
        Assert.That(audio.TriggerBatches.SelectMany(batch => batch), Is.EqualTo(new long[] { 1, 2, 3, 4 }));
        Tick(100);
        Assert.That(vm.PlaybackInterval.TotalMilliseconds, Is.EqualTo(2.0833).Within(.0001));
    }

    [Test]
    public async Task PauseBetweenTicksRetainsPendingRangeWithoutDuplicateStart()
    {
        await Load(0, 3, 6);
        vm.PlayPauseCommand.Execute(null);
        audio.Snapshot = audio.Snapshot with { Position = TimeSpan.FromMilliseconds(3.5) };
        vm.PlayPauseCommand.Execute(null);
        Tick(3.5);
        Assert.That(audio.TriggerBatches.SelectMany(batch => batch), Is.EqualTo(new long[] { 1 }));
        vm.PlayPauseCommand.Execute(null);
        Tick(8.5);
        Assert.That(audio.TriggerBatches.SelectMany(batch => batch), Is.EqualTo(new long[] { 1, 2, 3 }));
    }

    [Test]
    public async Task SeekingResetsHistoryWithoutPlayingSkippedSamplesOrMovingBackwards()
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
        Assert.That(audio.TriggerBatches.SelectMany(batch => batch), Is.EqualTo(new long[] { 1, 3, 4 }));
        vm.PlayPauseCommand.Execute(null);
        Assert.That(audio.TriggerBatches.SelectMany(batch => batch), Is.EqualTo(new long[] { 1, 3, 4, 2 }));
        vm.StopCommand.Execute(null);
        vm.PlayPauseCommand.Execute(null);
        Assert.That(audio.TriggerBatches.SelectMany(batch => batch).Last(), Is.EqualTo(1));
    }

    [Test]
    public async Task RemovalAndVolumeRefreshDoNotReplayPlacements()
    {
        await Load(0, 10, 20);
        vm.PlayPauseCommand.Execute(null);
        var track = vm.Tracks[0];
        vm.RemoveSample(track, track.Samples[1]);
        vm.SetTrackVolume(track, 50);
        await vm.FlushPlaybackUpdatesAsync();
        Tick(25);
        Assert.That(audio.TriggerBatches.SelectMany(batch => batch), Is.EqualTo(new long[] { 1, 3 }));
    }

    [Test]
    public async Task NaturalEndConsumesFinalPartialIntervalAndDeactivationStopsTriggers()
    {
        await Load(9999);
        vm.PlayPauseCommand.Execute(null);
        audio.Snapshot = new AudioPlaybackSnapshot(TimeSpan.FromSeconds(10), false, null);
        vm.RefreshPlayback();
        Assert.That(audio.TriggerBatches.SelectMany(batch => batch), Is.EqualTo(new long[] { 1 }));
        Assert.That(vm.IsPlaying, Is.False);
        await vm.DeactivateAsync();
        Tick(10);
        Assert.That(audio.TriggerBatches.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task FutureEditsParticipateButPastEditsDoNotReplay()
    {
        await Load(0);
        vm.PlayPauseCommand.Execute(null);
        Tick(25);
        vm.Tracks[0].Samples.Add(new Sample { FileName = "hit.wav", Timestamp = 10 });
        vm.Tracks[0].Samples.Add(new Sample { FileName = "hit.wav", Timestamp = 50 });
        vm.SetTrackVolume(vm.Tracks[0], 50);
        await vm.FlushPlaybackUpdatesAsync();
        Tick(75);
        Assert.That(audio.TriggerBatches.SelectMany(batch => batch), Is.EqualTo(new long[] { 1, 3 }));
    }

    [Test]
    public async Task DeviceErrorDoesNotCatchUpAndRestartCanTriggerFromBeginning()
    {
        await Load(0, 10);
        vm.PlayPauseCommand.Execute(null);
        Tick(25);
        audio.Snapshot = new AudioPlaybackSnapshot(TimeSpan.Zero, false, "device lost");
        vm.RefreshPlayback();
        Assert.That(vm.ErrorMessage, Is.EqualTo("device lost"));
        Assert.That(audio.TriggerBatches.SelectMany(batch => batch), Is.EqualTo(new long[] { 1, 2 }));
        vm.PlayPauseCommand.Execute(null);
        Assert.That(audio.TriggerBatches.SelectMany(batch => batch), Is.EqualTo(new long[] { 1, 2, 1 }));
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
