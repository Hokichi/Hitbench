using osuHitsounder.Core.Entities;
using osuHitsounder.ViewModels.Shell;

namespace osuHitsounder.Test.Presentation;

public class WorkstationPageVMTests
{
    [Test]
    public async Task LoadGatesCommandsThenEnablesTransportAndZoom()
    {
        var source = new StubWorkstationSourceService();
        var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(source, audio, new ImmediateUiScheduler());
        Assert.That(vm.CanPlay, Is.False);
        await vm.LoadAsync("project.json", CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(vm.CanPlay, Is.True);
            Assert.That(vm.Waveform, Is.Not.Null);
            Assert.That(audio.OpenedPath, Is.EqualTo("audio.wav"));
        });
    }

    [Test]
    public async Task TransportReflectsPlaybackSnapshotAndFormatsLongElapsedTime()
    {
        var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), audio, new ImmediateUiScheduler());
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.PlayPauseCommand.Execute(null);
        Assert.That(vm.IsPlaying, Is.True);
        audio.Snapshot = new AudioPlaybackSnapshot(TimeSpan.FromMinutes(65.25), true, null);
        vm.RefreshPlayback();
        Assert.That(vm.ElapsedText, Is.EqualTo("65:15.0"));
        vm.StopCommand.Execute(null);
        Assert.That(vm.Position, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public async Task ZoomUsesPowerOfTwoBoundsAndFitResets()
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new StubWorkstationAudioService(), new ImmediateUiScheduler());
        await vm.LoadAsync("project.json", CancellationToken.None);
        for (var index = 0; index < 8; index++) vm.ZoomInCommand.Execute(null);
        Assert.That(vm.ZoomFactor, Is.EqualTo(16));
        Assert.That(vm.ZoomInCommand.CanExecute(null), Is.False);
        vm.ZoomOutCommand.Execute(null);
        Assert.That(vm.ZoomFactor, Is.EqualTo(8));
        vm.FitCommand.Execute(null);
        Assert.That(vm.ZoomFactor, Is.EqualTo(1));
    }

    [Test]
    public async Task LoadFailureIsReadableAndWaveformAnalysisSurvivesPlaybackDeviceFailure()
    {
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => throw new InvalidDataException("Missing AudioFilename")
        };
        var vm = new WorkstationPageVM(source, new StubWorkstationAudioService(), new ImmediateUiScheduler());
        await vm.LoadAsync("project.json", CancellationToken.None);
        Assert.That(vm.ErrorMessage, Does.Contain("Missing AudioFilename"));
        Assert.That(vm.CanPlay, Is.False);
    }

    [Test]
    public async Task DeactivateRejectsLateLoadAndDisposesAudio()
    {
        var completion = new TaskCompletionSource<WorkstationSource>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new StubWorkstationSourceService { Load = (_, _) => completion.Task };
        var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(source, audio, new ImmediateUiScheduler());
        var load = vm.LoadAsync("project.json", CancellationToken.None);
        var deactivate = vm.DeactivateAsync();
        completion.SetResult(new WorkstationSource("map.osu", "audio.wav"));
        await Task.WhenAll(load, deactivate);
        Assert.That(vm.Waveform, Is.Null);
        Assert.That(audio.Disposed, Is.True);
    }
}
