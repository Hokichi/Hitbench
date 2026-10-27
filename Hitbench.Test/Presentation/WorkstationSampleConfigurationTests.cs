using Hitbench.Core.Entities;
using Hitbench.Services;
using Hitbench.ViewModels.Shell;

namespace Hitbench.Test.Presentation;

public sealed class WorkstationSampleConfigurationTests
{
    internal static async Task<WorkstationPageVM> CreateAsync(params Track[] tracks)
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(
            new WorkstationSource("map", "song", new ProjectData { Tracks = tracks.ToList(), Offsets = [0], BPMs = [120] })) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(),
            sampleFiles: new StubSampleFileService(["a.wav", "b.wav"]), sampleDialog: new StubSampleFileDialogService([]),
            trackRemovalDialog: new StubTrackRemovalDialogService { Confirm = _ => Task.FromResult(true) });
        await vm.LoadAsync("project.json", default);
        vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;
        return vm;
    }

    [Test]
    public async Task OpenConfigurationBlocksCreationAcrossTracksButKeepsConfigurationEditable()
    {
        var first = new Track { SelectedSampleFileName = "a.wav" };
        var second = new Track { SelectedSampleFileName = "a.wav" };
        var vm = await CreateAsync(first, second);
        try
        {
            var owner = vm.BeginSampleConfiguration(first);
            Assert.That(owner, Is.Not.Null);
            Assert.That(vm.IsSampleConfigurationOpen, Is.True);
            Assert.That(vm.PlaceSample(first, 125), Is.False);
            Assert.That(vm.PlaceSample(second, 125), Is.False);
            Assert.That(vm.CanImportSample, Is.True);
            vm.SelectSample(first, "b.wav"); vm.SetTrackAddition(first, "Soft"); vm.SetTrackMuted(first, true);
            Assert.That(first.SelectedSampleFileName, Is.EqualTo("b.wav"));
            Assert.That(first.Addition, Is.EqualTo("Soft")); Assert.That(first.IsTrackMuted, Is.True);
            vm.EndSampleConfiguration(owner!.Value);
            Assert.That(vm.PlaceSample(second, 125), Is.True);
        }
        finally { await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task MatchingCloseRestoresCreationAndStaleCloseCannotReleaseNewOwner()
    {
        var track = new Track(); var vm = await CreateAsync(track);
        try
        {
            var old = vm.BeginSampleConfiguration(track)!.Value;
            var current = vm.BeginSampleConfiguration(track)!.Value;
            vm.EndSampleConfiguration(old);
            Assert.That(vm.CanCreateSampleNodes, Is.False);
            vm.EndSampleConfiguration(current);
            Assert.That(vm.CanCreateSampleNodes, Is.True);
            Assert.That(vm.IsSampleConfigurationOpen, Is.False);
        }
        finally { await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task InvalidOrInstrumentalTrackCannotAcquireConfiguration()
    {
        var instrument = new InstrumentalTrack(); var vm = await CreateAsync(instrument);
        try
        {
            Assert.That(vm.BeginSampleConfiguration(new Track()), Is.Null);
            Assert.That(vm.BeginSampleConfiguration(instrument), Is.Null);
            Assert.That(vm.IsSampleConfigurationOpen, Is.False);
        }
        finally { await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task ModeAndReadinessStillGateCreation()
    {
        var track = new Track(); var vm = await CreateAsync(track);
        try
        {
            vm.IsAdditionMode = false; Assert.That(vm.CanCreateSampleNodes, Is.False);
            vm.IsAdditionMode = true; Assert.That(vm.CanCreateSampleNodes, Is.True);
            vm.IsReturning = true; Assert.That(vm.CanCreateSampleNodes, Is.False);
        }
        finally { await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task ProjectLoadRemovalAndDeactivationReleaseConfiguration()
    {
        var track = new Track(); var vm = await CreateAsync(track);
        try
        {
            vm.BeginSampleConfiguration(track);
            await vm.LoadAsync("replacement.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;
            Assert.That(vm.IsSampleConfigurationOpen, Is.False);
            vm.BeginSampleConfiguration(track); await vm.RemoveTrackCommand.ExecuteAsync(track);
            Assert.That(vm.IsSampleConfigurationOpen, Is.False);
        }
        finally { await vm.DeactivateAsync(); }
        Assert.That(vm.IsSampleConfigurationOpen, Is.False);
        Assert.That(vm.BeginSampleConfiguration(track), Is.Null);
    }
}
