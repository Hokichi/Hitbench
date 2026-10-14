using System.Windows;
using System.Windows.Input;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.Resources.DataModels;
namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
public class PianoRollViewTests
{
    [Test]
    public void DragRetainsGrabOffsetAndCancelDoesNotMutate()
    {
        var note = new InstrumentalNoteSnapshot(Guid.NewGuid(), "C4", 1000, 100);
        var roll = new PianoRollView { Notes = [note], Offsets = [0], Bpms = [120], Duration = TimeSpan.FromSeconds(10), ContentWidth = 1000, HorizontalOffset = 50, VerticalOffset = 70 * 36, KeyColumnWidth = 72, LaneHeight = 36, CanEdit = true, IsAdditionMode = false };
        UiTestPump.Layout(roll, 400, 150);
        var requests = new List<InstrumentalNoteEditRequestedEventArgs>(); roll.NoteEditRequested += (_, e) => requests.Add(e);
        Assert.That(roll.BeginPointerInteraction(new Point(127, 54)), Is.True);
        roll.UpdatePointerPosition(new Point(152, 18));
        Assert.That(requests, Is.Empty);
        Assert.That(roll.CompletePointerInteraction(), Is.True);
        Assert.That(requests.Single().NoteId, Is.EqualTo(note.Id));
        Assert.That(requests.Single().Key, Is.EqualTo("C#4"));
        Assert.That(requests.Single().RequestedMilliseconds, Is.EqualTo(1250).Within(.0001));
        requests.Clear(); roll.BeginPointerInteraction(new Point(127, 54)); roll.UpdatePointerPosition(new Point(177, 90));
        Assert.That(roll.CancelInteraction(), Is.True); Assert.That(roll.CompletePointerInteraction(), Is.False); Assert.That(requests, Is.Empty);
        Assert.That(note.Timestamp, Is.EqualTo(1000));
    }
    [Test]
    public void KeyboardMovesRespectPitchBoundsAndFractionalGrid()
    {
        var note = new InstrumentalNoteSnapshot(Guid.NewGuid(), "B9", 146, 100);
        var roll = new PianoRollView { Notes = [note], SelectedNoteId = note.Id, Offsets = [0], Bpms = [137], Subdivision = 3, Duration = TimeSpan.FromSeconds(10), CanEdit = true, IsAdditionMode = false };
        var requests = new List<InstrumentalNoteEditRequestedEventArgs>(); roll.NoteEditRequested += (_, e) => requests.Add(e);
        roll.HandleEditorKey(Key.Up, ModifierKeys.Alt, false); Assert.That(requests, Is.Empty);
        roll.HandleEditorKey(Key.System, Key.Right, ModifierKeys.Alt, false);
        Assert.That(requests.Single().RequestedMilliseconds, Is.EqualTo(60000d / 137 / 3 * 2).Within(.0001));
        requests.Clear(); roll.HandleEditorKey(Key.Down, ModifierKeys.Alt, false); Assert.That(requests.Single().Key, Is.EqualTo("A#9"));
        requests.Clear(); roll.HandleEditorKey(Key.Delete, ModifierKeys.None, true); Assert.That(requests, Is.Empty);
        roll.HandleEditorKey(Key.Delete, ModifierKeys.None, false); Assert.That(requests.Single().Kind, Is.EqualTo(InstrumentalNoteEditKind.Remove));
    }
    [Test]
    public void ScrollExtentBindingUpdatesAfterResize()
    {
        var roll = new PianoRollView { LaneHeight = 36 };
        var bar = new System.Windows.Controls.Primitives.ScrollBar();
        System.Windows.Data.BindingOperations.SetBinding(bar, System.Windows.Controls.Primitives.ScrollBar.MaximumProperty, new System.Windows.Data.Binding(nameof(PianoRollView.MaximumVerticalOffset)) { Source = roll, Mode = System.Windows.Data.BindingMode.OneWay });
        roll.Measure(new Size(400, 100)); roll.Arrange(new Rect(0, 0, 400, 100));
        roll.Measure(new Size(400, 200)); roll.Arrange(new Rect(0, 0, 400, 200));
        roll.UpdateLayout();
        Assert.That(bar.Maximum, Is.EqualTo(108 * 36 - 200));
    }
    [Test]
    public void HitTestingAccountsForKeyColumnAndScrolling()
    {
        var id = Guid.NewGuid(); var roll = new PianoRollView { Notes = [new(id, "C4", 1000, 100)], Offsets = [0], Bpms = [120], Duration = TimeSpan.FromSeconds(10), ContentWidth = 1000, ViewportWidth = 328, HorizontalOffset = 50, VerticalOffset = 70 * 36, KeyColumnWidth = 72, LaneHeight = 36, CanEdit = true, IsAdditionMode = true };
        roll.Measure(new Size(400, 100)); roll.Arrange(new Rect(0, 0, 400, 100));
        Assert.That(roll.FindNoteAt(new Point(122, 54))?.Id, Is.EqualTo(id));
        Assert.That(roll.FindNoteAt(new Point(20, 54)), Is.Null);
        var requests = 0; roll.NoteEditRequested += (_, _) => requests++;
        Assert.That(roll.HandleEditorKey(Key.Enter, ModifierKeys.None, true), Is.True); Assert.That(requests, Is.Zero);
        Assert.That(roll.HandleEditorKey(Key.Enter, ModifierKeys.None, false), Is.True); Assert.That(requests, Is.EqualTo(1));
    }
}
