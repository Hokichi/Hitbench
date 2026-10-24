using System.Windows;
using System.Windows.Media;
using Onset.Resources.CustomControls;

namespace Onset.Test.Presentation;

[Apartment(ApartmentState.STA)]
public sealed class PianoRollDrawingCacheTests
{
    [Test]
    public void PositionOnlyUpdatesReuseContentAndGeometryInputsInvalidate()
    {
        var roll = new PianoRollView { Duration = TimeSpan.FromSeconds(10), ContentWidth = 1000, Offsets = [0], Bpms = [120] };
        UiTestPump.Layout(roll, 400, 150);
        roll.PrepareDrawing(); var before = roll.ContentBuildCount;
        for (var i = 0; i < 1000; i++) { roll.Position = TimeSpan.FromMilliseconds(i); roll.PrepareDrawing(); }
        Assert.That(roll.ContentBuildCount, Is.EqualTo(before));
        foreach (var change in new Action[] { () => roll.LaneHeight = 40, () => roll.HorizontalOffset = 50,
            () => roll.SelectedNoteIds = [Guid.NewGuid()], () => roll.Foreground = Brushes.Blue,
            () => roll.FontSize = 20, () => roll.IsEnabled = false })
        {
            before = roll.ContentBuildCount; change(); roll.PrepareDrawing();
            Assert.That(roll.ContentBuildCount, Is.EqualTo(before + 1));
        }
    }
}
