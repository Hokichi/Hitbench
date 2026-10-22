using Soundie.Core.Entities;
using Soundie.Services;
using Soundie.ViewModels.Shell;

namespace Soundie.Test.Presentation;

public sealed class WorkstationHeaderStateTests
{
    [Test]
    public async Task ReturnWaitsForAppliedMasterVolume()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saved = 0;
        var settings = new StubAppSettingsService { SaveVolume = async (value, _) => { await release.Task; saved = value; } };
        var volumes = new PlaybackVolumeSettings(settings, 27);
        var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(),
            new MarkerPlaybackCoordinator(audio, MarkerPlaybackCoordinator.DelayAsync), new ImmediateUiScheduler(), playbackVolumes: volumes);
        try
        {
            await vm.LoadAsync("project.json", default);
            vm.AcknowledgePresentationReady(vm.LoadGeneration);
            Assert.That(audio.ApplicationVolume, Is.EqualTo(27));
            vm.ApplicationVolume = 64;
            Assert.That(audio.ApplicationVolume, Is.EqualTo(64));
            Assert.That(vm.HasPendingSaves, Is.True);
            var returning = vm.PrepareToReturnAsync(default);
            Assert.That(returning.IsCompleted, Is.False);
            release.SetResult();
            Assert.That(await returning, Is.True);
            Assert.That(saved, Is.EqualTo(64));
        }
        finally { release.TrySetResult(); await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task FailedMasterVolumeSaveKeepsReturnOpen()
    {
        var settings = new StubAppSettingsService { SaveVolume = (_, _) => Task.FromException(new IOException("Disk unavailable")) };
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(),
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), MarkerPlaybackCoordinator.DelayAsync), new ImmediateUiScheduler(),
            playbackVolumes: new PlaybackVolumeSettings(settings));
        try
        {
            await vm.LoadAsync("project.json", default);
            vm.AcknowledgePresentationReady(vm.LoadGeneration);
            vm.ApplicationVolume = 45;
            Assert.That(await vm.PrepareToReturnAsync(default), Is.False);
            Assert.That(vm.IsReturning, Is.False);
            Assert.That(vm.HasPendingSaves, Is.True);
            settings.SaveVolume = (_, _) => Task.CompletedTask;
            Assert.That(await vm.PrepareToReturnAsync(default), Is.True);
        }
        finally { settings.SaveVolume = (_, _) => Task.CompletedTask; await vm.DeactivateAsync(); }
    }
    [Test]
    public async Task CollapsedRowsUseCommonMeasuredMinimum()
    {
        var sample = new Track { IsCollapsed = true };
        var instrument = new InstrumentalTrack { IsCollapsed = true };
        var project = new ProjectData { Tracks = [sample, instrument] };
        var vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", project)) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), MarkerPlaybackCoordinator.DelayAsync), new ImmediateUiScheduler());
        try
        {
            await vm.LoadAsync("project.json", default);
            vm.SetTrackMinimumExtent(sample, true, 51);
            vm.SetTrackMinimumExtent(instrument, true, 47);
            Assert.That(vm.GetTrackExtent(instrument), Is.EqualTo(vm.GetTrackExtent(sample)));
            Assert.That(vm.GetTrackExtent(sample), Is.GreaterThanOrEqualTo(51));
        }
        finally { await vm.DeactivateAsync(); }
    }
    [Test]
    public async Task HeaderFollowsProjectAndPlaybackTiming()
    {
        var project = new ProjectData { Offsets = [100, 1000, 1000], BPMs = [120, 150, 180],
            ControlPoints = [new() { Timestamp = 0 }, new() { Timestamp = 0, IsDisabled = true }] };
        var vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", project)) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), MarkerPlaybackCoordinator.DelayAsync), new ImmediateUiScheduler());
        try
        {
            await vm.LoadAsync(Path.Combine(Path.GetTempPath(), "Project name", "project.json"), default);
            Assert.That(vm.ProjectName, Is.EqualTo("Project name"));
            Assert.That(vm.ControlPointCount, Is.EqualTo(2));
            vm.Position = TimeSpan.Zero;
            Assert.That(vm.CurrentBpmText, Is.EqualTo("120 BPM"));
            vm.Position = TimeSpan.FromMilliseconds(1000);
            Assert.That(vm.CurrentBpmText, Is.EqualTo("180 BPM"));
        }
        finally { await vm.DeactivateAsync(); }
    }
}
