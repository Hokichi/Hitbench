using Hitbench.Core.Entities;
using Hitbench.Resources.DataModels;
using Hitbench.Services;
using Hitbench.Services.FileSystem;
using Hitbench.ViewModels.Shell;

namespace Hitbench.Test.Presentation;

public class WorkstationSkinDefaultsTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task ProjectLoadsWhenSkinLoadingIsSkipped(bool allowDefaults)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var project = new ProjectData
        {
            AllowDefaults = allowDefaults, CurrentSkin = "Old", Offsets = [0], BPMs = [120],
            Tracks = [new Track { Name = "Hits", SelectedSampleFileName = "hit.wav", Samples = [new Sample { FileName = "hit.wav", Timestamp = 500 }] }],
            ControlPoints = [new ControlPoint { Timestamp = 0, Volume = 80 }]
        };
        var settings = new StubAppSettingsService
        {
            Load = _ => allowDefaults
                ? Task.FromResult(new AppSettings { OsuPath = Path.Combine(root, "missing-osu") })
                : throw new InvalidOperationException("Disabled defaults must not read settings.")
        };
        var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(
            new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav", project)) },
            new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler(),
            sampleFiles: new StubSampleFileService(["hit.wav"]), skinDefaults: new OHsSkinDefaultsService(settings));
        try
        {
            await vm.LoadAsync(Path.Combine(root, "project.json"), default);
            vm.AcknowledgePresentationReady(vm.LoadGeneration);

            Assert.That(vm.LoadPhase, Is.EqualTo(ProjectLoadPhase.Ready));
            Assert.That(vm.ErrorMessage, Is.Null);
            Assert.That(vm.Project, Is.SameAs(project));
            Assert.That(vm.Waveform, Is.Not.Null);
            Assert.That(audio.OpenedPath, Is.EqualTo("audio.wav"));
            Assert.That(vm.Tracks.Single().Name, Is.EqualTo("Hits"));
            Assert.That(vm.Tracks.Single().Samples.Single().Timestamp, Is.EqualTo(500));
            Assert.That(vm.ControlPointCount, Is.EqualTo(1));
            Assert.That(project.ControlPoints.Single().Volume, Is.EqualTo(80));
            Assert.That(vm.AllowDefaults, Is.EqualTo(allowDefaults));
            Assert.That(project.CurrentSkin, Is.EqualTo("Old"));
        }
        finally { await vm.DeactivateAsync(); Directory.Delete(root, true); }
    }

    [Test]
    public async Task ReplacementLoadSettlesCancelledRefreshBeforeLoadingAgain()
    {
        var gate = new TaskCompletionSource<OHsSkinDefaultsSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var project = new ProjectData { Offsets = [0], BPMs = [120] };
        var service = new StubOHsSkinDefaultsService { Refresh = (_, old, allow, _, _) =>
        {
            if (!allow) return Task.FromResult(new OHsSkinDefaultsSnapshot(old, [], false));
            entered.TrySetResult(); return gate.Task;
        } };
        var vm = Create(project, service);
        try
        {
            await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            var change = vm.SetAllowDefaultsAsync(true); await entered.Task;
            var next = vm.LoadAsync("replacement.json", default);
            Assert.That(next.IsCompleted, Is.False);
            gate.SetResult(new("stale", ["SkinDefault/normal-hitnormal.wav"], true));
            await change; await next;
            Assert.That(project.AllowDefaults, Is.False); Assert.That(project.CurrentSkin, Is.Not.EqualTo("stale"));
            Assert.That(vm.IsRefreshingSkinDefaults, Is.False);
        }
        finally { gate.TrySetResult(new("", [], false)); await vm.DeactivateAsync(); }
    }
    [Test]
    public async Task RefreshWaitsForDrawerAudioAndForcesDecodeOfSamePath()
    {
        var prepared = false; var audio = new StubWorkstationAudioService();
        var project = new ProjectData { Offsets = [0], BPMs = [120] };
        var manager = new StubSampleManagementDialogService { PrepareSkin = (_, _) => { prepared = true; return Task.CompletedTask; } };
        var skin = new StubOHsSkinDefaultsService { Refresh = (_, old, allow, _, _) =>
        {
            if (allow) Assert.That(prepared, Is.True);
            return Task.FromResult(new OHsSkinDefaultsSnapshot(old, [], allow));
        } };
        var vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", project)) },
            new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler(), sampleManagementDialog: manager, skinDefaults: skin);
        try
        {
            await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            await vm.SetAllowDefaultsAsync(true);
            Assert.That(project.AllowDefaults, Is.True); Assert.That(audio.ForceReloadSampleRequests, Is.EqualTo(1));
        }
        finally { await vm.DeactivateAsync(); }
    }
    internal static WorkstationPageVM Create(ProjectData project, StubOHsSkinDefaultsService skin) => new(
        new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", project)) },
        new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(),
        sampleFiles: new StubSampleFileService(["a.wav"]), skinDefaults: skin);

    [Test]
    public async Task EachLoadChecksSkinAndEnableDisablePersists()
    {
        var calls = 0;
        var project = new ProjectData { Offsets = [0], BPMs = [120] };
        var skin = new StubOHsSkinDefaultsService { Refresh = (_, old, allow, _, _) =>
        {
            calls++;
            return Task.FromResult(new OHsSkinDefaultsSnapshot(allow ? "New" : old, ["SkinDefault/normal-hitnormal.wav"], allow));
        } };
        var vm = Create(project, skin);
        try
        {
            await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            Assert.That(calls, Is.EqualTo(1));
            await vm.SetAllowDefaultsAsync(true);
            Assert.That(project.AllowDefaults, Is.True); Assert.That(project.CurrentSkin, Is.EqualTo("New"));
            await vm.SetAllowDefaultsAsync(false);
            Assert.That(project.AllowDefaults, Is.False); Assert.That(vm.SkinDefaultFiles, Has.Count.EqualTo(1));
            Assert.That(vm.IsRefreshingSkinDefaults, Is.False);
        }
        finally { await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task FailedEnableRetainsCommittedState()
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120] };
        var skin = new StubOHsSkinDefaultsService(); var vm = Create(project, skin);
        try
        {
            await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            skin.Refresh = (_, _, _, _, _) => throw new IOException("failed");
            await vm.SetAllowDefaultsAsync(true);
            Assert.That(project.AllowDefaults, Is.False); Assert.That(vm.IsRefreshingSkinDefaults, Is.False);
            Assert.That(vm.ErrorMessage, Is.EqualTo("failed"));
        }
        finally { await vm.DeactivateAsync(); }
    }
}
