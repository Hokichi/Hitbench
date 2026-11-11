using System.Windows;
using System.Windows.Input;
using Hitbench.Resources.CustomControls;
using Hitbench.Resources.DataModels;
using Hitbench.Resources.Helpers;
using Hitbench.Services;
using Hitbench.ViewModels.Shell;
using Hitbench.Views.Pages;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class OHsInstrumentalLockedNoteViewportTests
{
    [Test]
    public void ExtendedBoundsDoNotChangeAudioScale()
    {
        Assert.That(OHsTimelineViewport.GetHorizontalBounds(1000, 200, 0, -100, 1500), Is.EqualTo((-100d, 1300d)));
        Assert.That(OHsTimelineViewport.GetHorizontalBounds(1000, 200, 100, -100, 1500), Is.EqualTo((-200d, 1400d)));
    }

    [Test]
    public void ReadOnlyNotesKeepBoundsButCannotBeHitOrKeyboardEdited()
    {
        var note = new InstrumentalNoteSnapshot(Guid.NewGuid(), "C4", 1000, true);
        var roll = new PianoRollView { Notes = [note], SelectedNoteId = note.Id, IsAdditionMode = false,
            UninheritedPoints = TimingTestData.Points([0], [120]), Duration = TimeSpan.FromSeconds(10), ContentWidth = 1000,
            HorizontalOffset = 50, VerticalOffset = 70 * 36, KeyColumnWidth = 72, LaneHeight = 36 };
        UiTestPump.Layout(roll, 400, 150);
        var bounds = roll.GetNoteBounds(note);
        Assert.That(bounds.IsEmpty, Is.False);
        Assert.That(roll.FindNoteAt(new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2)), Is.Null);
        var edits = 0; roll.NoteEditRequested += (_, _) => edits++;
        roll.HandleEditorKey(Key.Delete, ModifierKeys.None, false);
        roll.HandleEditorKey(Key.Right, ModifierKeys.Alt, false);
        Assert.That(edits, Is.Zero);
        var peer = new PianoRollViewAutomationPeer(roll);
        Assert.That(peer.GetItemStatus(), Does.Contain("read-only: 1"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SharedTimelineReachesExtendedBoundsAndRetainsAudioWidth(bool staticMarker)
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        using var view = new InMemoryView(new Window { Content = page, Width = 1100, Height = 700 });
        try
        {
            UiTestPump.Run(vm.LoadAsync("project.json", default)); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            vm.SetMarkerMode(staticMarker); page.Timeline.ApplyMarkerMode();
            view.UpdateLayout(); view.UpdateLayout();
            var width = page.Timeline.TimelineContentWidth;
            page.Timeline.DisplayStartMilliseconds = -1000;
            page.Timeline.DisplayEndMilliseconds = 15000;
            view.UpdateLayout(); view.UpdateLayout();
            var padding = page.Timeline.TimelineEdgePadding;
            Assert.That(page.Timeline.TimelineContentWidth, Is.EqualTo(width));
            Assert.That(page.Timeline.MinimumHorizontalOffset, Is.EqualTo(-width * .1 - padding).Within(.01));
            Assert.That(page.Timeline.ScrollableWidth, Is.EqualTo(width * 1.5 + padding - page.Timeline.TimelineViewportWidth).Within(.01));
            Assert.That(page.Timeline.TimelineScrollOriginPadding, Is.EqualTo(width * .1 + padding).Within(.01));
            page.Timeline.ScrollFromUser(page.Timeline.ScrollableWidth);
            view.UpdateLayout(); view.UpdateLayout();
            Assert.That(page.Timeline.TimelineHorizontalOffset, Is.EqualTo(page.Timeline.ScrollableWidth).Within(.01));
            Assert.That(audio.SeekCalls, Is.Zero);
            page.Timeline.ClearValue(OHsWorkstationTimeline.DisplayStartMillisecondsProperty);
            page.Timeline.ClearValue(OHsWorkstationTimeline.DisplayEndMillisecondsProperty);
            view.UpdateLayout(); view.UpdateLayout();
            Assert.That(page.Timeline.MinimumHorizontalOffset, Is.EqualTo(-padding).Within(.01));
        }
        finally { UiTestPump.Run(page.DeactivateAsync()); }
    }
}
