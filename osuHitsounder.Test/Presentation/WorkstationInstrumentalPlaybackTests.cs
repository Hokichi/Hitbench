using osuHitsounder.Core.Entities;
using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Services;
using osuHitsounder.ViewModels.Shell;
namespace osuHitsounder.Test.Presentation;

public class WorkstationInstrumentalPlaybackTests
{
    [Test]
    public async Task MixedScheduleSoloRenameDeleteAndRelinkPreserveNoteIdentity()
    {
        var sample = new Track { SelectedSampleFileName = "kick.wav", Samples = [new() { FileName = "kick.wav", Timestamp = 125 }] };
        var instrument = new InstrumentalTrack { Volume = 75, KeyFiles = new() { ["C4"] = "piano.wav", ["E4"] = "piano.wav" }, Notes = [new() { Key = "C4", Timestamp = 125 }, new() { Key = "E4", Timestamp = 125 }] };
        var project = new ProjectData { Tracks = [sample, instrument], Offsets = [0], BPMs = [120] };
        var messenger = new CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger(); var audio = new StubWorkstationAudioService();
        var dialogs = new StubInstrumentalTrackDialogService();
        var owner = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", project)) }, new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler(), sampleFiles: new StubSampleFileService(["kick.wav", "piano.wav"]), messenger: messenger, instrumentDialogs: dialogs);
        dialogs.Mapping = async (snapshot, _) =>
        {
            var context = new osuHitsounder.Resources.DataModels.SampleManagementContext(snapshot.Context.ProjectPath, null, snapshot.Context.ProjectSessionId);
            messenger.Send(new osuHitsounder.Resources.Messages.ProjectSamplesChangedMessage(context, ["kick.wav", "PIANO.wav"], "piano.wav", "PIANO.wav"));
            await owner.FlushPlaybackUpdatesAsync();
            Assert.That(instrument.KeyFiles.Values, Is.All.EqualTo("PIANO.wav"));
            Assert.That(audio.SampleUpdates.Last().Count, Is.EqualTo(2));
            var ids = audio.SampleUpdates.Last().Select(e => e.Id).ToArray();
            messenger.Send(new osuHitsounder.Resources.Messages.ProjectSamplesChangedMessage(context, ["kick.wav"], "PIANO.wav"));
            await owner.FlushPlaybackUpdatesAsync(); Assert.That(audio.SampleUpdates.Last(), Is.Empty); Assert.That(instrument.Notes.Count, Is.EqualTo(2));
            messenger.Send(new osuHitsounder.Resources.Messages.ProjectSamplesChangedMessage(context, ["kick.wav", "piano.wav"]));
            await owner.FlushPlaybackUpdatesAsync(); Assert.That(audio.SampleUpdates.Last().Select(e => e.Id), Is.EqualTo(ids));
        };
        try
        {
            await owner.LoadAsync("project.json", default); owner.AcknowledgePresentationReady(owner.LoadGeneration);
            var combined = audio.SampleUpdates.Last(); Assert.That(combined.Count, Is.EqualTo(3)); Assert.That(combined.Select(e => e.Id).Distinct().Count(), Is.EqualTo(3));
            Assert.That(combined.First(e => e.FileName == "piano.wav").TrackVolume, Is.EqualTo(75));
            Assert.That(owner.PlaceSample(instrument, 250), Is.False); Assert.That(instrument.Samples, Is.Empty);
            owner.SetTrackSoloed(instrument, true); await owner.FlushPlaybackUpdatesAsync(); Assert.That(audio.SampleUpdates.Last().Count, Is.EqualTo(2));
            await owner.KeysMappingCommand.ExecuteAsync(instrument);
        }
        finally { await owner.DeactivateAsync(); }
    }
    [Test]
    public async Task GlobalPlaybackContainsInstrumentAndHonorsMute()
    {
        var instrument = new InstrumentalTrack { Volume = 75, KeyFiles = new() { ["C4"] = "piano.wav" }, Notes = [new() { Key = "C4", Timestamp = 125 }] };
        var project = new ProjectData { Tracks = [instrument], Offsets = [0], BPMs = [120] }; var audio = new StubWorkstationAudioService();
        var owner = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", project)) }, new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler(), sampleFiles: new StubSampleFileService(["piano.wav"]));
        try
        {
            await owner.LoadAsync("project.json", default); owner.AcknowledgePresentationReady(owner.LoadGeneration);
            Assert.That(audio.SampleUpdates.Last().Single().FileName, Is.EqualTo("piano.wav"));
            var id = audio.SampleUpdates.Last().Single().Id;
            owner.SetTrackMuted(instrument, true); await owner.FlushPlaybackUpdatesAsync(); Assert.That(audio.SampleUpdates.Last(), Is.Empty);
            owner.SetTrackMuted(instrument, false); await owner.FlushPlaybackUpdatesAsync(); Assert.That(audio.SampleUpdates.Last().Single().Id, Is.EqualTo(id));
        }
        finally { await owner.DeactivateAsync(); }
    }
}
