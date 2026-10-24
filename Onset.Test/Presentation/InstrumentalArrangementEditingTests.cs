using CommunityToolkit.Mvvm.Messaging;
using Onset.Core.Entities;
using Onset.Resources.DataModels;
using Onset.Resources.Messages;
using Onset.Services;
using Onset.ViewModels.Popups;
namespace Onset.Test.Presentation;

public class InstrumentalArrangementEditingTests
{
    [Test]
    public async Task GroupMovementPreservesSpacingAndRejectsPitchOverflow()
    {
        var ctx=new InstrumentalTrackContext("project.json",null,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid());
        var a = new InstrumentalNoteSnapshot(Guid.NewGuid(), "A9", 146); var b = new InstrumentalNoteSnapshot(Guid.NewGuid(), "B9", 292);
        var snap=new InstrumentalTrackSnapshot(ctx,"Piano",100,new Dictionary<string,string>(),[a,b],null,0);
        var messenger=new WeakReferenceMessenger();var requests=new List<ReplaceInstrumentalNotesRequestedMessage>();messenger.Register<ReplaceInstrumentalNotesRequestedMessage>(this,(_,m)=>requests.Add(m));
        var vm=new InstrumentalArrangementDrawerVM(InstrumentalArrangementTestContext.Create(snap,[0],[137],TimeSpan.Zero,3,32),messenger,new ImmediateUiScheduler());
        try
        {
            await vm.InitializeAsync(default);vm.MoveNotes([a.Id,b.Id],a.Id,"A#9",146);Assert.That(requests,Is.Empty);Assert.That(vm.ErrorMessage,Is.Not.Null);
            vm.MoveNotes([a.Id,b.Id],a.Id,"A9",60000d/137/3*2);var moved=requests.Single().DesiredNotes;
            Assert.That(moved[1].Timestamp - moved[0].Timestamp, Is.EqualTo(146)); Assert.That(moved[0].Timestamp, Is.EqualTo(292)); await vm.DeactivateAsync(); vm.RemoveNotes([a.Id]); Assert.That(requests.Count, Is.EqualTo(1)); Assert.That(vm.SelectAllNotesCommand.CanExecute(null), Is.False);
        }
        finally{await vm.DeactivateAsync();messenger.UnregisterAll(this);}
    }
    [Test]
    public async Task SelectionAndCopyPreserveFormationAtMarker()
    {
        var ctx = new InstrumentalTrackContext("project.json", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var a = new InstrumentalNoteSnapshot(Guid.NewGuid(), "C4", 125);
        var b = new InstrumentalNoteSnapshot(Guid.NewGuid(), "E4", 625);
        var snap = new InstrumentalTrackSnapshot(ctx, "Piano", 100, new Dictionary<string,string>(), [a,b], null, 0);
        var messenger = new WeakReferenceMessenger(); ReplaceInstrumentalNotesRequestedMessage? request = null;
        messenger.Register<ReplaceInstrumentalNotesRequestedMessage>(this, (_, m) => request = m);
        var vm = new InstrumentalArrangementDrawerVM(InstrumentalArrangementTestContext.Create(snap,[0],[120],TimeSpan.Zero,4,32),messenger,new ImmediateUiScheduler());
        try
        {
            await vm.InitializeAsync(default);
            vm.SetNoteSelection([a.Id,b.Id],a.Id);
            vm.CopyNotesCommand.Execute(null); messenger.Send(new OHsInstrumentalTimelineChangedMessage(new(ctx,1,TimeSpan.FromSeconds(10),TimeSpan.FromMilliseconds(9750),false,true,true,[0],[120],32,3,false,null))); vm.PasteNotesCommand.Execute(null);
            Assert.That(request!.DesiredNotes.Count,Is.EqualTo(3));
            var pasted = request.DesiredNotes.Last(); Assert.That(pasted.Key, Is.EqualTo("C4")); Assert.That(pasted.Timestamp, Is.EqualTo(9750)); Assert.That(pasted.Id, Is.Not.EqualTo(a.Id));
        }
        finally { await vm.DeactivateAsync(); messenger.UnregisterAll(this); }
    }
}
