using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Core.Entities;
using Hitbench.Resources.DataModels;
using Hitbench.Resources.Messages;
using Hitbench.Services;
using Hitbench.Services.FileSystem;
using Hitbench.Services.ProjectFiles;
using Hitbench.ViewModels.Shell;
using Hitbench.Test.Services;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;

namespace Hitbench.Test.Presentation;

public class WorkstationMidiImportIntegrationTests
{
    [TestCase(OHsMidiImportMode.AsIs, 1250)]
    [TestCase(OHsMidiImportMode.ClosestBeat, 1250)]
    [TestCase(OHsMidiImportMode.ProjectBpm, 667)]
    public async Task RealFileImportsEachSourcePartAsOneSavedBatch(OHsMidiImportMode mode, int expected)
    {
        using var midi = OHsMidiTestData.File(480,
            OHsMidiTestData.Track(new SetTempoEvent(500000), new SequenceTrackNameEvent("Piano"), new SetTempoEvent(750000) { DeltaTime = 480 }, Note(60, 480)),
            OHsMidiTestData.Track(new SequenceTrackNameEvent("Piano"), Note(64, 960)));
        var path = Path.GetTempFileName(); await File.WriteAllBytesAsync(path, midi.ToArray());
        var project = new ProjectData { UninheritedPoints = TimingTestData.Points([0], [180]), ControlPoints = [new()] };
        var saves = new DelayedProjectFileService(); saves.Release();
        var dialogs = new StubOHsMidiImportDialogService { Path = path, Mode = mode };
        var vm = Create(project, saves, dialogs);
        try
        {
            await Ready(vm); await vm.ImportMidiCommand.ExecuteAsync(null); await vm.FlushSavesAsync();
            Assert.That(dialogs.Errors, Is.Empty); Assert.That(dialogs.TimingChoiceCalls, Is.EqualTo(1));
            Assert.That(project.Tracks, Has.Count.EqualTo(2));
            Assert.That(project.Tracks.Select(t => t.Name), Is.EqualTo(new[] { "Piano", "Piano (1)" }));
            Assert.That(project.Tracks.Cast<InstrumentalTrack>().SelectMany(t => t.Notes).Select(n => n.Timestamp), Is.All.EqualTo(expected));
            Assert.That(saves.Writes, Is.EqualTo(1));
            Assert.That(ProjectDataSerializer.Deserialize(saves.LatestJson!).Tracks, Has.Count.EqualTo(2));
        }
        finally { await vm.DeactivateAsync(); File.Delete(path); }
    }

