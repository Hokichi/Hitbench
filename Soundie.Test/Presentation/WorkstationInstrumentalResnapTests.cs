using Soundie.Core.Entities;
using Soundie.Services;
using Soundie.ViewModels.Shell;
using CommunityToolkit.Mvvm.Messaging;
using Soundie.Resources.DataModels;
using Soundie.Resources.Messages;

namespace Soundie.Test.Presentation;

public sealed class WorkstationInstrumentalResnapTests
{
    [Test]
    public async Task CombinedResnapPreservesIdentitiesAndIndependentHistoryAndNoOpWrites()
    {
        var instrument = new InstrumentalTrack { KeyFiles = new() { ["C4"] = "piano.wav" }, Notes = [new() { Key = "C4", Timestamp = 1100 }] };
        var sample = new Sample { FileName = "hit.wav", Timestamp = 1300 };
        var sampleTrack = new Track { SelectedSampleFileName = "hit.wav", Samples = [sample] };
        var messenger = new WeakReferenceMessenger(); var dialogs = new StubInstrumentalTrackDialogService();
        var audio = new StubWorkstationAudioService(); var saves = new DelayedProjectFileService(); saves.Release();
        var entered = new TaskCompletionSource<InstrumentalEditorContext>();
        dialogs.Arrangement = async (context, token) => { entered.TrySetResult(context); await Task.Delay(Timeout.Infinite, token); };
        InstrumentalTrackSnapshot? latest = null;
        messenger.Register<InstrumentalTrackChangedMessage>(this, (_, m) => latest = m.Snapshot);
        var owner = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", new ProjectData { Tracks = [sampleTrack, instrument], Offsets = [0], BPMs = [120] })) },
            new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler(), projectFiles: saves, messenger: messenger, instrumentDialogs: dialogs);
        try
        {
            await owner.LoadAsync("project.json", default); owner.AcknowledgePresentationReady(owner.LoadGeneration);
            owner.SelectedSubdivisionIndex = 0;
            var showing = owner.ViewArrangementCommand.ExecuteAsync(instrument); var data = await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var id = data.Track.Notes.Single().Id;
            owner.ResnapAllSamplesCommand.Execute(null);
            Assert.That(sample.Timestamp, Is.EqualTo(1500)); Assert.That(instrument.Notes.Single().Timestamp, Is.EqualTo(1000));
            Assert.That(latest!.Notes.Single().Id, Is.EqualTo(id));
            await owner.FlushPlaybackUpdatesAsync();
            var updates = audio.SampleUpdates.Count; var writes = saves.Writes;
            Assert.That(owner.ResnapAllSampleNodes(), Is.False);
            await owner.FlushPlaybackUpdatesAsync();
            Assert.That(audio.SampleUpdates.Count, Is.EqualTo(updates)); Assert.That(saves.Writes, Is.EqualTo(writes));
            messenger.Send(new ReplayInstrumentalNoteHistoryRequestedMessage(data.Track.Context, Guid.NewGuid(), false));
            Assert.That(instrument.Notes.Single().Timestamp, Is.EqualTo(1100)); Assert.That(sample.Timestamp, Is.EqualTo(1500));
            owner.UndoCommand.Execute(null);
            Assert.That(sample.Timestamp, Is.EqualTo(1300)); Assert.That(instrument.Notes.Single().Timestamp, Is.EqualTo(1100));
            messenger.Send(new ReplayInstrumentalNoteHistoryRequestedMessage(data.Track.Context, Guid.NewGuid(), true));
            Assert.That(latest!.Notes.Single().Id, Is.EqualTo(id)); Assert.That(instrument.KeyFiles["C4"], Is.EqualTo("piano.wav"));
            await owner.SettleInstrumentalEditorAsync(); await showing; await owner.FlushSavesAsync();
        }
        finally { await owner.DeactivateAsync(); messenger.UnregisterAll(this); }
    }
    [TestCase(new[] { 1100, 1000 }, new[] { 1100, 1000 })]
    [TestCase(new[] { 1100, 1200 }, new[] { 1000, 1200 })]
    [TestCase(new[] { 1200, 1100 }, new[] { 1000, 1100 })]
    [TestCase(new[] { 1100, 1200, 1300 }, new[] { 1000, 1200, 1500 })]
    public async Task ClosedInstrumentPreservesResidentsAndFirstClaimWithoutLosingNotes(int[] times, int[] expected)
    {
        var track = new InstrumentalTrack { Notes = times.Select(t => new InstrumentalNote { Key = "C4", Timestamp = t }).ToList() };
        var other = new InstrumentalTrack { Notes = [new() { Key = "D4", Timestamp = 1100 }] };
        var owner = new WorkstationPageVM(new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(
            new WorkstationSource("map", "song", new ProjectData { Tracks = [track, other], Offsets = [0], BPMs = [120] }))
        },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler());
        try
        {
            await owner.LoadAsync("project.json", default); owner.AcknowledgePresentationReady(owner.LoadGeneration);
            owner.SelectedSubdivisionIndex = 0;
            Assert.That(owner.ResnapAllSamplesCommand.CanExecute(null), Is.True);
            owner.ResnapAllSamplesCommand.Execute(null);
            Assert.That(track.Notes.Select(n => n.Timestamp), Is.EqualTo(expected));
            Assert.That(track.Notes.Select(n => (n.Key, n.Timestamp)).Distinct().Count(), Is.EqualTo(times.Length));
            Assert.That(track.Notes.All(n => n.Key == "C4"), Is.True);
            Assert.That(other.Notes.Single().Timestamp, Is.EqualTo(1000));
            owner.ResnapAllSamplesCommand.Execute(null);
            Assert.That(track.Notes.Select(n => n.Timestamp), Is.EqualTo(expected));
        }
        finally { await owner.DeactivateAsync(); }
    }
}
