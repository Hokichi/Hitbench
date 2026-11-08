using Hitbench.Core.Entities;
using Hitbench.Resources.DataModels;
using Hitbench.Services;
using Hitbench.ViewModels.Shell;

namespace Hitbench.Test.Presentation;

public class WorkstationSkinDefaultsTests
{
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
