using osuHitsounder.Core.Entities;
using osuHitsounder.ViewModels.Shell;

namespace osuHitsounder.Test.Presentation;

public class WorkstationPageVMTests
{
    [Test]
    public async Task ZoomButtonsUseEightToThirtyTwoBounds()
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new StubWorkstationAudioService(), new ImmediateUiScheduler());
        Assert.That(vm.ZoomFactor, Is.EqualTo(8));
        await vm.LoadAsync("project.json", CancellationToken.None);
        Assert.That(vm.ZoomOutCommand.CanExecute(null), Is.False);
        vm.ZoomInCommand.Execute(null);
        Assert.That(vm.ZoomFactor, Is.EqualTo(9));
        for (var index = 0; index < 40; index++) vm.ZoomInCommand.Execute(null);
        Assert.That(vm.ZoomFactor, Is.EqualTo(32));
        Assert.That(vm.ZoomInCommand.CanExecute(null), Is.False);
        for (var index = 0; index < 40; index++) vm.ZoomOutCommand.Execute(null);
        Assert.That(vm.ZoomFactor, Is.EqualTo(8));
        Assert.That(vm.ZoomOutCommand.CanExecute(null), Is.False);
    }

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
            Assert.That(vm.Project, Is.Not.Null);
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
    public async Task SubdivisionStepsClampWithoutAffectingAudio()
    {
        var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), audio, new ImmediateUiScheduler());
        await vm.LoadAsync("project.json", CancellationToken.None);
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
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new StubWorkstationAudioService(), new ImmediateUiScheduler());
        Assert.That(vm.ZoomInCommand.CanExecute(null), Is.False);
        Assert.That(vm.ZoomOutCommand.CanExecute(null), Is.False);
        await vm.LoadAsync("project.json", CancellationToken.None);
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
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), audio, new ImmediateUiScheduler());
        Assert.That(vm.SeekCommand.CanExecute(TimeSpan.FromSeconds(3)), Is.False);
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.PlayPauseCommand.Execute(null);
        vm.SeekCommand.Execute(TimeSpan.FromSeconds(3.4));
        Assert.Multiple(() =>
        {
            Assert.That(vm.Position, Is.EqualTo(TimeSpan.FromSeconds(3.4)));
            Assert.That(vm.ElapsedText, Is.EqualTo("00:03.4"));
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
        var vm = new WorkstationPageVM(source, new StubWorkstationAudioService(), new ImmediateUiScheduler());
        await vm.LoadAsync("project.json", CancellationToken.None);
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
        var vm = new WorkstationPageVM(source, new StubWorkstationAudioService(), new ImmediateUiScheduler());

        await vm.LoadAsync("missing.json", CancellationToken.None);

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
        var vm = new WorkstationPageVM(source, audio, new ImmediateUiScheduler());
        var load = vm.LoadAsync("project.json", CancellationToken.None);
        var deactivate = vm.DeactivateAsync();
        completion.SetResult(new WorkstationSource("map.osu", "audio.wav", new ProjectData()));
        await Task.WhenAll(load, deactivate);
        Assert.That(vm.Waveform, Is.Null);
        Assert.That(vm.Project, Is.Null);
        Assert.That(audio.Disposed, Is.True);
    }
}
