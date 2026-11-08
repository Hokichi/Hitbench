using Hitbench.Core.Entities;
using Hitbench.Resources.DataModels;
using Hitbench.Services;
using Hitbench.ViewModels.Shell;

namespace Hitbench.Test.Presentation;

public class WorkstationSkinDefaultsTests
{
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
