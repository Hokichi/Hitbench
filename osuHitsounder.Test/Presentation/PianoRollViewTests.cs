using System.Windows;
using System.Windows.Input;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.Resources.DataModels;
namespace osuHitsounder.Test.Presentation;
[Apartment(ApartmentState.STA)]
public class PianoRollViewTests
{
 [Test] public void ScrollExtentBindingUpdatesAfterResize()
 {
  var roll=new PianoRollView {LaneHeight=36};
  var bar=new System.Windows.Controls.Primitives.ScrollBar();
  System.Windows.Data.BindingOperations.SetBinding(bar,System.Windows.Controls.Primitives.ScrollBar.MaximumProperty,new System.Windows.Data.Binding(nameof(PianoRollView.MaximumVerticalOffset)){Source=roll,Mode=System.Windows.Data.BindingMode.OneWay});
  roll.Measure(new Size(400,100));roll.Arrange(new Rect(0,0,400,100));
  roll.Measure(new Size(400,200));roll.Arrange(new Rect(0,0,400,200));
  roll.UpdateLayout();
  Assert.That(bar.Maximum,Is.EqualTo(108*36-200));
 }
 [Test] public void HitTestingAccountsForKeyColumnAndScrolling()
 {
  var id=Guid.NewGuid();var roll=new PianoRollView { Notes=[new(id,"C4",1000,100)],Offsets=[0],Bpms=[120],Duration=TimeSpan.FromSeconds(10),ContentWidth=1000,ViewportWidth=328,HorizontalOffset=50,VerticalOffset=70*36,KeyColumnWidth=72,LaneHeight=36,CanEdit=true };
  roll.Measure(new Size(400,100));roll.Arrange(new Rect(0,0,400,100));
  Assert.That(roll.FindNoteAt(new Point(122,54))?.Id,Is.EqualTo(id));
  Assert.That(roll.FindNoteAt(new Point(20,54)),Is.Null);
  var requests=0;roll.NoteEditRequested+=(_,_)=>requests++;
  Assert.That(roll.HandleEditorKey(Key.Enter,ModifierKeys.None,true),Is.True);Assert.That(requests,Is.Zero);
  Assert.That(roll.HandleEditorKey(Key.Enter,ModifierKeys.None,false),Is.True);Assert.That(requests,Is.EqualTo(1));
 }
}
