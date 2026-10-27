using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using Hitbench.Core.Entities;
using Hitbench.Resources.Messages;
using Hitbench.ViewModels.Shell;
using Hitbench.Views.Pages;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class OHsControlPointDragTests
{
    [TestCase("volume", 1000, 100, 1000, 50, true, 0, 75)]
    [TestCase("body horizontal", 1000, 100, 1500, 100, false, 0, 50)]
    [TestCase("left edge", 0, 100, 500, 100, true, 500, 50)]
    [TestCase("right edge", 2000, 100, 2500, 100, true, 2500, 50)]
    public void ZoneGesturesChangeOnlyTheCorrespondingValue(string gesture, double startX, double startY, double endX, double endY, bool commits, int timestamp, int volume)
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120], ControlPoints = [new() { Volume = 50 }, new() { Timestamp = 2000, Volume = 50 }, new() { Timestamp = 8000 }] };
        var files = new DelayedProjectFileService(); var owner = WorkstationControlPointStateTests.Create(project, new(), files);
        try
        {
            UiTestPump.Run(WorkstationControlPointStateTests.Ready(owner)); var lane = Connect(owner);
            lane.Offsets = [0]; lane.Bpms = [120];
            var id = owner.ControlPointsState!.Points[0].Id;
            lane.BeginZoneGesture(id, new Point(startX, startY), 200);
            lane.UpdateZoneGesture(new Point(endX, endY));
            Assert.That(files.Writes, Is.Zero);
            Assert.That(project.ControlPoints[0].Volume, Is.EqualTo(50));
            Assert.That(project.ControlPoints[1].Timestamp, Is.EqualTo(2000));
            Assert.That(lane.CompleteZoneGesture(), Is.EqualTo(commits));
            Assert.That(files.Writes, Is.EqualTo(commits ? 1 : 0));
            var changed = project.ControlPoints[gesture == "right edge" ? 1 : 0];
            Assert.That((changed.Timestamp, changed.Volume), Is.EqualTo((timestamp, volume)));
            Assert.That(lane.CompleteZoneGesture(), Is.False);
        }
        finally { files.Release(); UiTestPump.Run(owner.DeactivateAsync()); }
    }

    [Test]
    public void NoOpOrStaleBoundaryDropDoesNotSave()
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120], ControlPoints = [new() { Timestamp = 500 }, new() { Timestamp = 2000 }] };
        var files = new DelayedProjectFileService(); var owner = WorkstationControlPointStateTests.Create(project, new(), files);
        try
        {
            UiTestPump.Run(WorkstationControlPointStateTests.Ready(owner)); var lane = Connect(owner);
            lane.Offsets = [0]; lane.Bpms = [120];
            var id = owner.ControlPointsState!.Points[0].Id;
            lane.BeginZoneGesture(id, new Point(500, 10), 200);
            lane.UpdateZoneGesture(new Point(510, 10));
            Assert.That(lane.CompleteZoneGesture(), Is.False);
            Assert.That(files.Writes, Is.Zero);
            lane.BeginZoneGesture(id, new Point(500, 10), 200);
            lane.UpdateZoneGesture(new Point(1000, 10));
            var state = owner.ControlPointsState!;
            owner.SetControlPointCommand.Execute(new Hitbench.Resources.Messages.SetControlPointRequestedMessage(state.Context, id, 60, null, state.Revision));
            var writes = files.Writes;
            Assert.That(lane.CompleteZoneGesture(), Is.False);
            Assert.That(files.Writes, Is.EqualTo(writes));
            Assert.That(project.ControlPoints[0].Timestamp, Is.EqualTo(500));
        }
        finally { files.Release(); UiTestPump.Run(owner.DeactivateAsync()); }
    }

    internal static OHsControlPointLane Connect(WorkstationPageVM owner)
    {
        var lane = OHsControlPointLaneTests.Create(owner.ControlPointsState!.Points);
        lane.ContentWidth = 10000; lane.ViewportWidth = 4000;
        lane.Offsets = [0, 1000]; lane.Bpms = [120, 240]; lane.Subdivision = 4;
        lane.PresentEditor = (_, _) => { }; lane.CapturePointer = () => true; lane.ReleasePointer = () => { };
        lane.AddCommand = owner.AddControlPointCommand; lane.CommitAddCommand = owner.CommitControlPointAddCommand;
        lane.SetPointCommand = owner.SetControlPointCommand; lane.MovePointCommand = owner.MoveControlPointCommand;
        lane.DeletePointCommand = owner.DeleteControlPointCommand; lane.RetrySaveCommand = owner.RetryControlPointsSaveCommand;
        lane.SetPointDisabledCommand = owner.SetControlPointDisabledCommand; lane.SplitPointCommand = owner.SplitControlPointCommand;
        lane.SetBinding(OHsControlPointLane.StateProperty, new Binding(nameof(owner.ControlPointsState)) { Source = owner, Mode = BindingMode.OneWay });
        lane.SetBinding(OHsControlPointLane.PendingAddProperty, new Binding(nameof(owner.PendingControlPointAdd)) { Source = owner, Mode = BindingMode.OneWay });
        UiTestPump.Layout(lane, 4000, 200);
        return lane;
    }

    [Test]
    public void ValidDropCommitsOnceAndPreviewNeverMovesAcceptedMarkers()
    {
        var project = new ProjectData { Offsets = [0, 1000], BPMs = [120, 240], ControlPoints = [new() { Timestamp = 500, Volume = 4, Sampleset = "Drum" }] };
        var audio = new StubWorkstationAudioService(); var files = new DelayedProjectFileService(); var owner = WorkstationControlPointStateTests.Create(project, audio, files);
        try
        {
            UiTestPump.Run(WorkstationControlPointStateTests.Ready(owner)); var lane = Connect(owner); var id = owner.ControlPointsState!.Points[0].Id;
            var timeline = audio.ControlPoints; var markers = owner.ControlPointTimestamps;
            lane.BeginPointDrag(id, 510); lane.UpdatePointDrag(1208);
            Assert.That(lane.PreviewTimestamp, Is.EqualTo(1188));
            Assert.That(lane.IsDragCandidateValid, Is.True); Assert.That(owner.ControlPointTimestamps, Is.SameAs(markers));
            Assert.That(audio.ControlPoints, Is.SameAs(timeline)); Assert.That(files.Writes, Is.Zero);
            Assert.That(lane.CompletePointDrag(), Is.True);
            Assert.That((project.ControlPoints[0].Timestamp, project.ControlPoints[0].Volume, project.ControlPoints[0].Sampleset), Is.EqualTo((1188, 4, "Drum")));
            Assert.That(owner.ControlPointTimestamps, Is.EqualTo(new[] { 1188 })); Assert.That(files.Writes, Is.EqualTo(1));
        }
        finally { files.Release(); UiTestPump.Run(owner.DeactivateAsync()); }
    }

    [Test]
    public void DuplicateTimestampRejectsAndDistinctCloseBoundaryCommits()
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120], ControlPoints = [new() { Timestamp = 500 }, new() { Timestamp = 2000 }] };
        var audio = new StubWorkstationAudioService(); var files = new DelayedProjectFileService(); var owner = WorkstationControlPointStateTests.Create(project, audio, files);
        try
        {
            UiTestPump.Run(WorkstationControlPointStateTests.Ready(owner)); var lane = Connect(owner); lane.Offsets = [0]; lane.Bpms = [120];
            var id = owner.ControlPointsState!.Points[0].Id; var timeline = audio.ControlPoints;
            lane.BeginPointDrag(id, 500); lane.UpdatePointDrag(2000);
            Assert.That(lane.IsDragCandidateValid, Is.False); Assert.That(lane.RejectionReason, Is.Not.Empty);
            Assert.That(lane.BringPointIntoView(id)!.IsInvalid, Is.True);
            Assert.That(lane.CompletePointDrag(), Is.False); Assert.That(project.ControlPoints[0].Timestamp, Is.EqualTo(500));
            lane.BeginPointDrag(id, 500); lane.UpdatePointDrag(1900); Assert.That(lane.IsDragCandidateValid, Is.True);
            Assert.That(lane.CompletePointDrag(), Is.True);
            Assert.That(project.ControlPoints[0].Timestamp, Is.EqualTo(1875));
            Assert.That(audio.ControlPoints, Is.Not.SameAs(timeline)); Assert.That(files.Writes, Is.EqualTo(1));
        }
        finally { files.Release(); UiTestPump.Run(owner.DeactivateAsync()); }
    }

    [TestCase("cancel")][TestCase("context")][TestCase("capture")][TestCase("delete")]
    public void CancelledGestureCannotLeakMutationOrOpenEditor(string reason)
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120], ControlPoints = [new() { Timestamp = 500 }] };
        var owner = WorkstationControlPointStateTests.Create(project, new());
        try
        {
            UiTestPump.Run(WorkstationControlPointStateTests.Ready(owner)); var lane = Connect(owner); var id = owner.ControlPointsState!.Points[0].Id;
            if (reason == "capture") lane.CapturePointer = () => false;
            lane.BeginPointDrag(id, 500); lane.UpdatePointDrag(1500);
            if (reason == "context") UiTestPump.Run(owner.SettleControlPointsAsync());
            else if (reason == "delete") owner.DeleteControlPointCommand.Execute(new DeleteControlPointRequestedMessage(owner.ControlPointsState!.Context, id));
            else lane.CancelPointDrag();
            Assert.That(lane.CompletePointDrag(), Is.False); Assert.That(lane.IsEditorOpen, Is.False);
            Assert.That(project.ControlPoints.Select(p => p.Timestamp), Is.EqualTo(reason == "delete" ? Array.Empty<int>() : new[] { 500 }));
        }
        finally { UiTestPump.Run(owner.DeactivateAsync()); }
    }

    [Test]
    public void BothAddEntryPointsAcceptCloseBoundariesButRejectDuplicateTimestamp()
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120] }; var files = new DelayedProjectFileService();
        var owner = WorkstationControlPointStateTests.Create(project, new(), files);
        try
        {
            UiTestPump.Run(WorkstationControlPointStateTests.Ready(owner)); var lane = Connect(owner); owner.Position = TimeSpan.FromMilliseconds(1000);
            UiTestPump.Run(owner.AddControlPointCommand.ExecuteAsync(null));
            Assert.That(project.ControlPoints.Single().Timestamp, Is.EqualTo(1000)); Assert.That(lane.IsEditorOpen, Is.True); lane.DismissEditor();
            owner.Position = TimeSpan.FromMilliseconds(2000); lane.RequestAddAtMarker();
            Assert.That(project.ControlPoints.Select(p => p.Timestamp), Is.EqualTo(new[] { 1000, 2000 })); Assert.That(lane.IsEditorOpen, Is.True); lane.DismissEditor();
            var before = owner.ControlPointsState!.Revision;
            owner.Position = TimeSpan.FromMilliseconds(2125); lane.RequestAddAtMarker();
            Assert.That(project.ControlPoints, Has.Count.EqualTo(3)); Assert.That(lane.IsEditorOpen, Is.True); Assert.That(lane.RejectionReason, Is.Null);
            Assert.That(owner.ControlPointsState!.Revision, Is.EqualTo(before + 1)); lane.DismissEditor();
            owner.Position = TimeSpan.FromMilliseconds(2000); lane.RequestAddAtMarker();
            Assert.That(lane.IsEditorOpen, Is.False); Assert.That(project.ControlPoints, Has.Count.EqualTo(3));
            var tag = lane.BringPointIntoView(owner.ControlPointsState.Points[0].Id)!;
            lane.DeletePoint(tag.Point!.Id); Assert.That(project.ControlPoints.Select(p => p.Timestamp), Is.EqualTo(new[] { 2000, 2125 }));
            UiControlContract.AssertStyle<ContextMenu>(lane.ZoneMenu, "OHsActionContextMenuStyle");
            Assert.That(((MenuItem)lane.ZoneMenu.Items[1]).Header, Is.EqualTo("Add"));
            UiControlContract.AssertStyle<MenuItem>((MenuItem)lane.ZoneMenu.Items[2], "OHsDestructiveActionMenuItemStyle");
            Assert.That(lane.ZoneMenu.IsOpen, Is.False);
        }
        finally { files.Release(); UiTestPump.Run(owner.DeactivateAsync()); }
    }

    [Test]
    public void KeyboardMenuDeletesCapturedIdentityEvenIfTagIsRecycled()
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120], ControlPoints = [new() { Timestamp = 500 }, new() { Timestamp = 2000 }] };
        var owner = WorkstationControlPointStateTests.Create(project, new());
        try
        {
            UiTestPump.Run(WorkstationControlPointStateTests.Ready(owner)); var lane = Connect(owner);
            var first = owner.ControlPointsState!.Points[0]; var second = owner.ControlPointsState.Points[1];
            var tag = lane.BringPointIntoView(first.Id)!;
            lane.PresentMenu = (_, _) => { };
            Assert.That(lane.HandlePointKey(System.Windows.Input.Key.Apps, System.Windows.Input.ModifierKeys.None, tag), Is.True);
            tag.Point = second;
            ((MenuItem)lane.ZoneMenu.Items[2]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.That(project.ControlPoints.Single().Timestamp, Is.EqualTo(2000));
            Assert.That(lane.HandlePointKey(System.Windows.Input.Key.Left, System.Windows.Input.ModifierKeys.Alt, tag), Is.True);
            Assert.That(project.ControlPoints.Single().Timestamp, Is.EqualTo(1938)); // 240 BPM after 1000ms: prior quarter beat is 1937.5ms.
        }
        finally { UiTestPump.Run(owner.DeactivateAsync()); }
    }

    [Test]
    public void PointerPreviewDoesNotScrollBackToAcceptedTimestamp()
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120], ControlPoints = [new() { Timestamp = 500 }] };
        var owner = WorkstationControlPointStateTests.Create(project, new());
        try
        {
            UiTestPump.Run(WorkstationControlPointStateTests.Ready(owner)); var lane = Connect(owner);
            var id = owner.ControlPointsState!.Points[0].Id; var scrollRequests = 0;
            lane.BeginPointDrag(id, 500); lane.HorizontalOffsetRequested += _ => scrollRequests++;
            lane.UpdatePointDrag(1500);
            Assert.That(lane.IsDragPreviewActive, Is.True); Assert.That(scrollRequests, Is.Zero);
            lane.CancelPointDrag(); Assert.That(project.ControlPoints.Single().Timestamp, Is.EqualTo(500));
        }
        finally { UiTestPump.Run(owner.DeactivateAsync()); }
    }

    [Test]
    public void KeyboardInvocationStillOpensEditorAfterPointerDrop()
    {
        var owner = WorkstationControlPointStateTests.Create(new() { Offsets = [0], BPMs = [120], ControlPoints = [new() { Timestamp = 500 }] }, new());
        try
        {
            UiTestPump.Run(WorkstationControlPointStateTests.Ready(owner)); var lane = Connect(owner); var id = owner.ControlPointsState!.Points[0].Id;
            lane.BeginPointDrag(id, 500); lane.UpdatePointDrag(1500); Assert.That(lane.CompletePointDrag(), Is.True);
            var tag = lane.BringPointIntoView(id)!;
            lane.HandlePointKey(System.Windows.Input.Key.Enter, System.Windows.Input.ModifierKeys.None, tag);
            tag.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.That(lane.IsEditorOpen, Is.True); lane.DismissEditor();
        }
        finally { UiTestPump.Run(owner.DeactivateAsync()); }
    }
}
