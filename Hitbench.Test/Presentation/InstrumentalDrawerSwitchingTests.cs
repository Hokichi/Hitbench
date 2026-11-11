using Hitbench.Test;
using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Core.Entities;
using Hitbench.Resources.DataModels;
using Hitbench.Services;
using Hitbench.ViewModels.Shell;

namespace Hitbench.Test.Presentation;

public sealed class InstrumentalDrawerSwitchingTests
{
    [TestCase("normal")]
    [TestCase("menu")]
    [TestCase("clear")]
    [TestCase("cancel")]
    [TestCase("project")]
    [TestCase("remove")]
    [TestCase("failure")]
    [TestCase("save")]
    [TestCase("supersede")]
    public async Task ArrangementMappingReturnsOnlyAfterNormalCloseWithValidOrigin(string completion)
    {
        var track = new InstrumentalTrack { KeyFiles = new() { ["C4"] = "piano.wav" } };
        var other = new InstrumentalTrack { KeyFiles = new() { ["D4"] = "other.wav" } };
        var messenger = new WeakReferenceMessenger();
        var first = new TaskCompletionSource<InstrumentalEditorContext>();
        var closeFirst = new TaskCompletionSource();
        var mapped = new TaskCompletionSource<InstrumentalTrackSnapshot>();
        var closeMapping = new TaskCompletionSource();
        var returned = new TaskCompletionSource<InstrumentalEditorContext>();
        var arrangementCalls = 0;
        var dialogs = new StubInstrumentalTrackDialogService
        {
            Arrangement = async (data, token) =>
            {
                if (++arrangementCalls == 1) { first.SetResult(data); await closeFirst.Task.WaitAsync(token); }
                else { returned.TrySetResult(data); await Task.Delay(Timeout.Infinite, token); }
            },
            Mapping = async (data, token) =>
            {
                mapped.TrySetResult(data); await closeMapping.Task.WaitAsync(token);
                if (completion == "failure") throw new IOException("mapping failed");
            }
        };
        var owner = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(
            new WorkstationSource("map", "song", new ProjectData { Tracks = [track, other], UninheritedPoints = TimingTestData.Points([0], [120]), ControlPoints = [new()] })) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), messenger: messenger,
            projectFiles: completion == "save" ? new FailOnceProjectFileService() : null,
            instrumentDialogs: dialogs, trackRemovalDialog: new StubTrackRemovalDialogService { Confirm = _ => Task.FromResult(true) });
        try
        {
            await owner.LoadAsync("project.json", default); owner.AcknowledgePresentationReady(owner.LoadGeneration);
            var editor = owner.ViewArrangementCommand.ExecuteAsync(track);
            var original = await first.Task.WaitAsync(TimeSpan.FromSeconds(3));
            messenger.Send(new Hitbench.Resources.Messages.OHsInstrumentalKeysMappingRequestedMessage(original.Track.Context));
            closeFirst.SetResult();
            var mapping = await mapped.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(owner.CanPlay, Is.False);
            switch (completion)
            {
                case "menu": await owner.KeysMappingCommand.ExecuteAsync(track); break;
                case "clear": track.KeyFiles.Clear(); break;
                case "cancel": await owner.SettleInstrumentalEditorAsync(); break;
                case "project": await owner.LoadAsync("replacement.json", default); break;
                case "remove": await owner.RemoveTrackCommand.ExecuteAsync(track); break;
                case "save": owner.SetTrackVolume(track, 80); break;
                case "supersede":
                    var replacement = owner.ViewArrangementCommand.ExecuteAsync(other);
                    var next = await returned.Task.WaitAsync(TimeSpan.FromSeconds(3));
                    Assert.That(next.Track.Context.TrackId, Is.Not.EqualTo(original.Track.Context.TrackId));
                    await owner.SettleInstrumentalEditorAsync(); await replacement;
                    break;
            }
            closeMapping.TrySetResult();
            if (completion == "normal")
            {
                var reopened = await returned.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.That(reopened.Track.Context.TrackId, Is.EqualTo(original.Track.Context.TrackId));
                Assert.That(owner.CanPlay, Is.True);
                await owner.SettleInstrumentalEditorAsync();
            }
            await editor.WaitAsync(TimeSpan.FromSeconds(3));
            var expectedCalls = completion is "normal" or "supersede" ? 2 : 1;
            Assert.That(arrangementCalls, Is.EqualTo(expectedCalls));
            messenger.Send(new Hitbench.Resources.Messages.OHsInstrumentalKeysMappingRequestedMessage(original.Track.Context));
            Assert.That(arrangementCalls, Is.EqualTo(expectedCalls));
            if (completion == "failure") Assert.That(owner.ErrorMessage, Is.EqualTo("mapping failed"));
        }
        finally
        {
            closeFirst.TrySetResult(); closeMapping.TrySetResult();
            if (owner.SaveError is not null) { owner.RetrySaveCommand.Execute(null); await owner.FlushSavesAsync(); }
            await owner.DeactivateAsync();
        }
    }

    [Test]
    public async Task MenuMappingDoesNotOpenArrangementOnClose()
    {
        var track = new InstrumentalTrack { KeyFiles = new() { ["C4"] = "piano.wav" } };
        var calls = 0;
        var owner = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(
            new WorkstationSource("map", "song", new ProjectData { Tracks = [track], UninheritedPoints = TimingTestData.Points([0], [120]), ControlPoints = [new()] })) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), messenger: new WeakReferenceMessenger(),
            instrumentDialogs: new StubInstrumentalTrackDialogService { Arrangement = (_, _) => { calls++; return Task.CompletedTask; } });
        try
        {
            await owner.LoadAsync("project.json", default); owner.AcknowledgePresentationReady(owner.LoadGeneration);
            await owner.KeysMappingCommand.ExecuteAsync(track);
            Assert.That(calls, Is.Zero);
        }
        finally { await owner.DeactivateAsync(); }
    }

    [Test]
    public async Task RemovingPresentedInstrumentSettlesEditorBeforeRemovingTrack()
    {
        var track = new InstrumentalTrack { KeyFiles = new() { ["C4"] = "piano.wav" }, Notes = [new() { Key = "C4", Timestamp = 125 }] };
        var opened = new TaskCompletionSource<InstrumentalTrackContext>(); var settled = false;
        var messenger = new WeakReferenceMessenger();
        var dialogs = new StubInstrumentalTrackDialogService { Arrangement = async (data, token) =>
        { opened.TrySetResult(data.Track.Context); try { await Task.Delay(Timeout.Infinite, token); } finally { settled = true; } } };
        var owner = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", new ProjectData { Tracks = [track], UninheritedPoints = TimingTestData.Points([0], [120]), ControlPoints = [new()] })) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(),
            messenger: messenger, instrumentDialogs: dialogs, trackRemovalDialog: new StubTrackRemovalDialogService { Confirm = _ => Task.FromResult(true) });
        try
        {
            await owner.LoadAsync("project.json", default); owner.AcknowledgePresentationReady(owner.LoadGeneration);
            var editor = owner.ViewArrangementCommand.ExecuteAsync(track); var context = await opened.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(owner.CanRemoveTrack, Is.True);
            await owner.RemoveTrackCommand.ExecuteAsync(track); await editor;
            Assert.That(settled, Is.True); Assert.That(owner.Tracks, Does.Not.Contain(track));
            messenger.Send(new Hitbench.Resources.Messages.PlaceInstrumentalNoteRequestedMessage(context, Guid.NewGuid(), "E4", 250, 4));
            Assert.That(track.Notes.Count, Is.EqualTo(1));
        }
        finally { await owner.DeactivateAsync(); }
    }
    [Test]
    public async Task MappingCancelsPendingPlayEvenWhenPreparationIgnoresCancellation()
    {
        var track = new InstrumentalTrack { KeyFiles = new() { ["C4"] = "piano.wav" } };
        var audio = new StubWorkstationAudioService(); var prepared = new TaskCompletionSource(); var entered = new TaskCompletionSource();
        var dialogs = new StubInstrumentalTrackDialogService { Mapping = async (_, token) => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); } };
        var owner = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", new ProjectData { Tracks = [track], UninheritedPoints = TimingTestData.Points([0], [120]), ControlPoints = [new()] })) },
            new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler(), messenger: new WeakReferenceMessenger(), instrumentDialogs: dialogs);
        try
        {
            await owner.LoadAsync("project.json", default); owner.AcknowledgePresentationReady(owner.LoadGeneration);
            audio.PrepareSamplesAsync = _ => prepared.Task;
            owner.SetTrackVolume(track, 50);
            var play = owner.PlayPauseCommand.ExecuteAsync(null);
            var mapping = owner.KeysMappingCommand.ExecuteAsync(track); await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            prepared.SetResult(); await play.WaitAsync(TimeSpan.FromSeconds(3)); await owner.FlushPlaybackUpdatesAsync();
            Assert.That(audio.PlayCalls, Is.Zero); Assert.That(owner.CanPlay, Is.False);
            await owner.SettleInstrumentalEditorAsync(); await mapping;
            Assert.That(audio.Snapshot.IsPlaying, Is.False);
        }
        finally { prepared.TrySetResult(); await owner.DeactivateAsync(); }
    }
    [Test]
    public async Task NewestRequestWaitsForOldCleanupAndSamplesReplacesMapping()
    {
        var track = new InstrumentalTrack { KeyFiles = new() { ["C4"] = "piano.wav" } };
        var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
        var mappingEntered = new TaskCompletionSource(); var samplesEntered = new TaskCompletionSource();
        var pianoCalls = 0; var mappingCalls = 0;
        var dialogs = new StubInstrumentalTrackDialogService
        {
            Arrangement = async (_, _) => { pianoCalls++; entered.TrySetResult(); await release.Task; },
            Mapping = async (_, token) => { mappingCalls++; mappingEntered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); }
        };
        var samples = new StubSampleManagementDialogService { Present = async (_, token) => { samplesEntered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); } };
        var owner = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", new ProjectData { Tracks = [track], UninheritedPoints = TimingTestData.Points([0], [120]), ControlPoints = [new()] })) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(),
            sampleFiles: new StubSampleFileService([]), sampleDialog: new StubSampleFileDialogService(Array.Empty<string>()),
            messenger: new WeakReferenceMessenger(), instrumentDialogs: dialogs, sampleManagementDialog: samples);
        try
        {
            await owner.LoadAsync("project.json", default); owner.AcknowledgePresentationReady(owner.LoadGeneration);
            var piano = owner.ViewArrangementCommand.ExecuteAsync(track); await entered.Task;
            var discardedMapping = owner.KeysMappingCommand.ExecuteAsync(track);
            var newerPiano = owner.ViewArrangementCommand.ExecuteAsync(track);
            var newestMapping = owner.KeysMappingCommand.ExecuteAsync(track);
            Assert.That(mappingCalls, Is.Zero);
            release.SetResult(); await mappingEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(pianoCalls, Is.EqualTo(1)); Assert.That(mappingCalls, Is.EqualTo(1));
            Assert.That(owner.CanPlay, Is.False);
            var manager = owner.ManageSamplesCommand.ExecuteAsync(null);
            await samplesEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(owner.CanPlay, Is.True);
            await owner.SettleInstrumentalEditorAsync();
            await Task.WhenAll(piano, discardedMapping, newerPiano, newestMapping, manager);
        }
        finally { release.TrySetResult(); await owner.DeactivateAsync(); }
    }

    [Test]
    public async Task MappingFailureReleasesGateAndOldContextCannotMutateNewProject()
    {
        var track = new InstrumentalTrack { KeyFiles = new() { ["C4"] = "piano.wav" } };
        InstrumentalTrackContext? old = null;
        var dialogs = new StubInstrumentalTrackDialogService { Mapping = (data, _) => { old = data.Context; throw new IOException("present failed"); } };
        var messenger = new WeakReferenceMessenger();
        var owner = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", new ProjectData { Tracks = [track], UninheritedPoints = TimingTestData.Points([0], [120]), ControlPoints = [new()] })) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), messenger: messenger, instrumentDialogs: dialogs);
        try
        {
            await owner.LoadAsync("project.json", default); owner.AcknowledgePresentationReady(owner.LoadGeneration);
            await owner.KeysMappingCommand.ExecuteAsync(track);
            Assert.That(owner.ErrorMessage, Is.EqualTo("present failed"));
            Assert.That(owner.CanPlay, Is.True);
            await owner.LoadAsync("replacement.json", default); owner.AcknowledgePresentationReady(owner.LoadGeneration);
            messenger.Send(new Hitbench.Resources.Messages.PlaceInstrumentalNoteRequestedMessage(old!, Guid.NewGuid(), "C4", 125, 4));
            Assert.That(track.Notes, Is.Empty);
        }
        finally { await owner.DeactivateAsync(); }
    }
    [Test]
    public async Task PianoKeepsPlaybackAndMappingPausesWithoutAutoResume()
    {
        var track = new InstrumentalTrack { KeyFiles = new() { ["C4"] = "piano.wav" } };
        var audio = new StubWorkstationAudioService(); var dialogs = new StubInstrumentalTrackDialogService();
        var opened = new TaskCompletionSource<InstrumentalEditorContext>();
        var mappingOpened = new TaskCompletionSource<InstrumentalTrackSnapshot>();
        dialogs.Arrangement = async (data, token) => { opened.TrySetResult(data); await Task.Delay(Timeout.Infinite, token); };
        dialogs.Mapping = async (data, token) => { mappingOpened.TrySetResult(data); await Task.Delay(Timeout.Infinite, token); };
        var owner = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(
            new WorkstationSource("map", "song", new ProjectData { Tracks = [track], UninheritedPoints = TimingTestData.Points([0], [120]), ControlPoints = [new()] })) },
            new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler(), messenger: new WeakReferenceMessenger(), instrumentDialogs: dialogs);
        try
        {
            await owner.LoadAsync("project.json", default); owner.AcknowledgePresentationReady(owner.LoadGeneration);
            await owner.PlayPauseCommand.ExecuteAsync(null);
            var piano = owner.ViewArrangementCommand.ExecuteAsync(track);
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(audio.Snapshot.IsPlaying, Is.True);
            Assert.That(owner.PlayPauseCommand.CanExecute(null), Is.True);
            await owner.SettleInstrumentalEditorAsync(); await piano;
            Assert.That(audio.Snapshot.IsPlaying, Is.True);
            Assert.That(audio.Disposed, Is.False);
            audio.Snapshot = audio.Snapshot with { Position = TimeSpan.FromMilliseconds(1500) };
            var mapping = owner.KeysMappingCommand.ExecuteAsync(track);
            await mappingOpened.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(audio.Snapshot.IsPlaying, Is.False);
            Assert.That(owner.CanPlay, Is.False);
            await owner.PlayPauseCommand.ExecuteAsync(null);
            await owner.SeekCommand.ExecuteAsync(TimeSpan.FromSeconds(2));
            foreach (var command in new[] { owner.StopCommand, owner.NextGridLineCommand, owner.PreviousGridLineCommand, owner.NextWholeBeatCommand, owner.PreviousWholeBeatCommand })
            { Assert.That(command.CanExecute(null), Is.False); await command.ExecuteAsync(null); }
            Assert.That(audio.Snapshot.Position, Is.EqualTo(TimeSpan.FromMilliseconds(1500)));
            Assert.That(audio.Snapshot.IsPlaying, Is.False);
            await owner.SettleInstrumentalEditorAsync();
            await Task.WhenAll(piano, mapping);
            Assert.That(owner.CanPlay, Is.True);
            Assert.That(audio.Snapshot.IsPlaying, Is.False);
            Assert.That(audio.Disposed, Is.False);
        }
        finally { await owner.DeactivateAsync(); }
    }

    [Test]
    public async Task ReinvokingSameDrawerRetainsSession()
    {
        var track = new InstrumentalTrack { KeyFiles = new() { ["C4"] = "piano.wav" } };
        var calls = 0; var opened = new TaskCompletionSource();
        var dialogs = new StubInstrumentalTrackDialogService { Arrangement = async (_, token) =>
        { calls++; opened.TrySetResult(); await Task.Delay(Timeout.Infinite, token); } };
        var owner = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(
            new WorkstationSource("map", "song", new ProjectData { Tracks = [track], UninheritedPoints = TimingTestData.Points([0], [120]), ControlPoints = [new()] })) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), messenger: new WeakReferenceMessenger(), instrumentDialogs: dialogs);
        try
        {
            await owner.LoadAsync("project.json", default); owner.AcknowledgePresentationReady(owner.LoadGeneration);
            var first = owner.ViewArrangementCommand.ExecuteAsync(track); await opened.Task;
            Assert.That(owner.ViewArrangementCommand.CanExecute(track), Is.True);
            await owner.ViewArrangementCommand.ExecuteAsync(track).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(calls, Is.EqualTo(1));
            await owner.SettleInstrumentalEditorAsync(); await first;
        }
        finally { await owner.DeactivateAsync(); }
    }
}
