using Onset.Core.Entities;
using Onset.Services;
using Onset.ViewModels.Shell;
namespace Onset.Test.Presentation;
public sealed class WorkstationTrackReorderTests
{
    [Test]
    public async Task ReorderRetainsNodesSelectionAndHistoryAndRejectsStaleIdentity()
    {
        var a = new Track { SelectedSampleFileName = "hit.wav", Name = "A" };
        var b = new InstrumentalTrack { Name = "B", IsCollapsed = true, Notes = [new InstrumentalNote { Key = "C3", Timestamp = 125 }] };
        var saves = new DelayedProjectFileService(); saves.Release();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", new ProjectData { Tracks = [a,b], Offsets = [0], BPMs = [120] })) }, new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), projectFiles: saves);
        try
        {
            await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            vm.SampleFiles.Add("hit.wav");
            Assert.That(vm.PlaceSample(a, 125), Is.True);
            var sample = a.Samples.Single(); var note = b.Notes.Single();
            vm.SelectedTrack = a;
            var consistent = true;
            vm.Tracks.CollectionChanged += (_, _) => consistent &= vm.Project!.Tracks.SequenceEqual(vm.Tracks);
            Assert.That(vm.MoveTrack(a, 1), Is.True);
            Assert.That(consistent, Is.True); Assert.That(vm.SelectedTrack, Is.SameAs(a));
            Assert.That(b.Notes.Single(), Is.SameAs(note)); Assert.That(b.IsCollapsed, Is.True);
            Assert.That(vm.SelectedSamples.Single(), Is.SameAs(sample));
            vm.UndoCommand.Execute(null); Assert.That(a.Samples, Is.Empty);
            vm.RedoCommand.Execute(null); Assert.That(a.Samples.Single(), Is.SameAs(sample));
            await vm.FlushSavesAsync(); var writes = saves.Writes;
            Assert.That(vm.MoveTrack(a,1), Is.False); Assert.That(vm.MoveTrack(a,2), Is.False);
            Assert.That(vm.MoveTrack(new Track(),0), Is.False);
            await vm.FlushSavesAsync(); Assert.That(saves.Writes, Is.EqualTo(writes));
            await vm.DeactivateAsync(); Assert.That(vm.MoveTrack(a,0), Is.False);
        }
        finally { await vm.DeactivateAsync(); }
    }
}
