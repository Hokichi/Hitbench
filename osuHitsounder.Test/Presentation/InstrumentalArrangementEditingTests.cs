using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Resources.Messages;
using osuHitsounder.Services;
using osuHitsounder.ViewModels.Popups;
namespace osuHitsounder.Test.Presentation;

public class InstrumentalArrangementEditingTests
{
    [Test]
    public async Task SelectionAndCopyPreserveFormationAtMarker()
    {
        var ctx = new InstrumentalTrackContext("project.json", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var a = new InstrumentalNoteSnapshot(Guid.NewGuid(), "C4", 125, 70);
        var b = new InstrumentalNoteSnapshot(Guid.NewGuid(), "E4", 625, 80);
        var snap = new InstrumentalTrackSnapshot(ctx, "Piano", 100, new Dictionary<string,string>(), [a,b], null, 0);
        var messenger = new WeakReferenceMessenger(); ReplaceInstrumentalNotesRequestedMessage? request = null;
        messenger.Register<ReplaceInstrumentalNotesRequestedMessage>(this, (_, m) => request = m);
        var vm = new InstrumentalArrangementPopupVM(new(snap,"song",new(TimeSpan.FromSeconds(10),[.5f]),[0],[120],TimeSpan.Zero,4,32,100,100,100),new MarkerPlaybackCoordinator(new StubWorkstationAudioService(),Task.Delay),messenger,new ImmediateUiScheduler());
        try
        {
            await vm.InitializeAsync(default);
            vm.SetNoteSelection([a.Id,b.Id],a.Id); Assert.That(vm.ScrollWithMarker,Is.False);
            vm.CopyNotesCommand.Execute(null); vm.Position=TimeSpan.FromMilliseconds(9750); vm.PasteNotesCommand.Execute(null);
            Assert.That(request!.DesiredNotes.Count,Is.EqualTo(3));
            var pasted=request.DesiredNotes.Last(); Assert.That(pasted.Key,Is.EqualTo("C4")); Assert.That(pasted.Volume,Is.EqualTo(70)); Assert.That(pasted.Timestamp,Is.EqualTo(9750)); Assert.That(pasted.Id,Is.Not.EqualTo(a.Id));
        }
        finally { await vm.DeactivateAsync(); messenger.UnregisterAll(this); }
    }
}
