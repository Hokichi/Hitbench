using Hitbench.Services.ProjectFiles;
using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.Messages;
using Hitbench.Resources.DataModels;
using Hitbench.Services;
using Hitbench.ViewModels.Shell;
using Hitbench.Core.Entities;

namespace Hitbench.Test.Presentation;

public sealed class WorkstationSampleManagementTests
{
    private static WorkstationPageVM CreateOwner(StubSampleManagementDialogService manager,
        StubInstrumentalTrackDialogService? dialogs = null, ProjectData? project = null, WeakReferenceMessenger? messenger = null) =>
        new(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song",
                project ?? new ProjectData { Offsets = [0], BPMs = [120], ControlPoints = [new()] })) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(),
            new StubSampleFileService(["piano.wav"]), new StubSampleFileDialogService([]),
            messenger: messenger ?? new WeakReferenceMessenger(), sampleManagementDialog: manager, instrumentDialogs: dialogs);

    [Test]
    public async Task SamplesCommandReturnsWhilePresentationRemainsOpen()
    {
        var opened = new TaskCompletionSource(); var closed = new TaskCompletionSource(); var stopped = new TaskCompletionSource();
        var manager = new StubSampleManagementDialogService
        {
            Present = async (_, token) => { opened.TrySetResult(); try { await closed.Task.WaitAsync(token); } finally { stopped.TrySetResult(); } },
            Close = (_, _) => { closed.TrySetResult(); return Task.FromResult(true); }
        };
        var vm = CreateOwner(manager);
        try
        {
            await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            var request = vm.ManageSamplesCommand.ExecuteAsync(null); await opened.Task;
            Assert.That(request.IsCompletedSuccessfully, Is.True);
            Assert.That(vm.IsSamplesOpen, Is.True);
            Assert.That(vm.ManageSamplesCommand.CanExecute(null), Is.True);
            Assert.That(stopped.Task.IsCompleted, Is.False);
            await vm.ManageSamplesCommand.ExecuteAsync(null);
            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await vm.SettleWorkstationDrawersAsync();
            Assert.That(vm.IsSamplesOpen, Is.False);
        }
        finally { closed.TrySetResult(); await vm.DeactivateAsync(); }
    }

    [TestCase(true)] [TestCase(false)]
    public async Task RapidSamplesRequestsSettleLatestDesiredState(bool reopen)
    {
        var first = new TaskCompletionSource(); var next = new TaskCompletionSource(); var exiting = new TaskCompletionSource();
        var exit = new TaskCompletionSource(); var closed = new TaskCompletionSource();
        var calls = 0; var live = 0; var peak = 0;
        var manager = new StubSampleManagementDialogService
        {
            Present = async (_, token) =>
            {
                calls++; live++; peak = Math.Max(peak, live);
                var completion = closed;
                if (calls == 1) first.TrySetResult(); else next.TrySetResult();
                try { await completion.Task.WaitAsync(token); } finally { live--; }
            },
            Close = async (_, token) => { var completion = closed; exiting.TrySetResult(); await exit.Task.WaitAsync(token); completion.TrySetResult(); return true; }
        };
        var vm = CreateOwner(manager);
        try
        {
            await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            await vm.ManageSamplesCommand.ExecuteAsync(null); await first.Task;
            await vm.ManageSamplesCommand.ExecuteAsync(null); await exiting.Task;
            await vm.ManageSamplesCommand.ExecuteAsync(null);
            if (!reopen) await vm.ManageSamplesCommand.ExecuteAsync(null);
            var old = closed; closed = new(); exit.TrySetResult(); old.TrySetResult();
            if (reopen) await next.Task.WaitAsync(TimeSpan.FromSeconds(3));
            else await vm.SettleWorkstationDrawersAsync();
            Assert.That(calls, Is.EqualTo(reopen ? 2 : 1));
            Assert.That(peak, Is.EqualTo(1));
            Assert.That(vm.IsSamplesOpen, Is.EqualTo(reopen));
        }
        finally { exit.TrySetResult(); closed.TrySetResult(); await vm.DeactivateAsync(); }
        Assert.That(live, Is.Zero);
    }

    [Test]
    public async Task SamplesKeepsArrangementContextAndMessagesActive()
    {
        var track = new InstrumentalTrack { KeyFiles = new() { ["C4"] = "piano.wav" } };
        var messenger = new WeakReferenceMessenger();
        var arrangement = new TaskCompletionSource<InstrumentalTrackContext>();
        var samples = new TaskCompletionSource(); var samplesClosed = new TaskCompletionSource();
        var calls = 0; CancellationToken pianoToken = default;
        var dialogs = new StubInstrumentalTrackDialogService { Arrangement = async (data, token) =>
        { calls++; pianoToken = token; arrangement.TrySetResult(data.Track.Context); await Task.Delay(Timeout.Infinite, token); } };
        var manager = new StubSampleManagementDialogService
        {
            Present = (_, token) => { samples.TrySetResult(); return samplesClosed.Task.WaitAsync(token); },
            Close = (_, _) => { samplesClosed.TrySetResult(); return Task.FromResult(true); }
        };
        var vm = CreateOwner(manager, dialogs, new() { Tracks = [track], Offsets = [0], BPMs = [120] }, messenger);
        try
        {
            await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            var editing = vm.ViewArrangementCommand.ExecuteAsync(track); var context = await arrangement.Task;
            await vm.ManageSamplesCommand.ExecuteAsync(null); await samples.Task;
            Assert.That(pianoToken.IsCancellationRequested, Is.False);
            messenger.Send(new PlaceInstrumentalNoteRequestedMessage(context, Guid.NewGuid(), "C4", 125, 4));
            Assert.That(track.Notes, Has.Count.EqualTo(1));
            await vm.ManageSamplesCommand.ExecuteAsync(null);
            await samplesClosed.Task;
            messenger.Send(new PlaceInstrumentalNoteRequestedMessage(context, Guid.NewGuid(), "C4", 250, 4));
            Assert.That(track.Notes, Has.Count.EqualTo(2));
            Assert.That(calls, Is.EqualTo(1)); Assert.That(editing.IsCompleted, Is.False);
            await vm.SettleInstrumentalEditorAsync(); await editing;
        }
        finally { samplesClosed.TrySetResult(); await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task RefusedToggleAndMappingPreserveBothSessionsAndCanRetry()
    {
        var track = new InstrumentalTrack { KeyFiles = new() { ["C4"] = "piano.wav" } };
        var opened = new TaskCompletionSource(); var closed = new TaskCompletionSource(); var mapped = new TaskCompletionSource();
        var piano = new TaskCompletionSource(); var accept = false; CancellationToken sampleToken = default, pianoToken = default;
        var manager = new StubSampleManagementDialogService
        {
            Present = (_, token) => { sampleToken = token; opened.TrySetResult(); return closed.Task.WaitAsync(token); },
            Close = (_, _) => { if (accept) closed.TrySetResult(); return Task.FromResult(accept); }
        };
        var dialogs = new StubInstrumentalTrackDialogService
        {
            Arrangement = (_, token) => { pianoToken = token; piano.TrySetResult(); return Task.Delay(Timeout.Infinite, token); },
            Mapping = (_, token) => { mapped.TrySetResult(); return Task.Delay(Timeout.Infinite, token); }
        };
        var vm = CreateOwner(manager, dialogs, new() { Tracks = [track], Offsets = [0], BPMs = [120] });
        try
        {
            await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            var editing = vm.ViewArrangementCommand.ExecuteAsync(track); await piano.Task;
            await vm.ManageSamplesCommand.ExecuteAsync(null); await opened.Task;
            await vm.ManageSamplesCommand.ExecuteAsync(null);
            Assert.That(vm.IsSamplesOpen, Is.True); Assert.That(sampleToken.IsCancellationRequested, Is.False);
            await vm.KeysMappingCommand.ExecuteAsync(track).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(pianoToken.IsCancellationRequested, Is.False); Assert.That(mapped.Task.IsCompleted, Is.False);
            accept = true;
            var mapping = vm.KeysMappingCommand.ExecuteAsync(track); await mapped.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(vm.IsSamplesOpen, Is.False); Assert.That(vm.CanPlay, Is.False);
            await vm.SettleWorkstationDrawersAsync(); await Task.WhenAll(editing, mapping);
        }
        finally { closed.TrySetResult(); await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task ArrangementReplacementLeavesSamplesOpen()
    {
        var track = new InstrumentalTrack { KeyFiles = new() { ["C4"] = "piano.wav" } };
        var other = new InstrumentalTrack { KeyFiles = new() { ["C4"] = "piano.wav" } };
        var first = new TaskCompletionSource(); var next = new TaskCompletionSource(); var samples = new TaskCompletionSource();
        CancellationToken sampleToken = default;
        var manager = new StubSampleManagementDialogService { Present = (_, token) =>
        { sampleToken = token; samples.TrySetResult(); return Task.Delay(Timeout.Infinite, token); } };
        var dialogs = new StubInstrumentalTrackDialogService { Arrangement = (data, token) =>
        { if (data.Track.Name == "Next") next.TrySetResult(); else first.TrySetResult(); return Task.Delay(Timeout.Infinite, token); } };
        other.Name = "Next";
        var vm = CreateOwner(manager, dialogs, new() { Tracks = [track, other], Offsets = [0], BPMs = [120] });
        try
        {
            await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            var editing = vm.ViewArrangementCommand.ExecuteAsync(track); await first.Task;
            await vm.ManageSamplesCommand.ExecuteAsync(null); await samples.Task;
            var replacement = vm.ViewArrangementCommand.ExecuteAsync(other); await next.Task;
            await vm.SettleInstrumentalEditorAsync(); await Task.WhenAll(editing, replacement);
            Assert.That(sampleToken.IsCancellationRequested, Is.False); Assert.That(vm.IsSamplesOpen, Is.True);
        }
        finally { await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task ProjectSettlementInvalidatesBothRequestStreams()
    {
        var opened = new TaskCompletionSource(); var closing = new TaskCompletionSource(); var release = new TaskCompletionSource(); var calls = 0;
        var manager = new StubSampleManagementDialogService
        {
            Present = (_, token) => { calls++; opened.TrySetResult(); return Task.Delay(Timeout.Infinite, token); },
            Close = async (_, token) => { closing.TrySetResult(); await release.Task.WaitAsync(token); return true; }
        };
        var vm = CreateOwner(manager);
        try
        {
            await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            await vm.ManageSamplesCommand.ExecuteAsync(null); await opened.Task;
            await vm.ManageSamplesCommand.ExecuteAsync(null); await closing.Task;
            await vm.ManageSamplesCommand.ExecuteAsync(null);
            await vm.DeactivateAsync().WaitAsync(TimeSpan.FromSeconds(3));
            release.TrySetResult();
            Assert.That(calls, Is.EqualTo(1)); Assert.That(vm.IsSamplesOpen, Is.False);
            Assert.That(vm.ManageSamplesCommand.CanExecute(null), Is.False);
        }
        finally { release.TrySetResult(); await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task RefusedMappingThenNaturalArrangementCloseCanReopen()
    {
        var track = new InstrumentalTrack { KeyFiles = new() { ["C4"] = "piano.wav" } };
        var opened = new TaskCompletionSource(); var samples = new TaskCompletionSource(); var closePiano = new TaskCompletionSource();
        var calls = 0;
        var manager = new StubSampleManagementDialogService { Present = (_, token) =>
        { samples.TrySetResult(); return Task.Delay(Timeout.Infinite, token); } };
        var dialogs = new StubInstrumentalTrackDialogService { Arrangement = (_, token) =>
        { calls++; opened.TrySetResult(); return closePiano.Task.WaitAsync(token); } };
        var vm = CreateOwner(manager, dialogs, new() { Tracks = [track], Offsets = [0], BPMs = [120] });
        try
        {
            await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            var editing = vm.ViewArrangementCommand.ExecuteAsync(track); await opened.Task;
            await vm.ManageSamplesCommand.ExecuteAsync(null); await samples.Task;
            await vm.KeysMappingCommand.ExecuteAsync(track);
            closePiano.TrySetResult(); await editing;
            await vm.ViewArrangementCommand.ExecuteAsync(track);
            Assert.That(calls, Is.EqualTo(2));
        }
        finally { closePiano.TrySetResult(); await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task SamplesRequestSupersedesPendingMappingWithoutCancellingArrangement()
    {
        var track = new InstrumentalTrack { KeyFiles = new() { ["C4"] = "piano.wav" } };
        var opened = new TaskCompletionSource(); var samples = new TaskCompletionSource(); var exiting = new TaskCompletionSource();
        var exit = new TaskCompletionSource(); var closed = new TaskCompletionSource();
        var reopened = new TaskCompletionSource();
        CancellationToken pianoToken = default; var mappings = 0; var sampleCalls = 0;
        var manager = new StubSampleManagementDialogService
        {
            Present = (_, token) =>
            {
                if (++sampleCalls > 1) { reopened.TrySetResult(); return Task.Delay(Timeout.Infinite, token); }
                samples.TrySetResult(); return closed.Task.WaitAsync(token);
            },
            Close = async (_, token) => { exiting.TrySetResult(); await exit.Task.WaitAsync(token); closed.TrySetResult(); return true; }
        };
        var dialogs = new StubInstrumentalTrackDialogService
        {
            Arrangement = (_, token) => { pianoToken = token; opened.TrySetResult(); return Task.Delay(Timeout.Infinite, token); },
            Mapping = (_, _) => { mappings++; return Task.CompletedTask; }
        };
        var vm = CreateOwner(manager, dialogs, new() { Tracks = [track], Offsets = [0], BPMs = [120] });
        try
        {
            await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            var editing = vm.ViewArrangementCommand.ExecuteAsync(track); await opened.Task;
            await vm.ManageSamplesCommand.ExecuteAsync(null); await samples.Task;
            var mapping = vm.KeysMappingCommand.ExecuteAsync(track); await exiting.Task;
            await vm.ManageSamplesCommand.ExecuteAsync(null);
            exit.TrySetResult(); await mapping.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(pianoToken.IsCancellationRequested, Is.False);
            Assert.That(mappings, Is.Zero);
            await reopened.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(vm.IsSamplesOpen, Is.True);
        }
        finally { exit.TrySetResult(); closed.TrySetResult(); await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task ScopedNotificationsKeepRootChoicesAndNestedInstrumentAvailabilitySeparate()
    {
        var instrument = new Hitbench.Core.Entities.InstrumentalTrack
        {
            KeyFiles = new() { ["C3"] = "Piano/C3.wav" }, Notes = [new() { Key = "C3", Timestamp = 125 }]
        };
        var sample = new Hitbench.Core.Entities.Track { SelectedSampleFileName = "Piano/C3.wav" };
        var project = new Hitbench.Core.Entities.ProjectData { Tracks = [sample, instrument], Offsets = [0], BPMs = [120] };
        var messenger = new WeakReferenceMessenger();
        var audio = new StubWorkstationAudioService();
        var manager = new StubSampleManagementDialogService();
        var dialogs = new StubInstrumentalTrackDialogService();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new Hitbench.Core.Entities.WorkstationSource("map", "song", project)) },
            new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler(), new StubSampleFileService(["kick.wav", "Piano/C3.wav"]),
            new StubSampleFileDialogService([]), messenger: messenger, sampleManagementDialog: manager, instrumentDialogs: dialogs);
        try
        {
            await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;
            Assert.That(vm.SampleFiles, Is.EqualTo(new[] { "kick.wav" }));
            Assert.That(sample.SelectedSampleFileName, Is.EqualTo("Piano/C3.wav"));
            Assert.That(vm.GetSampleAvailability(sample), Is.EqualTo(TrackSampleAvailability.Missing));
            manager.Present = (context, _) =>
            {
                messenger.Send(new ProjectSamplesChangedMessage(context, ["snare.wav", "Piano/C3.wav"]));
                return Task.CompletedTask;
            };
            await vm.ManageSamplesCommand.ExecuteAsync(null);
            Assert.That(vm.SampleFiles, Is.EqualTo(new[] { "snare.wav" }));
            manager.Present = (context, _) =>
            {
                messenger.Send(new ProjectSamplesChangedMessage(context, ["hat.wav"], includesSubfolders: false));
                return Task.CompletedTask;
            };
            await vm.ManageSamplesCommand.ExecuteAsync(null);
            Assert.That(vm.SampleFiles, Is.EqualTo(new[] { "hat.wav" }));
            dialogs.Mapping = (snapshot, _) =>
            {
                Assert.That(snapshot.AvailableFiles, Does.Contain("Piano/C3.wav"));
                return Task.CompletedTask;
            };
            await vm.KeysMappingCommand.ExecuteAsync(instrument);
            await vm.FlushPlaybackUpdatesAsync();
            Assert.That(audio.SampleUpdates.Last().Single().FileName, Is.EqualTo("Piano/C3.wav"));
        }
        finally { await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task OpeningManagerPreservesPlayingSnapshotAndTransport()
    {
        var audio = new StubWorkstationAudioService(); var closed = new TaskCompletionSource(); var presented = new TaskCompletionSource();
        var manager = new StubSampleManagementDialogService { Present = (_, token) => { presented.SetResult(); return closed.Task.WaitAsync(token); } };
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler(),
            new StubSampleFileService(["hit.wav"]), new StubSampleFileDialogService([]), sampleManagementDialog: manager);
        try
        {
            await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;
            await vm.SeekCommand.ExecuteAsync(TimeSpan.FromSeconds(3)); await vm.PlayPauseCommand.ExecuteAsync(null);
            var managing = vm.ManageSamplesCommand.ExecuteAsync(null); await presented.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(audio.Snapshot.IsPlaying, Is.True); Assert.That(audio.Snapshot.Position, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(vm.PlayPauseCommand.CanExecute(null), Is.True); Assert.That(vm.StopCommand.CanExecute(null), Is.True);
            Assert.That(vm.SeekCommand.CanExecute(TimeSpan.FromSeconds(4)), Is.True);
            Assert.That(vm.ManageSamplesCommand.CanExecute(null), Is.True); Assert.That(vm.CanImportSample, Is.False);
            await vm.PlayPauseCommand.ExecuteAsync(null); Assert.That(audio.Snapshot.IsPlaying, Is.False);
            await vm.PlayPauseCommand.ExecuteAsync(null); Assert.That(audio.Snapshot.IsPlaying, Is.True);
            closed.SetResult(); await managing;
            Assert.That(audio.Snapshot.IsPlaying, Is.True); Assert.That(audio.Snapshot.Position, Is.EqualTo(TimeSpan.FromSeconds(3)));
        }
        finally { closed.TrySetResult(); await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task OpeningManagerDoesNotCancelPendingPlay()
    {
        var audio = new StubWorkstationAudioService(); var closed = new TaskCompletionSource(); var presented = new TaskCompletionSource();
        var prepared = new TaskCompletionSource(); var preparing = new TaskCompletionSource();
        var manager = new StubSampleManagementDialogService { Present = (_, token) => { presented.SetResult(); return closed.Task.WaitAsync(token); } };
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler(),
            new StubSampleFileService(["hit.wav"]), new StubSampleFileDialogService([]), sampleManagementDialog: manager);
        try
        {
            await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true; await vm.FlushPlaybackUpdatesAsync();
            audio.PrepareSamplesAsync = token => { preparing.TrySetResult(); return prepared.Task.WaitAsync(token); };
            vm.AddSampleTrackCommand.Execute(null);
            Assert.That(vm.PlaceSample(vm.Tracks.Single(), 0), Is.True);
            await preparing.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var playing = vm.PlayPauseCommand.ExecuteAsync(null);
            var managing = vm.ManageSamplesCommand.ExecuteAsync(null); await presented.Task.WaitAsync(TimeSpan.FromSeconds(3));
            prepared.SetResult(); await playing;
            Assert.That(audio.Snapshot.IsPlaying, Is.True);
            closed.SetResult(); await managing; Assert.That(audio.Snapshot.IsPlaying, Is.True);
        }
        finally { prepared.TrySetResult(); closed.TrySetResult(); await vm.DeactivateAsync(); }
    }

    [Test] public async Task PreviousSessionCannotChangeReloadedProject()
    {
        var messenger=new WeakReferenceMessenger(); var manager=new StubSampleManagementDialogService();
        SampleManagementContext? previous=null;
        manager.Present=(context,_)=>{previous=context;return Task.CompletedTask;};
        var vm=new WorkstationPageVM(new StubWorkstationSourceService(),new MarkerPlaybackCoordinator(new StubWorkstationAudioService(),Task.Delay),new ImmediateUiScheduler(),
            new StubSampleFileService(["hit.wav"]),new StubSampleFileDialogService(Array.Empty<string>()),messenger:messenger,sampleManagementDialog:manager);
        await vm.LoadAsync("project.json",default); vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true; await vm.ManageSamplesCommand.ExecuteAsync(null);
        await vm.LoadAsync("project.json",default); vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;
        messenger.Send(new ProjectSamplesChangedMessage(previous!,["obsolete.wav"])); await vm.FlushSavesAsync();
        Assert.That(vm.SampleFiles,Is.EqualTo(new[]{"hit.wav"})); await vm.DeactivateAsync();
        messenger.Send(new ProjectSamplesChangedMessage(previous!,[])); Assert.That(vm.SampleFiles,Is.EqualTo(new[]{"hit.wav"}));
    }
    [Test] public async Task RenameAndDeletionKeepPlacementsAndRestoreAvailability()
    {
        var messenger = new WeakReferenceMessenger(); var files = new StubSampleFileService(["hit.wav", "replacement.wav"]);
        var manager = new StubSampleManagementDialogService();
        var saves = new DelayedProjectFileService(); saves.Release();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), MarkerPlaybackCoordinator.DelayAsync),
            new ImmediateUiScheduler(), files, new StubSampleFileDialogService(Array.Empty<string>()), projectFiles:saves, messenger: messenger, sampleManagementDialog: manager);
        await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;
        vm.AddSampleTrackCommand.Execute(null); var track = vm.Tracks.Single(); vm.PlaceSample(track, 0); var node = track.Samples.Single();
        var renamed = new TaskCompletionSource();
        manager.Present = (context, _) => { messenger.Send(new ProjectSamplesChangedMessage(context, ["kick.wav", "replacement.wav"], "hit.wav", "kick.wav")); renamed.TrySetResult(); return Task.CompletedTask; };
        await vm.ManageSamplesCommand.ExecuteAsync(null);
        await renamed.Task.WaitAsync(TimeSpan.FromSeconds(3)); await vm.FlushSavesAsync();
        Assert.That(track.SelectedSampleFileName, Is.EqualTo("kick.wav")); Assert.That(node.FileName, Is.EqualTo("kick.wav"));
        await vm.FlushSavesAsync(); Assert.That(ProjectDataSerializer.Deserialize(saves.LatestJson!).Tracks.Single().Samples.Single().FileName,Is.EqualTo("kick.wav"));
        var deleted = new TaskCompletionSource();
        manager.Present = (context, _) => { messenger.Send(new ProjectSamplesChangedMessage(context, ["replacement.wav"], "kick.wav")); deleted.TrySetResult(); return Task.CompletedTask; };
        await vm.ManageSamplesCommand.ExecuteAsync(null);
        await deleted.Task.WaitAsync(TimeSpan.FromSeconds(3)); await vm.FlushSavesAsync();
        Assert.That(track.Samples.Single(), Is.SameAs(node)); Assert.That(vm.GetSampleAvailability(track), Is.EqualTo(TrackSampleAvailability.Missing));
        Assert.That(vm.PlaceSample(track, 200), Is.False);
        vm.SelectSample(track, "replacement.wav"); Assert.That(vm.GetSampleAvailability(track), Is.EqualTo(TrackSampleAvailability.Available));
        Assert.That(node.FileName, Is.EqualTo("replacement.wav")); await vm.DeactivateAsync();
    }
}
