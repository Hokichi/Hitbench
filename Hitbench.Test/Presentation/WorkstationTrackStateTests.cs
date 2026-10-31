using Hitbench.Core.Entities;
using Hitbench.Services;
using Hitbench.ViewModels.Shell;
namespace Hitbench.Test.Presentation;
public sealed class WorkstationTrackStateTests
{
    [Test]
    public async Task EmptyAndBoundaryOperationsNeverSave()
    {
        var saves = new DelayedProjectFileService(); saves.Release();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), projectFiles: saves);
        try
        {
            await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            vm.ToggleTracksCollapsedCommand.Execute(null); vm.ToggleTrackMutesCommand.Execute(null);
            Assert.That(vm.AreTracksCollapsed || vm.AreTracksMuted || vm.AreTracksSoloed, Is.False);
            Assert.That(vm.ToggleTracksCollapsedCommand.CanExecute(null), Is.False);
            Assert.That(vm.MoveTrack(new Track(), 0), Is.False);
            await vm.FlushSavesAsync(); Assert.That(saves.Writes, Is.Zero);
            vm.AddPianoTrackCommand.Execute(null); var track = vm.Tracks.Single();
            Assert.That(vm.MoveTrackUpCommand.CanExecute(track), Is.False);
            Assert.That(vm.MoveTrackDownCommand.CanExecute(track), Is.False);
            vm.SetTrackCollapsed(track, true); vm.AddPianoTrackCommand.Execute(null);
            Assert.That(vm.Tracks.Last().IsCollapsed, Is.False);
            Assert.That(track.IsCollapsed, Is.True);
            Assert.That(vm.AreTracksCollapsed, Is.False);
        }
        finally { await vm.DeactivateAsync(); }
    }
    [Test]
    public async Task ExplicitExtentsKeepSharedGrowthAndIndependentCollapse()
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler());
        try
        {
            await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            vm.AddPianoTrackCommand.Execute(null); var track = vm.Tracks.Single();
            vm.SetTrackMinimumExtent(track, false, 160); vm.SetTrackMinimumExtent(track, true, 48);
            vm.IncreaseTrackHeightCommand.Execute(null);
            Assert.That(vm.GetTrackExtent(track), Is.EqualTo(166));
            vm.SetTrackCollapsed(track, true); Assert.That(vm.GetTrackExtent(track), Is.EqualTo(48));
            vm.SetTrackCollapsed(track, false); Assert.That(vm.GetTrackExtent(track), Is.EqualTo(166));
            vm.SetTrackMinimumExtent(track, false, double.NaN); Assert.That(vm.GetTrackExtent(track), Is.EqualTo(166));
        }
        finally { await vm.DeactivateAsync(); }
    }
    [Test]
    public async Task StateMovesAndDraftsPreserveLiveTracks()
    {
        var a = new Track { Name = "A", SelectedSampleFileName = "a.wav" }; var b = new Track { Name = "B" };
        var appearance = new StubOHsTrackAppearanceDialogService();
        var saves = new DelayedProjectFileService(); saves.Release();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", new ProjectData { Tracks = [a,b], Offsets = [0], BPMs = [120] })) }, new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), projectFiles:saves, trackAppearanceDialog:appearance);
        try {
            await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            vm.SetTrackCollapsed(a, true); Assert.That(b.IsCollapsed, Is.False); Assert.That(vm.AreTracksCollapsed, Is.False);
            vm.ToggleTracksCollapsedCommand.Execute(null); Assert.That(vm.AreTracksCollapsed, Is.True);
            Assert.That(vm.MoveTrack(a, 1), Is.True); Assert.That(vm.Project!.Tracks[1], Is.SameAs(a)); Assert.That(vm.MoveTrack(a,1), Is.False);
            await vm.ChangeTrackColorCommand.ExecuteAsync(a); Assert.That(a.Name, Is.EqualTo("A"));
            vm.BeginTrackRenameCommand.Execute(a); vm.TrackNameDraft = "Accepted";
            await vm.CommitTrackRenameAsync(a); Assert.That(a.Name, Is.EqualTo("Accepted"));
            vm.ToggleTrackMutesCommand.Execute(null); Assert.That(vm.AreTracksMuted, Is.True);
            vm.ToggleTrackMutesCommand.Execute(null); Assert.That(vm.HasMutedTracks, Is.False);
            Assert.That(a.SelectedSampleFileName, Is.EqualTo("a.wav"));
        } finally { await vm.DeactivateAsync(); }
    }
}
