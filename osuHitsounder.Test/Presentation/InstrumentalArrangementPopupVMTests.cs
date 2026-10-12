using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Resources.Messages;
using osuHitsounder.Services;
using osuHitsounder.ViewModels.Popups;
namespace osuHitsounder.Test.Presentation;

public class InstrumentalArrangementPopupVMTests
{
    [Test]
    public async Task DecoderWarningsRemainVisibleWithUsableSongTransport()
    {
        var context = new InstrumentalTrackContext("project.json", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var snapshot = new InstrumentalTrackSnapshot(context, "Piano", 100, new Dictionary<string, string>(), [], null, 0);
        var audio = new StubWorkstationAudioService();
        audio.PrepareSamplesAsync = _ => { audio.Snapshot = audio.Snapshot with { Warning = "Unreadable sample: piano.wav" }; return Task.CompletedTask; };
        var vm = new InstrumentalArrangementPopupVM(new(snapshot, "song", new(TimeSpan.FromSeconds(10), [.5f]), [0], [120], TimeSpan.Zero, 4, 32, 100, 100, 100), new MarkerPlaybackCoordinator(audio, Task.Delay), new WeakReferenceMessenger(), new ImmediateUiScheduler());
        try { await vm.InitializeAsync(default); Assert.That(vm.PlaybackWarning, Does.Contain("piano.wav")); Assert.That(vm.CanPlay, Is.True); Assert.That(vm.CanEdit, Is.True); }
        finally { await vm.DeactivateAsync(); }
    }
    [Test]
    public async Task ReadinessReplayAndRapidRemappingPrepareNewestSnapshot()
    {
        var context = new InstrumentalTrackContext("project.json", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var id = Guid.NewGuid();
        InstrumentalTrackSnapshot Snapshot(string file, long revision) => new(context, "Piano", 80, new Dictionary<string, string> { ["C4"] = file }, [new(id, "C4", 125, 90)], null, revision);
        var messenger = new WeakReferenceMessenger();
        messenger.Register<InstrumentalEditorReadyMessage>(this, (_, _) => messenger.Send(new InstrumentalTrackChangedMessage(Snapshot("ready.wav", 1))));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var preparing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var audio = new StubWorkstationAudioService { PrepareSamplesAsync = token => { preparing.TrySetResult(); return release.Task.WaitAsync(token); } };
        var vm = new InstrumentalArrangementPopupVM(new(Snapshot("old.wav", 0), "song", new(TimeSpan.FromSeconds(10), [.5f]), [0], [120], TimeSpan.FromSeconds(2), 4, 32, 100, 100, 100), new MarkerPlaybackCoordinator(audio, Task.Delay), messenger, new ImmediateUiScheduler());
        try
        {
            var initialize = vm.InitializeAsync(default); await preparing.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(audio.OpenedPath, Is.EqualTo("song")); Assert.That(audio.SampleUpdates[0].Single().FileName, Is.EqualTo("ready.wav"));
            messenger.Send(new InstrumentalTrackChangedMessage(Snapshot("latest.wav", 2)));
            messenger.Send(new InstrumentalTrackChangedMessage(Snapshot("obsolete.wav", 1)));
            release.SetResult(); await initialize.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(audio.SampleUpdates.Last().Single().FileName, Is.EqualTo("latest.wav"));
            Assert.That(vm.KeyFiles["C4"], Is.EqualTo("latest.wav")); Assert.That(vm.Position, Is.EqualTo(TimeSpan.FromSeconds(2)));
        }
        finally { release.TrySetResult(); await vm.DeactivateAsync(); messenger.UnregisterAll(this); }
    }
    [Test]
    public async Task CancellationDuringPreparationDisposesOutputAndCanBeRepeated()
    {
        var context = new InstrumentalTrackContext("project.json", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var snapshot = new InstrumentalTrackSnapshot(context, "Piano", 100, new Dictionary<string, string>(), [], null, 0);
        var preparing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var audio = new StubWorkstationAudioService { PrepareSamplesAsync = token => { preparing.SetResult(); return Task.Delay(Timeout.Infinite, token); } };
        var vm = new InstrumentalArrangementPopupVM(new(snapshot, "song", new(TimeSpan.FromSeconds(10), [.5f]), [0], [120], TimeSpan.Zero, 4, 32, 100, 100, 100), new MarkerPlaybackCoordinator(audio, Task.Delay), new WeakReferenceMessenger(), new ImmediateUiScheduler());
        var initialize = vm.InitializeAsync(default); await preparing.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await Task.WhenAll(vm.DeactivateAsync(), vm.DeactivateAsync(), initialize).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(audio.Disposed, Is.True); Assert.That(vm.CanEdit, Is.False); Assert.That(vm.CanPlay, Is.False);
    }
    [Test]
    public async Task LocalTransportUsesOnlyCurrentInstrumentAndSaveStatusDoesNotRebuild()
    {
        var ctx = new InstrumentalTrackContext("project.json", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var snap = new InstrumentalTrackSnapshot(ctx, "Piano", 75, new Dictionary<string, string> { ["C4"] = "piano.wav" }, [new(Guid.NewGuid(), "C4", 125, 70)], ["piano.wav"], 0);
        var data = new InstrumentalEditorContext(snap, "song.wav", new(TimeSpan.FromSeconds(10), [.5f]), [0], [120], TimeSpan.FromSeconds(1), 4, 32, 90, 80, 100);
        var messenger = new WeakReferenceMessenger(); var audio = new StubWorkstationAudioService();
        var vm = new InstrumentalArrangementPopupVM(data, new MarkerPlaybackCoordinator(audio, Task.Delay), messenger, new ImmediateUiScheduler());
        try
        {
            await vm.InitializeAsync(default); Assert.That(audio.SampleUpdates.Last().Single().TrackVolume, Is.EqualTo(75));
            var updates = audio.SampleUpdates.Count; messenger.Send(new InstrumentalSaveStateChangedMessage(ctx, 1, true, null));
            Assert.That(audio.SampleUpdates.Count, Is.EqualTo(updates));
            await vm.SeekCommand.ExecuteAsync(TimeSpan.FromSeconds(2)); Assert.That(vm.Position, Is.EqualTo(TimeSpan.FromSeconds(2)));
            await vm.NextGridCommand.ExecuteAsync(null); Assert.That(vm.Position, Is.EqualTo(TimeSpan.FromMilliseconds(2125)));
            await vm.StopCommand.ExecuteAsync(null); Assert.That(vm.Position, Is.EqualTo(TimeSpan.Zero));
        }
        finally { await vm.DeactivateAsync(); }
        Assert.That(audio.Disposed, Is.True);
    }
}
