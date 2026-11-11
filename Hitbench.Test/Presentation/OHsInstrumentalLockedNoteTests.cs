using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Core.Entities;
using Hitbench.Resources.DataModels;
using Hitbench.Resources.Messages;
using Hitbench.Services;
using Hitbench.ViewModels.Shell;

namespace Hitbench.Test.Presentation;

public class OHsInstrumentalLockedNoteTests
{
    [Test]
    public async Task ForgedReplacementCannotUnlockResident()
    {
        var track = new InstrumentalTrack { Notes = [new() { Key = "C4", Timestamp = -100, IsReadOnly = true }, new() { Key = "D4", Timestamp = 1100 }, new() { Key = "E4", Timestamp = 10000, IsReadOnly = true }] };
        var messenger = new WeakReferenceMessenger();
        var dialogs = new StubInstrumentalTrackDialogService();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", new() { Tracks = [track], UninheritedPoints = TimingTestData.Points([0], [120]) })) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), messenger: messenger, instrumentDialogs: dialogs);
        InstrumentalEditResultMessage? result = null;
        messenger.Register<InstrumentalEditResultMessage>(this, (_, m) => result = m);
        var opened = false;
        dialogs.Arrangement = (context, _) =>
        {
            opened = true;
            Assert.That(context.Track.Notes[0].IsReadOnly, Is.True);
            var resident = context.Track.Notes[0];
            messenger.Send(new ReplaceInstrumentalNotesRequestedMessage(context.Track.Context, Guid.NewGuid(), context.Track.Revision,
                context.Track.Notes.Select(n => n.Id == resident.Id ? n with { IsReadOnly = false } : n).ToArray(), [resident.Id]));
            Assert.That(result!.Changed, Is.False);
            messenger.Send(new RemoveInstrumentalNoteRequestedMessage(context.Track.Context, Guid.NewGuid(), resident.Id));
            Assert.That(track.Notes, Has.Count.EqualTo(3));
            messenger.Send(new PlaceInstrumentalNoteRequestedMessage(context.Track.Context, Guid.NewGuid(), "F4", 125, 4));
            Assert.That(result!.Changed, Is.True);
            messenger.Send(new ReplayInstrumentalNoteHistoryRequestedMessage(context.Track.Context, Guid.NewGuid(), false));
            Assert.That(track.Notes, Has.Count.EqualTo(3));
            messenger.Send(new ReplayInstrumentalNoteHistoryRequestedMessage(context.Track.Context, Guid.NewGuid(), true));
            Assert.That(track.Notes, Has.Count.EqualTo(4));
            return Task.CompletedTask;
        };
        try
        {
            await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            Assert.That(vm.ViewArrangementCommand.CanExecute(track), Is.True);
            await vm.ViewArrangementCommand.ExecuteAsync(track);
            Assert.That(opened, Is.True);
            vm.SelectedSubdivisionIndex = 3;
            vm.ResnapAllSamplesCommand.Execute(null);
            Assert.That(track.Notes.Where(n => n.IsReadOnly).Select(n => n.Timestamp), Is.EqualTo(new[] { -100, 10000 }));
            Assert.That(track.Notes.Single(n => n.Key == "D4").Timestamp, Is.EqualTo(1125));
        }
        finally { await vm.DeactivateAsync(); messenger.UnregisterAll(this); }
    }
}
