using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Onset.Core.Entities;
using Onset.Resources.Messages;
using Onset.Views.Pages;
using Onset.Views.Popups;

namespace Onset.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class OHsControlPointLifecycleTests
{
    [Test]
    public void GlobalKeyRoutingExecutesZoneInputOnce()
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120], ControlPoints = [new() { Timestamp = 1000, Volume = 50 }] };
        var audio = new StubWorkstationAudioService(); var owner = WorkstationControlPointStateTests.Create(project, audio);
        try
        {
            UiTestPump.Run(WorkstationControlPointStateTests.Ready(owner));
            new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
            var page = new WorkstationPage(owner); UiTestPump.Layout(page, 1100, 700);
            var lane = (OHsControlPointLane)page.Timeline.FindName("ControlPointLane");
            lane.PresentEditor = (_, _) => { }; var menus = 0; lane.PresentMenu = (_, _) => menus++;
            var state = owner.ControlPointsState!; var id = state.Points[0].Id; var zone = lane.BringPointIntoView(id)!;
            Assert.That(page.HandlePlaybackKey(Key.Up, ModifierKeys.None, false, zone), Is.True);
            Assert.That(project.ControlPoints[0].Volume, Is.EqualTo(51)); Assert.That(owner.ControlPointsState!.Revision, Is.EqualTo(state.Revision + 1));
            UiTestPump.Layout(page, 1100, 700); zone = lane.BringPointIntoView(id)!;
            Assert.That(page.HandlePlaybackKey(Key.Right, ModifierKeys.Alt, false, zone), Is.True);
            Assert.That(project.ControlPoints[0].Timestamp, Is.EqualTo(1125));
            UiTestPump.Layout(page, 1100, 700); zone = lane.BringPointIntoView(id)!;
            Assert.That(page.HandlePlaybackKey(Key.Apps, ModifierKeys.None, false, zone), Is.True); Assert.That(menus, Is.EqualTo(1));
            Assert.That((audio.PlayCalls, audio.PauseCalls, audio.SeekCalls), Is.EqualTo((0, 0, 0)));
            Assert.That(page.HandlePlaybackKey(Key.Enter, ModifierKeys.None, false, zone), Is.False);
        }
        finally { UiTestPump.Run(owner.DeactivateAsync()); }
    }

    [Test]
    public void TimelineResetDismissesEditorAndPreviewWithoutMutatingPoint()
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120], ControlPoints = [new() { Timestamp = 1000, Volume = 4 }] };
        var owner = WorkstationControlPointStateTests.Create(project, new());
        try
        {
            UiTestPump.Run(WorkstationControlPointStateTests.Ready(owner));
            new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
            var page = new WorkstationPage(owner); UiTestPump.Layout(page, 1100, 700);
            var lane = (OHsControlPointLane)page.Timeline.FindName("ControlPointLane"); lane.PresentEditor = (_, _) => { };
            var point = owner.ControlPointsState!.Points[0];
            Assert.That(lane.OpenEditor(point.Id), Is.True); page.Timeline.Reset();
            Assert.That(lane.IsEditorOpen, Is.False);
            lane.CapturePointer = () => true; lane.ReleasePointer = () => { };
            var left = lane.GetExtents().Single().Left; lane.BeginPointDrag(point.Id, left); lane.UpdatePointDrag(left + 50);
            Assert.That(lane.IsDragPreviewActive, Is.True); page.Timeline.Reset();
            Assert.That(lane.IsDragPreviewActive, Is.False); Assert.That(project.ControlPoints.Single().Timestamp, Is.EqualTo(1000));
        }
        finally { UiTestPump.Run(owner.DeactivateAsync()); }
    }

    [Test]
    public void TagArrowAndPopupInputCannotSeekOrTriggerWorkstationTransport()
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120], ControlPoints = [new() { Timestamp = 0 }] };
        var owner = WorkstationControlPointStateTests.Create(project, new());
        try
        {
            UiTestPump.Run(WorkstationControlPointStateTests.Ready(owner));
            new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
            var page = new WorkstationPage(owner); UiTestPump.Layout(page, 1100, 700);
            var lane = (OHsControlPointLane)page.Timeline.FindName("ControlPointLane"); lane.PresentEditor = (_, _) => { };
            var tag = lane.BringPointIntoView(owner.ControlPointsState!.Points[0].Id)!;
            page.HandlePlaybackKey(Key.Right, ModifierKeys.None, false, tag); UiTestPump.Run(page.PlaybackInputCompletion);
            Assert.That(owner.Position, Is.EqualTo(TimeSpan.Zero));
            lane.OpenEditor(tag.Point!.Id); var editor = (OHsControlPointEditor)lane.FindName("ValueEditor");
            Assert.That(page.HandlePlaybackKey(Key.Space, ModifierKeys.None, false, editor), Is.False);
            Assert.That(owner.IsPlaying, Is.False);
        }
        finally { UiTestPump.Run(owner.DeactivateAsync()); }
    }

    [Test]
    public void EditorClickKeyboardAndRepeatedOpenDoNotDuplicateOrLeakCommands()
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120], ControlPoints = [new() { Timestamp = 500, Volume = 4 }] };
        var owner = WorkstationControlPointStateTests.Create(project, new());
        try
        {
            UiTestPump.Run(WorkstationControlPointStateTests.Ready(owner)); var lane = OHsControlPointDragTests.Connect(owner);
            var id = owner.ControlPointsState!.Points[0].Id; var opens = 0; lane.PresentEditor = (open, _) => { if (open) opens++; };
            var tag = lane.BringPointIntoView(id)!; tag.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.That(lane.IsEditorOpen, Is.True); lane.OpenEditor(id); Assert.That(opens, Is.EqualTo(1));
            var editor = (OHsControlPointEditor)lane.FindName("ValueEditor"); UiTestPump.Layout(editor, 320, 260);
            var input = (TextBox)editor.VolumeControl.Template.FindName("PART_TextBox", editor.VolumeControl);
            input.SelectAll();
            input.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, new TextComposition(InputManager.Current, input, "5"))
                { RoutedEvent = TextCompositionManager.PreviewTextInputEvent });
            input.Text = "5";
            using var source = new InMemoryPresentationSource();
            editor.VolumeControl.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
            Assert.That(project.ControlPoints.Single().Volume, Is.EqualTo(5));
            lane.DismissEditor(); var revision = owner.ControlPointsState!.Revision;
            input.Text = "75"; editor.CommitVolumeInput(true);
            Assert.That(owner.ControlPointsState!.Revision, Is.EqualTo(revision));
        }
        finally { UiTestPump.Run(owner.DeactivateAsync()); }
    }

    [TestCase(4000)][TestCase(5000)]
    public void OffscreenAnchorClosesPopupAndDeactivatedOwnerClearsPendingWork(double offset)
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120], ControlPoints = [new() { Timestamp = 500 }, new() { Timestamp = 4000 }] };
        var owner = WorkstationControlPointStateTests.Create(project, new());
        try
        {
            UiTestPump.Run(WorkstationControlPointStateTests.Ready(owner)); var lane = OHsControlPointDragTests.Connect(owner);
            lane.OpenEditor(owner.ControlPointsState!.Points[0].Id); lane.HorizontalOffset = offset;
            Assert.That(lane.IsEditorOpen, Is.False);
            lane.HorizontalOffset = 0; lane.OpenEditor(owner.ControlPointsState.Points[0].Id);
            UiTestPump.Run(owner.DeactivateAsync());
            Assert.That(lane.IsEditorOpen, Is.False); Assert.That(lane.PendingAdd, Is.Null); Assert.That(lane.IsMenuOwnerActive, Is.False);
        }
        finally { UiTestPump.Run(owner.DeactivateAsync()); }
    }

    [Test]
    public void DismissingValidTypedDraftCommitsWhileInvalidDraftPreservesAcceptedValue()
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120], ControlPoints = [new() { Timestamp = 0, Volume = 4 }] };
        var owner = WorkstationControlPointStateTests.Create(project, new());
        try
        {
            UiTestPump.Run(WorkstationControlPointStateTests.Ready(owner)); var lane = OHsControlPointDragTests.Connect(owner);
            var id = owner.ControlPointsState!.Points[0].Id; lane.OpenEditor(id);
            var editor = (OHsControlPointEditor)lane.FindName("ValueEditor"); UiTestPump.Layout(editor, 320, 260);
            ((TextBox)editor.VolumeControl.Template.FindName("PART_TextBox", editor.VolumeControl)).Text = "25";
            lane.DismissEditor(); Assert.That(project.ControlPoints[0].Volume, Is.EqualTo(25));
            lane.OpenEditor(id); UiTestPump.Layout(editor, 320, 260);
            ((TextBox)editor.VolumeControl.Template.FindName("PART_TextBox", editor.VolumeControl)).Text = "bad";
            lane.DismissEditor(); Assert.That(project.ControlPoints[0].Volume, Is.EqualTo(25));
        }
        finally { UiTestPump.Run(owner.DeactivateAsync()); }
    }

    [Test]
    public void OffscreenEditorWaitsForSharedScrollLayoutBeforePresentation()
    {
        var point = new Onset.Resources.DataModels.ControlPointSnapshot(Guid.NewGuid(), 5000, 50, "Normal");
        var lane = OHsControlPointLaneTests.Create([point]); lane.ContentWidth = 10000; lane.ViewportWidth = 4000;
        UiTestPump.Layout(lane, 4000, 200);
        var opens = 0; double? requested = null;
        lane.PresentEditor = (open, _) => { if (open) opens++; };
        lane.HorizontalOffsetRequested += offset => requested = offset;
        Assert.That(lane.OpenEditor(point.Id), Is.True);
        Assert.That(opens, Is.Zero, "Native Popup must wait until its shared-scrolling anchor is arranged.");
        lane.HorizontalOffset = requested!.Value; UiTestPump.Layout(lane, 4000, 200);
        Assert.That(opens, Is.EqualTo(1)); lane.DismissEditor();
    }

    [Test]
    public void LiveEditKeepsEditorAnchoredToCurrentRecycledTag()
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120], ControlPoints = [new() { Timestamp = 0 }] };
        var owner = WorkstationControlPointStateTests.Create(project, new());
        try
        {
            UiTestPump.Run(WorkstationControlPointStateTests.Ready(owner)); var lane = OHsControlPointDragTests.Connect(owner);
            var state = owner.ControlPointsState!; var id = state.Points[0].Id; lane.OpenEditor(id);
            owner.SetControlPointCommand.Execute(new SetControlPointRequestedMessage(state.Context, id, 25, null));
            UiTestPump.Layout(lane, 4000, 200);
            Assert.That(lane.IsEditorOpen, Is.True); Assert.That(lane.BringPointIntoView(id)!.IsEditing, Is.True);
            lane.DismissEditor();
        }
        finally { UiTestPump.Run(owner.DeactivateAsync()); }
    }

    [Test]
    public void DismissalUsesSurvivingInvokerAfterCommittingDraft()
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120], ControlPoints = [new() { Timestamp = 0 }] };
        var owner = WorkstationControlPointStateTests.Create(project, new());
        try
        {
            UiTestPump.Run(WorkstationControlPointStateTests.Ready(owner)); var lane = OHsControlPointDragTests.Connect(owner);
            OHsControlPointZone? restored = null; lane.PresentEditor = (open, target) => { if (!open) restored = target as OHsControlPointZone; };
            var id = owner.ControlPointsState!.Points[0].Id; lane.OpenEditor(id);
            var editor = (OHsControlPointEditor)lane.FindName("ValueEditor"); UiTestPump.Layout(editor, 320, 260);
            ((TextBox)editor.VolumeControl.Template.FindName("PART_TextBox", editor.VolumeControl)).Text = "25";
            lane.DismissEditor();
            Assert.That(restored, Is.SameAs(lane.BringPointIntoView(id)));
        }
        finally { UiTestPump.Run(owner.DeactivateAsync()); }
    }

    [Test]
    public void OpeningAlreadyVisibleTagDoesNotDisablePlaybackFollow()
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120], ControlPoints = [new() { Timestamp = 0 }] };
        var owner = WorkstationControlPointStateTests.Create(project, new());
        try
        {
            UiTestPump.Run(WorkstationControlPointStateTests.Ready(owner));
            new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
            var page = new WorkstationPage(owner); UiTestPump.Layout(page, 1100, 700);
            var lane = (OHsControlPointLane)page.Timeline.FindName("ControlPointLane"); lane.PresentEditor = (_, _) => { };
            owner.ScrollWithMarker = true; lane.OpenEditor(owner.ControlPointsState!.Points[0].Id);
            Assert.That(owner.ScrollWithMarker, Is.True); lane.DismissEditor();
        }
        finally { UiTestPump.Run(owner.DeactivateAsync()); }
    }
}
