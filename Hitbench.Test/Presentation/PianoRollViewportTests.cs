using Hitbench.Test;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.CustomControls;
using Hitbench.Resources.DataModels;
using Hitbench.ViewModels.Popups;
using Hitbench.Views.Pages;
using Hitbench.Views.Popups;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class PianoRollViewportTests
{
    [SetUp]
    public void Setup() => new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();

    [TestCase(200d, 300d, 1000d, 50d)]
    [TestCase(144d, 240d, 2000d, 150d)]
    public void TimeMappingUsesSuppliedKeyWidthAndOffset(double keys, double viewport, double content, double offset)
    {
        var note = new InstrumentalNoteSnapshot(Guid.NewGuid(), "C4", 1000);
        var roll = CreateRoll(keys, viewport, content, offset, [note]);
        var x = keys + note.Timestamp / 10000d * content - offset;
        var bounds = roll.GetNoteBounds(note);
        Assert.That(bounds.Left + bounds.Width / 2, Is.EqualTo(x).Within(.0001));
        Assert.That(roll.FindNoteAt(new Point(x + 1, 54))?.Id, Is.EqualTo(note.Id));
        Assert.That(roll.FindNoteAt(new Point(keys - 1, 54)), Is.Null);
    }

    [Test]
    public void RightPaddingCannotPlaceOrSelectNotes()
    {
        var clipped = new InstrumentalNoteSnapshot(Guid.NewGuid(), "C4", 4000);
        var roll = CreateRoll(200, 300, 1000, 50, [clipped]);
        var requests = 0;
        roll.NoteEditRequested += (_, _) => requests++;
        foreach (var x in new[] { 500d, 540d, 552d })
        {
            Assert.That(roll.FindNoteAt(new Point(x, 54)), Is.Null);
            Assert.That(roll.BeginPointerInteraction(new Point(x, 54)), Is.False);
        }
        Assert.That(roll.CompletePointerInteraction(), Is.False);
        Assert.That(requests, Is.Zero);
    }

    [Test]
    public void KeyboardRevealUsesSharedViewport()
    {
        var roll = CreateRoll(200, 300, 1000, 50, []);
        roll.InitializeActiveCell();
        for (var i = 0; i < 25; i++) roll.HandleEditorKey(Key.Right, ModifierKeys.None, false);
        var activeTime = 500 + 25 * 125;
        Assert.That(roll.HorizontalOffset, Is.EqualTo(activeTime / 10000d * 1000 - 300d / 2).Within(.0001));
    }

    [TestCase(200d, 300d, FlowDirection.LeftToRight)]
    [TestCase(144d, 240d, FlowDirection.LeftToRight)]
    [TestCase(144d, 600d, FlowDirection.LeftToRight)]
    [TestCase(144d, 600d, FlowDirection.RightToLeft)]
    [TestCase(200d, 300d, FlowDirection.RightToLeft)]
    public void AlignmentTracksExplicitViewportChanges(double keys, double viewport, FlowDirection direction)
    {
        var drawer = CreateDrawer();
        drawer.FlowDirection = direction;
        try
        {
            UiTestPump.Layout(drawer, 900, 300);
            var roll = (PianoRollView)drawer.FindName("PianoRoll");
            var transform = roll.TransformToVisual(drawer);
            var bounds = transform.TransformBounds(new Rect(keys, 0, viewport, 100));
            drawer.AlignTimelineViewport(bounds);
            UiTestPump.Layout(drawer, 900, 300);
            Assert.That(drawer.PianoKeyColumnWidth, Is.EqualTo(keys));
            Assert.That(drawer.PianoRollWidth, Is.EqualTo(keys + viewport));
            Assert.That(roll.KeyColumnWidth, Is.EqualTo(keys));
            var updates = 0;
            var descriptor = DependencyPropertyDescriptor.FromProperty(OHsInstrumentalArrangementDrawer.PianoRollWidthProperty, drawer.GetType());
            EventHandler changed = (_, _) => updates++;
            descriptor.AddValueChanged(drawer, changed);
            try
            {
                drawer.AlignTimelineViewport(bounds);
                Assert.That(updates, Is.Zero);
                drawer.AlignTimelineViewport(transform.TransformBounds(new Rect(keys + 12, 0, viewport - 24, 100)));
                Assert.That(drawer.PianoKeyColumnWidth, Is.EqualTo(keys + 12));
                Assert.That(drawer.PianoRollWidth, Is.EqualTo(keys + viewport - 12));
            }
            finally { descriptor.RemoveValueChanged(drawer, changed); }
        }
        finally { drawer.Detach(); UiTestPump.Run(((InstrumentalArrangementDrawerVM)drawer.DataContext).DeactivateAsync()); }
    }

    [Test]
    public void UnavailableViewportClearsStaleGeometry()
    {
        var drawer = CreateDrawer();
        try
        {
            UiTestPump.Layout(drawer, 900, 300);
            var roll = (PianoRollView)drawer.FindName("PianoRoll");
            var bounds = new Rect(roll.TranslatePoint(default, drawer).X + 200, 0, 300, 100);
            drawer.AlignTimelineViewport(bounds);
            foreach (var invalid in new[] { Rect.Empty, new Rect(0, 0, 0, 100), new Rect(0, double.PositiveInfinity, 300, 100) })
            {
                drawer.AlignTimelineViewport(invalid);
                Assert.That(drawer.PianoRollWidth, Is.Zero);
                Assert.That(drawer.PianoKeyColumnWidth, Is.Zero);
            }
            drawer.AlignTimelineViewport(bounds);
            Assert.That(drawer.PianoRollWidth, Is.EqualTo(500));
            Assert.That(new OHsWorkstationTimeline().GetTimeViewportBounds(drawer), Is.EqualTo(Rect.Empty));
        }
        finally { drawer.Detach(); UiTestPump.Run(((InstrumentalArrangementDrawerVM)drawer.DataContext).DeactivateAsync()); }
    }

    private static PianoRollView CreateRoll(double keys, double viewport, double content, double offset, IReadOnlyList<InstrumentalNoteSnapshot> notes)
    {
        var roll = new PianoRollView { KeyColumnWidth = keys, ViewportWidth = viewport, ContentWidth = content,
            HorizontalOffset = offset, Duration = TimeSpan.FromSeconds(10), UninheritedPoints = TimingTestData.Points([0], [120]),
            Subdivision = 4, VerticalOffset = 70 * 36, LaneHeight = 36, CanEdit = true, Notes = notes };
        UiTestPump.Layout(roll, keys + viewport + 100, 150);
        return roll;
    }

    private static OHsInstrumentalArrangementDrawer CreateDrawer()
    {
        var context = new InstrumentalTrackContext("project.json", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var snapshot = new InstrumentalTrackSnapshot(context, "Piano", 100, new Dictionary<string, string>(), [], null, 0);
        return new(new InstrumentalArrangementDrawerVM(InstrumentalArrangementTestContext.Create(snapshot, [0], [120],
            TimeSpan.Zero, 4, 32), new WeakReferenceMessenger(), new ImmediateUiScheduler()));
    }
}