    [Test]
    public async Task SavedImportRetainsLockedNotesWithoutEmittingVoices()
    {
        using var midi = OHsMidiTestData.File(480, OHsMidiTestData.Track(new SetTempoEvent(500000), Note(60, 0), Note(60, 192), Note(60, 864)));
        var path = Path.GetTempFileName(); await File.WriteAllBytesAsync(path, midi.ToArray());
        var project = new ProjectData { UninheritedPoints = TimingTestData.Points([-100], [120]), ControlPoints = [new() { Timestamp = -100 }] };
        var saves = new DelayedProjectFileService(); saves.Release();
        var dialogs = new StubOHsMidiImportDialogService { Path = path };
        var messenger = new WeakReferenceMessenger(); var instrumentDialogs = new StubInstrumentalTrackDialogService();
        var vm = Create(project, saves, dialogs, messenger, instrumentDialogs);
        try
        {
            await Ready(vm); await vm.ImportMidiCommand.ExecuteAsync(null); await vm.FlushSavesAsync();
            Assert.That(dialogs.Errors, Is.Empty);
            var reopened = ProjectDataSerializer.Deserialize(saves.LatestJson!);
            var track = (InstrumentalTrack)reopened.Tracks.Single();
            Assert.That(track.Notes.Select(n => (n.Timestamp, n.IsReadOnly)), Is.EqualTo(new[] { (-100, true), (100, false), (1000, true) }));
            track.KeyFiles["C4"] = "piano.wav";
            var snapshots = track.Notes.Select(n => new InstrumentalNoteSnapshot(Guid.NewGuid(), n.Key, n.Timestamp, n.IsReadOnly)).ToArray();
            var events = InstrumentalPlaybackEvents.Build(track.KeyFiles, snapshots, 100, null, _ => 1, 1000);
            Assert.That(events.Select(e => e.Timestamp), Is.EqualTo(new[] { 100 }));
            var export = OHsExportInputFactory.Create(reopened, OHsExportBeatmap.Parse("osu file format v14\n[TimingPoints]\n-100,500,4,1,0,100,1,0\n"),
                new("Hitsounds", 1, ".ogg", ".ogg", false), default, durationMilliseconds: 1000);
            Assert.That(export.Voices.Select(v => v.Timestamp), Is.EqualTo(new[] { 100 }));

            var runtimeTrack = (InstrumentalTrack)project.Tracks.Single();
            instrumentDialogs.Arrangement = (context, _) =>
            {
                var editable = context.Track.Notes.Single(n => !n.IsReadOnly);
                messenger.Send(new MoveInstrumentalNoteRequestedMessage(context.Track.Context, Guid.NewGuid(), editable.Id, "C4", 200, 4));
                Assert.That(runtimeTrack.Notes.Single(n => !n.IsReadOnly).Timestamp, Is.EqualTo(150));
                messenger.Send(new ReplayInstrumentalNoteHistoryRequestedMessage(context.Track.Context, Guid.NewGuid(), false));
                Assert.That(runtimeTrack.Notes.Single(n => !n.IsReadOnly).Timestamp, Is.EqualTo(100));
                messenger.Send(new ReplayInstrumentalNoteHistoryRequestedMessage(context.Track.Context, Guid.NewGuid(), true));
                vm.ResnapAllSamplesCommand.Execute(null);
                Assert.That(runtimeTrack.Notes.Where(n => n.IsReadOnly).Select(n => n.Timestamp), Is.EqualTo(new[] { -100, 1000 }));
                return Task.CompletedTask;
            };
            Assert.That(vm.ViewArrangementCommand.CanExecute(runtimeTrack), Is.True);
            await vm.ViewArrangementCommand.ExecuteAsync(runtimeTrack); await vm.FlushSavesAsync();
            Assert.That(((InstrumentalTrack)ProjectDataSerializer.Deserialize(saves.LatestJson!).Tracks.Single()).Notes.Count(n => n.IsReadOnly), Is.EqualTo(2));
        }
        finally { await vm.DeactivateAsync(); File.Delete(path); }
    }

    [Test]
    public async Task LongSourceMetadataStaysContentWithoutCreatingSamplePaths()
    {
        var name = "../音楽\\" + new string('名', 400);
        using var midi = OHsMidiTestData.File(480, OHsMidiTestData.Track(new SequenceTrackNameEvent(name), Note(60, 480)));
        var path = Path.GetTempFileName(); await File.WriteAllBytesAsync(path, midi.ToArray());
        var project = new ProjectData { UninheritedPoints = TimingTestData.Points([0], [120]), ControlPoints = [new()] };
        var saves = new DelayedProjectFileService(); saves.Release();
        var vm = Create(project, saves, new() { Path = path });
        try
        {
            await Ready(vm); await vm.ImportMidiCommand.ExecuteAsync(null); await vm.FlushSavesAsync();
            var track = (InstrumentalTrack)ProjectDataSerializer.Deserialize(saves.LatestJson!).Tracks.Single();
            Assert.That(track.Name, Is.EqualTo(name)); Assert.That(track.FolderName, Is.Empty); Assert.That(track.KeyFiles, Is.Empty);
        }
        finally { await vm.DeactivateAsync(); File.Delete(path); }
    }

