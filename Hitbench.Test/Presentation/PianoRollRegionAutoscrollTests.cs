using System.Windows;
using Hitbench.Resources.CustomControls;
namespace Hitbench.Test.Presentation;
[Apartment(ApartmentState.STA)]
public sealed class PianoRollRegionAutoscrollTests
{
    [TestCase("keys")]
    [TestCase("viewport")]
    [TestCase("zoom")]
    public void ChangingSelectionScaleCancelsWithoutEditing(string change)
    {
        var roll = new PianoRollView { IsAdditionMode = false, Offsets = [0], Bpms = [120], Duration = TimeSpan.FromSeconds(10), ContentWidth = 2000 };
        UiTestPump.Layout(roll,400,150);
        var edits = 0; roll.NoteEditRequested += (_, _) => edits++;
        roll.BeginPointerInteraction(new Point(100,30)); roll.UpdatePointerPosition(new Point(450,200));
        if (change == "keys") roll.KeyColumnWidth = double.NaN;
        else if (change == "viewport") roll.ViewportWidth = 200;
        else roll.ZoomFactor = 64;
        Assert.That(roll.CompletePointerInteraction(), Is.False);
        Assert.That(roll.IsRegionAutoScrollActive, Is.False);
        Assert.That(edits, Is.Zero);
    }
    [Test]
    public void SelectionIncludesNotesThatScrolledOutOfView()
    {
        var a = new Hitbench.Resources.DataModels.InstrumentalNoteSnapshot(Guid.NewGuid(), "C4", 1000);
        var b = new Hitbench.Resources.DataModels.InstrumentalNoteSnapshot(Guid.NewGuid(), "C#4", 1250);
        var roll = new PianoRollView { Notes = [a,b], IsAdditionMode = false, Offsets = [0], Bpms = [120], Duration = TimeSpan.FromSeconds(10), ContentWidth = 1000, HorizontalOffset = 50, VerticalOffset = 70 * 36 };
        UiTestPump.Layout(roll, 400, 150);
        Hitbench.Resources.CustomControls.InstrumentalNoteSelectionRequestedEventArgs? selected = null;
        var edits = 0;
        roll.NoteSelectionRequested += (_, args) => selected = args;
        roll.NoteEditRequested += (_, _) => edits++;
        Assert.That(roll.BeginPointerInteraction(new Point(100,5)), Is.True);
        roll.UpdatePointerPosition(new Point(500,200));
        for (var i = 0; i < 30; i++) roll.AdvanceRegionSelection(TimeSpan.FromMilliseconds(50));
        Assert.That(roll.GetNoteBounds(a).Bottom, Is.LessThan(0));
        roll.CompletePointerInteraction();
        Assert.That(selected!.NoteIds, Is.EquivalentTo(new[] { a.Id,b.Id }));
        Assert.That(edits, Is.Zero);
        Assert.That(roll.IsRegionAutoScrollActive, Is.False);
    }
    [Test]
    public void OutsidePointerScrollsBothAxesAndCancelStops()
    {
        var roll = new PianoRollView { IsAdditionMode = false, Offsets = [0], Bpms = [120], Duration = TimeSpan.FromSeconds(10), ContentWidth = 2000, VerticalOffset = 500 };
        UiTestPump.Layout(roll, 400, 150);
        Assert.That(roll.BeginPointerInteraction(new Point(100, 30)), Is.True);
        roll.UpdatePointerPosition(new Point(450, 200));
        roll.AdvanceRegionSelection(TimeSpan.FromMilliseconds(20));
        Assert.That(roll.HorizontalOffset, Is.GreaterThan(0));
        Assert.That(roll.VerticalOffset, Is.GreaterThan(500));
        roll.CancelInteraction();
        var offset = roll.HorizontalOffset;
        roll.AdvanceRegionSelection(TimeSpan.FromMilliseconds(20));
        Assert.That(roll.HorizontalOffset, Is.EqualTo(offset));
    }
}
