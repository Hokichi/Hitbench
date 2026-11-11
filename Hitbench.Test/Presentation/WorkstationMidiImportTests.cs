using Hitbench.Core.Entities;
using Hitbench.Resources.DataModels;
using Hitbench.Services;
using Hitbench.ViewModels.Shell;
using Hitbench.Test.Services;

namespace Hitbench.Test.Presentation;

public class WorkstationMidiImportTests
{
    [TestCase(false, OHsMidiImportMode.AsIs, 1333, 0)]
    [TestCase(true, OHsMidiImportMode.AsIs, 2000, 1)]
    [TestCase(true, OHsMidiImportMode.ClosestBeat, 2000, 1)]
    [TestCase(true, OHsMidiImportMode.ProjectBpm, 1333, 1)]
    public async Task SelectedTimingModeControlsAtomicImport(bool explicitTempo, OHsMidiImportMode mode, int expected, int prompts)
    {
        var reader = new StubOHsMidiFileReader { Read = (_, _) => Task.FromResult(OHsMidiImportMapperTests.Source(explicitTempo: explicitTempo)) };
        var dialogs = new StubOHsMidiImportDialogService { Mode = mode };
        var (vm, saves, project) = Create(reader, dialogs);
        try
        {
            await Ready(vm);
            await vm.ImportMidiCommand.ExecuteAsync(null); await vm.FlushSavesAsync();
            Assert.That(((InstrumentalTrack)project.Tracks.Single()).Notes.Single().Timestamp, Is.EqualTo(expected));
            Assert.That(dialogs.TimingChoiceCalls, Is.EqualTo(prompts));
            Assert.That(saves.Writes, Is.EqualTo(1));
            Assert.That(vm.SelectedTrack, Is.SameAs(project.Tracks[0]));
            Assert.That(vm.CanImportMidi, Is.True);
        }
        finally { await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task MatchingTempoPreservesExpressiveTimingWithoutPrompt()
    {
        var reader = new StubOHsMidiFileReader { Read = (_, _) => Task.FromResult(OHsMidiImportMapperTests.Source(123)) };
        var dialogs = new StubOHsMidiImportDialogService();
        var (vm, saves, project) = Create(reader, dialogs, bpm: 120);
        try
        {
            await Ready(vm); await vm.ImportMidiCommand.ExecuteAsync(null);
            Assert.That(dialogs.TimingChoiceCalls, Is.Zero);
            Assert.That(((InstrumentalTrack)project.Tracks.Single()).Notes.Single().Timestamp, Is.EqualTo(128));
        }
        finally { await vm.DeactivateAsync(); }
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task CanceledPickerOrTimingChoiceDoesNotWrite(bool picker)
    {
        var dialogs = new StubOHsMidiImportDialogService { Path = picker ? null : "song.mid", Mode = null };
        var (vm, saves, project) = Create(new(), dialogs);
        try { await Ready(vm); await vm.ImportMidiCommand.ExecuteAsync(null); Assert.That(project.Tracks, Is.Empty); Assert.That(saves.Writes, Is.Zero); Assert.That(dialogs.Errors, Is.Empty); }
        finally { await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task RejectedExclusionsAbortWholeBatch()
    {
        var source = OHsMidiImportMapperTests.Source();
        var reader = new StubOHsMidiFileReader { Read = (_, _) => Task.FromResult(new OHsMidiImportSource(source.FileName, 480, true, source.Tempos, source.Parts, 1, [])) };
        var dialogs = new StubOHsMidiImportDialogService { AcceptExclusions = false };
        var (vm, saves, project) = Create(reader, dialogs);
        try { await Ready(vm); await vm.ImportMidiCommand.ExecuteAsync(null); Assert.That(dialogs.ExclusionCalls, Is.EqualTo(1)); Assert.That(project.Tracks, Is.Empty); Assert.That(saves.Writes, Is.Zero); }
        finally { await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task ReaderFailureUsesMessageBoundaryAndRestoresGates()
    {
        var reader = new StubOHsMidiFileReader { Read = (_, _) => throw new InvalidDataException("broken MIDI") };
        var dialogs = new StubOHsMidiImportDialogService();
        var (vm, saves, project) = Create(reader, dialogs);
        try
        {
            await Ready(vm); await vm.ImportMidiCommand.ExecuteAsync(null);
            Assert.That(dialogs.Errors.Single().Message, Is.EqualTo("broken MIDI"));
            Assert.That(project.Tracks, Is.Empty); Assert.That(saves.Writes, Is.Zero); Assert.That(vm.CanPlay, Is.True); Assert.That(vm.CanImportMidi, Is.True);
        }
        finally { await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task TimingChangesWhileChoosingPreventStaleCommit()
    {
        var entered = new TaskCompletionSource(); var release = new TaskCompletionSource<OHsMidiImportMode?>();
        var dialogs = new StubOHsMidiImportDialogService { Choose = _ => { entered.SetResult(); return release.Task; } };
        var (vm, saves, project) = Create(new(), dialogs);
        try
        {
            await Ready(vm); var import = vm.ImportMidiCommand.ExecuteAsync(null); await entered.Task;
            project.UninheritedPoints.Add(new() { Offset = 1000, BPM = 240 }); release.SetResult(OHsMidiImportMode.ProjectBpm);
            await import; Assert.That(project.Tracks, Is.Empty); Assert.That(saves.Writes, Is.Zero); Assert.That(dialogs.Errors, Is.Empty);
        }
        finally { release.TrySetResult(null); await vm.DeactivateAsync(); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task NavigationCancelsUncooperativeReaderAndBlocksConcurrentEdits(bool deactivate)
    {
        var entered = new TaskCompletionSource(); var release = new TaskCompletionSource<OHsMidiImportSource>();
        var reader = new StubOHsMidiFileReader { Read = (_, _) => { entered.TrySetResult(); return release.Task; } };
        var dialogs = new StubOHsMidiImportDialogService();
        var (vm, saves, project) = Create(reader, dialogs);
        try
        {
            await Ready(vm); var import = vm.ImportMidiCommand.ExecuteAsync(null); await entered.Task;
            Assert.That(vm.CanPlay, Is.False); Assert.That(vm.CanAddTrack, Is.False); Assert.That(vm.CanZoom, Is.False);
            await vm.ImportMidiCommand.ExecuteAsync(null); Assert.That(reader.Calls, Is.EqualTo(1));
            var navigation = deactivate ? vm.DeactivateAsync() : vm.LoadAsync("other.json", default);
            Assert.That(navigation.IsCompleted, Is.False);
            release.SetResult(OHsMidiImportMapperTests.Source()); await Task.WhenAll(import, navigation);
            Assert.That(project.Tracks, Is.Empty); Assert.That(saves.Writes, Is.Zero); Assert.That(dialogs.Errors, Is.Empty);
        }
        finally { release.TrySetResult(OHsMidiImportMapperTests.Source()); await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task FlushSavesWaitsForOwnedImport()
    {
        var entered = new TaskCompletionSource(); var release = new TaskCompletionSource<OHsMidiImportSource>();
        var reader = new StubOHsMidiFileReader { Read = (_, _) => { entered.SetResult(); return release.Task; } };
        var (vm, saves, _) = Create(reader, new());
        try
        {
            await Ready(vm); var import = vm.ImportMidiCommand.ExecuteAsync(null); await entered.Task;
            var flush = vm.FlushSavesAsync();
            Assert.That(flush.IsCompleted, Is.False);
            release.SetResult(OHsMidiImportMapperTests.Source()); await Task.WhenAll(flush, import);
            Assert.That(saves.Writes, Is.EqualTo(1));
        }
        finally { release.TrySetResult(OHsMidiImportMapperTests.Source()); await vm.DeactivateAsync(); }
    }

    private static (WorkstationPageVM Vm, DelayedProjectFileService Saves, ProjectData Project) Create(StubOHsMidiFileReader reader, StubOHsMidiImportDialogService dialogs, int bpm = 180)
    {
        var project = new ProjectData { UninheritedPoints = TimingTestData.Points([0], [bpm]), ControlPoints = [new()] };
        var saves = new DelayedProjectFileService(); saves.Release();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", project)) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), projectFiles: saves,
            saveDelay: (_, _) => Task.CompletedTask, midiReader: reader, midiDialogs: dialogs);
        return (vm, saves, project);
    }
    private static async Task Ready(WorkstationPageVM vm) { await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration); }
}
