using Soundie.Core.Entities;
using Soundie.ViewModels.Shell;
using NAudio.Wave;
using Soundie.Services;
using Soundie.Test.Services;

namespace Soundie.Test.Presentation;

public class WorkstationPageVMTests
{
    [Test]
    public async Task MasterVolumesPersistAcrossWorkstationsAndClampWithoutChangingProjectMix()
    {
        var settings = new PlaybackVolumeSettings();
        var firstAudio = new StubWorkstationAudioService();
        var first = new WorkstationPageVM(new StubWorkstationSourceService(), new Soundie.Services.MarkerPlaybackCoordinator(firstAudio, (duration, token) => Task.Delay(duration, token)),
            new ImmediateUiScheduler(), playbackVolumes: settings);
        await first.LoadAsync("project.json", CancellationToken.None);
        first.AcknowledgePresentationReady(first.LoadGeneration);
        var trackVolumes = first.Tracks.Select(track => track.Volume).ToArray();

        first.ApplicationVolume = 140;
        first.SamplesVolume = -20;
        first.ApplicationVolume = 40;
        first.SamplesVolume = 60;
        await first.DeactivateAsync();

        var secondAudio = new StubWorkstationAudioService();
        var second = new WorkstationPageVM(new StubWorkstationSourceService(), new Soundie.Services.MarkerPlaybackCoordinator(secondAudio, (duration, token) => Task.Delay(duration, token)),
            new ImmediateUiScheduler(), playbackVolumes: settings);
        await second.LoadAsync("project.json", CancellationToken.None);
        second.AcknowledgePresentationReady(second.LoadGeneration);

        Assert.Multiple(() =>
        {
            Assert.That(second.ApplicationVolume, Is.EqualTo(40));
            Assert.That(second.SamplesVolume, Is.EqualTo(60));
            Assert.That(secondAudio.ApplicationVolume, Is.EqualTo(40));
            Assert.That(secondAudio.SamplesVolume, Is.EqualTo(60));
            Assert.That(first.Tracks.Select(track => track.Volume), Is.EqualTo(trackVolumes));
            Assert.That(settings.ApplicationVolume, Is.EqualTo(40));
            Assert.That(settings.SamplesVolume, Is.EqualTo(60));
        });
        await second.DeactivateAsync();
    }

