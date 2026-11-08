using Hitbench.Core.Entities;
using Hitbench.Resources.DataModels;

namespace Hitbench.Test.Presentation;

public class WorkstationDefaultSampleTests
{
    [Test]
    public async Task NewTrackStartsDefaultAndUnavailableSourceCanBeReconfigured()
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120] };
        var vm = WorkstationSkinDefaultsTests.Create(project, new());
        try
        {
            await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            vm.AddSampleTrackCommand.Execute(null); var track = vm.Tracks.Single();
            Assert.That(track.UsesDefaultSample, Is.True); Assert.That(vm.TrackSampleChoices.First().IsDefault, Is.True);
            Assert.That(vm.CanInteractWithTrack(track), Is.False);
            Assert.That(vm.BeginSampleConfiguration(track), Is.Not.Null);
            vm.SelectSample(track, "a.wav"); Assert.That(track.UsesDefaultSample, Is.False);
            Assert.That(vm.CanInteractWithTrack(track), Is.True);
            Assert.That(vm.SelectDefaultSample(track), Is.False);
        }
        finally { await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task DefaultSupportsNodesAndSwitchingSourceKeepsTimestamps()
    {
        var track = new Track { UsesDefaultSample = true };
        var project = new ProjectData { AllowDefaults = true, Offsets = [0], BPMs = [120], Tracks = [track] };
        var vm = WorkstationSkinDefaultsTests.Create(project, new() { Refresh = (_, _, _, _, _) =>
            Task.FromResult(new OHsSkinDefaultsSnapshot("skin", ["SkinDefault/normal-hitnormal.wav"], false)) });
        try
        {
            await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;
            Assert.That(vm.IsDefaultSampleAvailable(track), Is.True);
            Assert.That(vm.PlaceSample(track, 125), Is.True);
            Assert.That(track.Samples.Single().FileName, Is.Empty);
            vm.SelectSample(track, "a.wav"); Assert.That(track.Samples.Single().Timestamp, Is.EqualTo(125));
            Assert.That(track.Samples.Single().FileName, Is.EqualTo("a.wav"));
            Assert.That(vm.SelectDefaultSample(track), Is.True);
            Assert.That(track.Samples.Single().FileName, Is.Empty);
            await vm.SetAllowDefaultsAsync(false);
            Assert.That(vm.CanEditTrackGrid(track), Is.False);
            var volume = track.Volume; vm.SetTrackVolume(track, 40); Assert.That(track.Volume, Is.EqualTo(volume));
        }
        finally { await vm.DeactivateAsync(); }
    }
}
