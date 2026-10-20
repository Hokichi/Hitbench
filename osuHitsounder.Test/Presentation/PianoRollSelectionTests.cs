using System.Windows;
using System.Windows.Input;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.Resources.DataModels;
namespace osuHitsounder.Test.Presentation;
[Apartment(ApartmentState.STA)]
public class PianoRollSelectionTests
{
    [Test]
    public void RightRemovalTargetsOnlyClickedNoteInSelectedFormation()
    {
        var a = new InstrumentalNoteSnapshot(Guid.NewGuid(), "C4", 1000); var b = new InstrumentalNoteSnapshot(Guid.NewGuid(), "C#4", 1250);
        var roll=new PianoRollView{Notes=[a,b],SelectedNoteIds=[a.Id,b.Id],Offsets=[0],Bpms=[120],Duration=TimeSpan.FromSeconds(10),ContentWidth=1000,HorizontalOffset=50,VerticalOffset=70*36};UiTestPump.Layout(roll,400,150);
        InstrumentalNoteEditRequestedEventArgs? request=null;roll.NoteEditRequested+=(_,e)=>request=e;
        Assert.That(roll.RemoveNoteAt(new Point(127,54)),Is.True);Assert.That(request!.NoteIds,Is.EqualTo(new[]{a.Id}));
    }
    [Test]
    public void SnapshotChangeCancelsPendingPlacement()
    {
        var roll=new PianoRollView{Offsets=[0],Bpms=[120],Duration=TimeSpan.FromSeconds(10),ContentWidth=1000};UiTestPump.Layout(roll,400,150);
        var count=0;roll.NoteEditRequested+=(_,_)=>count++;
        roll.BeginPointerInteraction(new Point(122, 54)); roll.Notes = [new(Guid.NewGuid(), "C4", 500)];
        Assert.That(roll.CompletePointerInteraction(),Is.False);Assert.That(count,Is.Zero);
    }
    [Test]
    public void ControlClickAndRectangleSelectionCommitIdsWithoutEdits()
    {
        var a = new InstrumentalNoteSnapshot(Guid.NewGuid(), "C4", 1000); var b = new InstrumentalNoteSnapshot(Guid.NewGuid(), "C#4", 1250);
        var roll=new PianoRollView{Notes=[a,b],SelectedNoteIds=[a.Id],Offsets=[0],Bpms=[120],Duration=TimeSpan.FromSeconds(10),ContentWidth=1000,HorizontalOffset=50,VerticalOffset=70*36,IsAdditionMode=false};UiTestPump.Layout(roll,400,150);
        InstrumentalNoteSelectionRequestedEventArgs? selection=null;var edits=0;roll.NoteSelectionRequested+=(_,e)=>selection=e;roll.NoteEditRequested+=(_,_)=>edits++;
        roll.BeginPointerInteraction(new Point(152,18),ModifierKeys.Control);roll.CompletePointerInteraction();Assert.That(selection!.NoteIds,Is.EquivalentTo(new[]{a.Id,b.Id}));
        roll.BeginPointerInteraction(new Point(100,5));roll.UpdatePointerPosition(new Point(170,80));roll.CompletePointerInteraction();Assert.That(selection.NoteIds,Is.EquivalentTo(new[]{a.Id,b.Id}));Assert.That(edits,Is.Zero);
    }
    [Test]
    public void FocusEntryKeepsViewportAndSelectionModeCannotPlace()
    {
        var roll=new PianoRollView{Offsets=[0],Bpms=[120],Duration=TimeSpan.FromSeconds(10),ContentWidth=1000,VerticalOffset=30*36,HorizontalOffset=50,IsAdditionMode=false};
        UiTestPump.Layout(roll,400,150);var vertical=roll.VerticalOffset;var horizontal=roll.HorizontalOffset;
        roll.InitializeActiveCell(); Assert.That(roll.VerticalOffset,Is.EqualTo(vertical));Assert.That(roll.HorizontalOffset,Is.EqualTo(horizontal));
        var requests=new List<InstrumentalNoteEditRequestedEventArgs>();roll.NoteEditRequested+=(_,e)=>requests.Add(e);
        roll.BeginPointerInteraction(new Point(122,54));roll.CompletePointerInteraction();roll.HandleEditorKey(Key.Enter,ModifierKeys.None,false);
        Assert.That(requests,Is.Empty);
        roll.IsAdditionMode=true;roll.BeginPointerInteraction(new Point(122,54));roll.CompletePointerInteraction();
        Assert.That(requests.Single().Key,Is.EqualTo("E7"));
    }
}