    [Test]
    public async Task WholeBeatCommandsAdvanceAcrossFrameRoundingAndTimingChange()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(100, 1);
        using var audio = new WorkstationAudioService(
            _ => new RawSourceWaveStream(new MemoryStream(new byte[format.BlockAlign * 1000]), format),
            () => new StubWavePlayer());
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav",
                new ProjectData { Offsets = [0, 800], BPMs = [120, 60] }))
        };
        var vm = new WorkstationPageVM(source, new Soundie.Services.MarkerPlaybackCoordinator(audio, (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration);
        Assert.That(vm.Subdivision, Is.EqualTo(4));
        await vm.NextWholeBeatCommand.ExecuteAsync(null);
        Assert.That(vm.Position, Is.EqualTo(TimeSpan.FromMilliseconds(500)));
        await vm.NextWholeBeatCommand.ExecuteAsync(null);
        Assert.That(vm.Position, Is.EqualTo(TimeSpan.FromMilliseconds(800)));
        await vm.NextWholeBeatCommand.ExecuteAsync(null);
        Assert.That(vm.Position, Is.EqualTo(TimeSpan.FromMilliseconds(1800)));
        await vm.PreviousWholeBeatCommand.ExecuteAsync(null);
        Assert.That(vm.Position, Is.EqualTo(TimeSpan.FromMilliseconds(800)));
        await vm.NextWholeBeatCommand.ExecuteAsync(null);
        Assert.That(vm.Position, Is.EqualTo(TimeSpan.FromMilliseconds(1800)));
        Assert.That(vm.Subdivision, Is.EqualTo(4));
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task WholeBeatCommandsAdvanceAfterAudioFrameQuantization()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(100, 1);
        using var audio = new WorkstationAudioService(
            _ => new RawSourceWaveStream(new MemoryStream(new byte[format.BlockAlign * 1000]), format),
            () => new StubWavePlayer());
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav",
                new ProjectData { Offsets = [0], BPMs = [137] }))
        };
        var vm = new WorkstationPageVM(source, new Soundie.Services.MarkerPlaybackCoordinator(audio, (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration);
        await vm.NextWholeBeatCommand.ExecuteAsync(null);
        var firstBeat = TimeSpan.FromSeconds(19_314d / 44_100);
        var secondBeat = TimeSpan.FromSeconds(38_628d / 44_100);
        Assert.That(vm.Position, Is.EqualTo(firstBeat).Within(TimeSpan.FromTicks(1)));
        await vm.NextWholeBeatCommand.ExecuteAsync(null);
        Assert.That(vm.Position, Is.EqualTo(secondBeat).Within(TimeSpan.FromTicks(1)));
        await vm.PreviousWholeBeatCommand.ExecuteAsync(null);
        Assert.That(vm.Position, Is.EqualTo(firstBeat).Within(TimeSpan.FromTicks(1)));
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task RepeatedRightNavigationAdvancesAfterAudioFrameQuantization()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(100, 1);
        using var audio = new WorkstationAudioService(
            _ => new RawSourceWaveStream(new MemoryStream(new byte[format.BlockAlign * 1000]), format),
            () => new StubWavePlayer());
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav",
                new ProjectData { Offsets = [0], BPMs = [120] }))
        };
        var vm = new WorkstationPageVM(source, new Soundie.Services.MarkerPlaybackCoordinator(audio, (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration);
        await vm.NextGridLineCommand.ExecuteAsync(null);
        var first = vm.Position;
        await vm.NextGridLineCommand.ExecuteAsync(null);
        Assert.That(first, Is.EqualTo(TimeSpan.FromSeconds(5_513d / 44_100)).Within(TimeSpan.FromTicks(1)));
        Assert.That(vm.Position, Is.GreaterThan(first));
        Assert.That(vm.Position, Is.EqualTo(TimeSpan.FromMilliseconds(250)));
        await vm.PreviousGridLineCommand.ExecuteAsync(null);
        Assert.That(vm.Position, Is.EqualTo(first));
        await vm.NextGridLineCommand.ExecuteAsync(null);
        Assert.That(vm.Position, Is.EqualTo(TimeSpan.FromMilliseconds(250)));
        await vm.DeactivateAsync();
    }
    [Test]
    public async Task GridCommandsSeekFromCurrentAudioPositionAndPreservePlayback()
    {
        var audio = new StubWorkstationAudioService
        {
            Snapshot = new AudioPlaybackSnapshot(TimeSpan.FromMilliseconds(200), true, null)
        };
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav",
                new ProjectData { Offsets = [0], BPMs = [120] }))
        };
        var vm = new WorkstationPageVM(source, new Soundie.Services.MarkerPlaybackCoordinator(audio, (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration);
        await vm.PlayPauseCommand.ExecuteAsync(null);
        audio.Snapshot = audio.Snapshot with { Position = TimeSpan.FromMilliseconds(200) };
        await vm.NextGridLineCommand.ExecuteAsync(null);
        Assert.That(audio.Snapshot.Position, Is.EqualTo(TimeSpan.FromMilliseconds(250)));
        Assert.That(audio.Snapshot.IsPlaying, Is.True);
        await vm.PreviousGridLineCommand.ExecuteAsync(null);
        Assert.That(audio.Snapshot.Position, Is.EqualTo(TimeSpan.FromMilliseconds(125)));
        await vm.DeactivateAsync();
    }
    [Test]
    public async Task ZoomButtonsUseEightToOneHundredTwentyEightBounds()
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new Soundie.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        Assert.That(vm.ZoomFactor, Is.EqualTo(32));
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration);
        Assert.That(vm.ZoomOutCommand.CanExecute(null), Is.True);
        vm.ZoomInCommand.Execute(null);
        Assert.That(vm.ZoomFactor, Is.EqualTo(33));
        for (var index = 0; index < 140; index++) vm.ZoomInCommand.Execute(null);
        Assert.That(vm.ZoomFactor, Is.EqualTo(128));
        Assert.That(vm.ZoomInCommand.CanExecute(null), Is.False);
        for (var index = 0; index < 140; index++) vm.ZoomOutCommand.Execute(null);
        Assert.That(vm.ZoomFactor, Is.EqualTo(8));
        Assert.That(vm.ZoomOutCommand.CanExecute(null), Is.False);
    }

    [Test]
    public async Task LoadGatesCommandsThenEnablesTransportAndZoom()
    {
        var source = new StubWorkstationSourceService();
        var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(source, new Soundie.Services.MarkerPlaybackCoordinator(audio, (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        Assert.That(vm.CanPlay, Is.False);
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration);
        Assert.Multiple(() =>
        {
            Assert.That(vm.CanPlay, Is.True);
            Assert.That(vm.Waveform, Is.Not.Null);
            Assert.That(vm.Project, Is.Not.Null);
            Assert.That(audio.OpenedPath, Is.EqualTo("audio.wav"));
        });
    }

    [Test]
    public async Task TransportReflectsPlaybackSnapshotAndFormatsLongElapsedTime()
    {
        var audio = new StubWorkstationAudioService
        {
            Analyze = (_, _) => Task.FromResult(new WaveformData(TimeSpan.FromMinutes(70), [.5f]))
        };
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new Soundie.Services.MarkerPlaybackCoordinator(audio, (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration);
        await vm.PlayPauseCommand.ExecuteAsync(null);
        Assert.That(vm.IsPlaying, Is.True);
        audio.Snapshot = new AudioPlaybackSnapshot(TimeSpan.FromMinutes(65.25), true, null);
        await StubWorkstationAudioService.WaitUntilAsync(() => { vm.RefreshPlayback(); return vm.Position >= audio.Snapshot.Position; });
        vm.RefreshPlayback();
        Assert.That(vm.ElapsedText, Is.EqualTo("65:15.000"));
        await vm.StopCommand.ExecuteAsync(null);
        Assert.That(vm.Position, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public async Task SubdivisionStepsClampWithoutAffectingAudio()
    {
        var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new Soundie.Services.MarkerPlaybackCoordinator(audio, (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration);
        var before = audio.Snapshot;

        Assert.That(vm.SelectedSubdivisionIndex, Is.EqualTo(3));
        for (var index = 0; index < 20; index++) vm.IncreaseSubdivisionCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(vm.SelectedSubdivisionIndex, Is.EqualTo(8));
            Assert.That(vm.IncreaseSubdivisionCommand.CanExecute(null), Is.False);
        });
        for (var index = 0; index < 20; index++) vm.DecreaseSubdivisionCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(vm.SelectedSubdivisionIndex, Is.Zero);
            Assert.That(vm.DecreaseSubdivisionCommand.CanExecute(null), Is.False);
            Assert.That(audio.Snapshot, Is.EqualTo(before));
        });
    }

    [Test]
    public async Task ZoomButtonsAreUnavailableBeforeLoadAndAfterDeactivation()
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new Soundie.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        Assert.That(vm.ZoomInCommand.CanExecute(null), Is.False);
        Assert.That(vm.ZoomOutCommand.CanExecute(null), Is.False);
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration);
        Assert.That(vm.ZoomInCommand.CanExecute(null), Is.True);
        await vm.DeactivateAsync();
        Assert.Multiple(() =>
        {
            Assert.That(vm.CanZoom, Is.False);
            Assert.That(vm.ZoomInCommand.CanExecute(null), Is.False);
            Assert.That(vm.ZoomOutCommand.CanExecute(null), Is.False);
        });
    }

    [Test]
    public async Task SeekUpdatesPositionAndElapsedWithoutChangingPlaybackState()
    {
        var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new Soundie.Services.MarkerPlaybackCoordinator(audio, (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        Assert.That(vm.SeekCommand.CanExecute(TimeSpan.FromSeconds(3)), Is.False);
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration);
        await vm.PlayPauseCommand.ExecuteAsync(null);
        await vm.SeekCommand.ExecuteAsync(TimeSpan.FromSeconds(3.4));
        Assert.Multiple(() =>
        {
            Assert.That(vm.Position, Is.EqualTo(TimeSpan.FromSeconds(3.4)));
            Assert.That(vm.ElapsedText, Is.EqualTo("00:03.400"));
            Assert.That(vm.IsPlaying, Is.True);
        });
    }

    [Test]
    public async Task LoadFailureIsReadableAndWaveformAnalysisSurvivesPlaybackDeviceFailure()
    {
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => throw new InvalidDataException("Missing AudioFilename")
        };
        var vm = new WorkstationPageVM(source, new Soundie.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration);
        Assert.That(vm.ErrorMessage, Does.Contain("Missing AudioFilename"));
        Assert.That(vm.CanPlay, Is.False);
        Assert.That(vm.IsLoading, Is.False);
    }

    [Test]
    public async Task MissingProjectFileReportsFailureAndEndsLoading()
    {
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => throw new FileNotFoundException("Project file was not found")
        };
        var vm = new WorkstationPageVM(source, new Soundie.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());

        await vm.LoadAsync("missing.json", CancellationToken.None);
        vm.AcknowledgePresentationReady(vm.LoadGeneration);

        Assert.Multiple(() =>
        {
            Assert.That(vm.ErrorMessage, Does.Contain("Project file was not found"));
            Assert.That(vm.IsLoading, Is.False);
            Assert.That(vm.CanPlay, Is.False);
        });
    }

    [Test]
    public async Task DeactivateRejectsLateLoadAndDisposesAudio()
    {
        var completion = new TaskCompletionSource<WorkstationSource>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new StubWorkstationSourceService { Load = (_, _) => completion.Task };
        var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(source, new Soundie.Services.MarkerPlaybackCoordinator(audio, (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        var load = vm.LoadAsync("project.json", CancellationToken.None);
        var deactivate = vm.DeactivateAsync();
        completion.SetResult(new WorkstationSource("map.osu", "audio.wav", new ProjectData()));
        await Task.WhenAll(load, deactivate);
        Assert.That(vm.Waveform, Is.Null);
        Assert.That(vm.Project, Is.Null);
        Assert.That(audio.Disposed, Is.True);
    }
}
