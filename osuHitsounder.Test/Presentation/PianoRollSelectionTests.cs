using System.Windows;
using System.Windows.Input;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.Resources.DataModels;
namespace osuHitsounder.Test.Presentation;
[Apartment(ApartmentState.STA)]
public class PianoRollSelectionTests
{
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