    [Test]
    public async Task RejectedRealFileExclusionsLeaveNoBatchOrSave()
    {
        using var midi = OHsMidiTestData.File(480, OHsMidiTestData.Track(Note(12, 0), Note(60, 480)));
        var path = Path.GetTempFileName(); await File.WriteAllBytesAsync(path, midi.ToArray());
        var project = new ProjectData { UninheritedPoints = TimingTestData.Points([0], [120]), ControlPoints = [new()] };
        var saves = new DelayedProjectFileService(); saves.Release();
        var dialogs = new StubOHsMidiImportDialogService { Path = path, AcceptExclusions = false };
        var vm = Create(project, saves, dialogs);
        try
        {
            await Ready(vm); await vm.ImportMidiCommand.ExecuteAsync(null); await vm.FlushSavesAsync();
            Assert.That(dialogs.ExclusionCalls, Is.EqualTo(1)); Assert.That(dialogs.Errors, Is.Empty);
            Assert.That(project.Tracks, Is.Empty); Assert.That(saves.Writes, Is.Zero);
        }
        finally { await vm.DeactivateAsync(); File.Delete(path); }
    }

    [Test]
    public async Task RealFileCannotCommitAfterTimingChangesDuringChoice()
    {
        using var midi = OHsMidiTestData.File(480, OHsMidiTestData.Track(new SetTempoEvent(500000), Note(60, 480)));
        var path = Path.GetTempFileName(); await File.WriteAllBytesAsync(path, midi.ToArray());
        var project = new ProjectData { UninheritedPoints = TimingTestData.Points([0], [180]), ControlPoints = [new()] };
        var saves = new DelayedProjectFileService(); saves.Release();
        var entered = new TaskCompletionSource(); var release = new TaskCompletionSource<OHsMidiImportMode?>();
        var dialogs = new StubOHsMidiImportDialogService { Path = path, Choose = _ => { entered.SetResult(); return release.Task; } };
        var vm = Create(project, saves, dialogs);
        try
        {
            await Ready(vm); var import = vm.ImportMidiCommand.ExecuteAsync(null); await entered.Task;
            project.UninheritedPoints.Add(new() { Offset = 500, BPM = 240 });
            release.SetResult(OHsMidiImportMode.ProjectBpm); await import; await vm.FlushSavesAsync();
            Assert.That(project.Tracks, Is.Empty); Assert.That(saves.Writes, Is.Zero); Assert.That(dialogs.Errors, Is.Empty);
        }
        finally { release.TrySetResult(null); await vm.DeactivateAsync(); File.Delete(path); }
    }

    [Test]
    public async Task FailedSaveRetainsBatchAndCanRetry()
    {
        using var midi = OHsMidiTestData.File(480, OHsMidiTestData.Track(Note(60, 0)));
        var path = Path.GetTempFileName(); await File.WriteAllBytesAsync(path, midi.ToArray());
        var project = new ProjectData { UninheritedPoints = TimingTestData.Points([0], [120]), ControlPoints = [new()] };
        var saves = new FailOnceProjectFileService();
        var vm = Create(project, saves, new() { Path = path });
        try
        {
            await Ready(vm); await vm.ImportMidiCommand.ExecuteAsync(null);
            Assert.ThrowsAsync<IOException>(async () => await vm.FlushSavesAsync());
            Assert.That(project.Tracks, Has.Count.EqualTo(1)); Assert.That(vm.SaveError, Does.Contain("Disk full"));
            vm.RetrySaveCommand.Execute(null); await vm.FlushSavesAsync();
            Assert.That(vm.SaveError, Is.Null); Assert.That(ProjectDataSerializer.Deserialize(saves.SavedJson!).Tracks, Has.Count.EqualTo(1));
        }
        finally { await vm.DeactivateAsync(); File.Delete(path); }
    }

    private static NoteOnEvent Note(byte number, long delta) => new((SevenBitNumber)number, (SevenBitNumber)100) { DeltaTime = delta };
    private static WorkstationPageVM Create(ProjectData project, IProjectFileService saves, StubOHsMidiImportDialogService dialogs,
        IMessenger? messenger = null, StubInstrumentalTrackDialogService? instrumentDialogs = null) =>
        new(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", project)) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService { Analyze = (_, _) => Task.FromResult(new WaveformData(TimeSpan.FromSeconds(1), [.5f])) }, Task.Delay),
            new ImmediateUiScheduler(), projectFiles: saves, messenger: messenger, instrumentDialogs: instrumentDialogs,
            saveDelay: (_, _) => Task.CompletedTask, midiReader: new OHsMidiFileReader(), midiDialogs: dialogs);
    private static async Task Ready(WorkstationPageVM vm) { await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration); }
}
