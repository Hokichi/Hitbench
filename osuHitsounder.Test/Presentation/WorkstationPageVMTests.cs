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
    public async Task ZoomUsesIntegerBoundsSupportsDraftsAndFitResets()
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new StubWorkstationAudioService(), new ImmediateUiScheduler());
        await vm.LoadAsync("project.json", CancellationToken.None);

        vm.ZoomInCommand.Execute(null);
        vm.ZoomInCommand.Execute(null);
        Assert.That(vm.ZoomFactor, Is.EqualTo(3));

        vm.ZoomDraft = "7";
        vm.CommitZoomCommand.Execute(null);
        Assert.That(vm.ZoomFactor, Is.EqualTo(7));

        vm.ZoomDraft = "2.5";
        vm.CommitZoomCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(vm.ZoomFactor, Is.EqualTo(7));
            Assert.That(vm.HasZoomValidationError, Is.True);
        });

        vm.CancelZoomEditCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(vm.ZoomDraft, Is.EqualTo("7"));
            Assert.That(vm.HasZoomValidationError, Is.False);
        });

        for (var index = 0; index < 20; index++) vm.ZoomInCommand.Execute(null);
        Assert.That(vm.ZoomFactor, Is.EqualTo(16));
        Assert.That(vm.ZoomInCommand.CanExecute(null), Is.False);
        vm.ZoomOutCommand.Execute(null);
        Assert.That(vm.ZoomFactor, Is.EqualTo(15));
        for (var index = 0; index < 20; index++) vm.ZoomOutCommand.Execute(null);
        Assert.That(vm.ZoomFactor, Is.EqualTo(1));
        vm.FitCommand.Execute(null);
        Assert.That(vm.ZoomFactor, Is.EqualTo(1));
    }

    [TestCase("")]
    [TestCase("abc")]
    [TestCase("0")]
    [TestCase("17")]
    [TestCase("1.5")]
    [TestCase("999999999999999999999999")]
    public async Task InvalidZoomDraftPreservesCommittedZoom(string draft)
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new StubWorkstationAudioService(), new ImmediateUiScheduler());
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.ZoomInCommand.Execute(null);

        vm.ZoomDraft = draft;
        vm.CommitZoomCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(vm.ZoomFactor, Is.EqualTo(2));
            Assert.That(vm.ZoomDraft, Is.EqualTo(draft));
            Assert.That(vm.ZoomValidationMessage, Is.EqualTo("Enter a whole number from 1 to 16."));
        });

        vm.ZoomInCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(vm.ZoomFactor, Is.EqualTo(3));
            Assert.That(vm.ZoomDraft, Is.EqualTo("3"));
            Assert.That(vm.HasZoomValidationError, Is.False);
        });
    }

    [TestCase("1", 1)]
    [TestCase("16", 16)]
    [TestCase(" 8 ", 8)]
    public async Task ValidZoomDraftCommitsWholeNumber(string draft, int expected)
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new StubWorkstationAudioService(), new ImmediateUiScheduler());
        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.ZoomDraft = draft;

        vm.CommitZoomCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(vm.ZoomFactor, Is.EqualTo(expected));
            Assert.That(vm.ZoomDraft, Is.EqualTo(expected.ToString(System.Globalization.CultureInfo.CurrentCulture)));
            Assert.That(vm.HasZoomValidationError, Is.False);
        });
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
    public async Task UnavailableZoomRejectsCommitAndDeactivationClearsDraftError()
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new StubWorkstationAudioService(), new ImmediateUiScheduler());
        vm.ZoomDraft = "8";
        Assert.That(vm.CommitZoomCommand.CanExecute(null), Is.False);
        vm.CommitZoomCommand.Execute(null);
        Assert.That(vm.ZoomFactor, Is.EqualTo(1));

        await vm.LoadAsync("project.json", CancellationToken.None);
        vm.ZoomDraft = "bad";
        vm.CommitZoomCommand.Execute(null);
        Assert.That(vm.HasZoomValidationError, Is.True);

        await vm.DeactivateAsync();
        Assert.Multiple(() =>
        {
            Assert.That(vm.CanZoom, Is.False);
            Assert.That(vm.ZoomDraft, Is.EqualTo("1"));
            Assert.That(vm.HasZoomValidationError, Is.False);
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
