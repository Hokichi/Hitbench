using System.Windows;
using Hitbench.Core.Entities;
using Hitbench.Resources.Helpers;
using Hitbench.Resources.Messages;
using Hitbench.Services;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class OHsControlPointVolumeDragTests
{
    [TestCase(50, 75)][TestCase(500, 5)][TestCase(-500, 100)]
    public void DragMathUsesExplicitScaleAndBounds(double pointerY, int expected) =>
        Assert.That(OHsControlPointLayout.GetDraggedVolume(50, 100, pointerY, 200), Is.EqualTo(expected));

    [TestCase(0)][TestCase(double.NaN)][TestCase(double.PositiveInfinity)]
    public void InvalidScaleCannotProduceVolume(double height) =>
        Assert.That(OHsControlPointLayout.GetDraggedVolume(50, 100, 50, height), Is.Null);

    [TestCase(false)][TestCase(true)]
    public void PreviewHasNoAudioOrSaveAndReleaseCommitsExactlyOnce(bool disabled)
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120], ControlPoints = [new() { Volume = 50, IsDisabled = disabled }] };
        var audio = new StubWorkstationAudioService(); var files = new DelayedProjectFileService();
        var owner = WorkstationControlPointStateTests.Create(project, audio, files);
        try
        {
            UiTestPump.Run(WorkstationControlPointStateTests.Ready(owner)); var lane = OHsControlPointDragTests.Connect(owner);
            var state = owner.ControlPointsState!; var id = state.Points[0].Id; var originalAudio = audio.ControlPoints;
            lane.BeginVolumeDrag(id, 100, 200); lane.UpdateVolumeDrag(50);
            Assert.That(lane.PreviewVolume, Is.EqualTo(75)); Assert.That(lane.IsVolumePreviewActive, Is.True);
            Assert.That(project.ControlPoints[0].Volume, Is.EqualTo(50)); Assert.That(audio.ControlPoints, Is.SameAs(originalAudio)); Assert.That(files.Writes, Is.Zero);
            Assert.That(lane.CompleteVolumeDrag(), Is.True); Assert.That(lane.CompleteVolumeDrag(), Is.False);
            Assert.That(project.ControlPoints[0].Volume, Is.EqualTo(75)); Assert.That(owner.ControlPointsState!.Revision, Is.EqualTo(state.Revision + 1));
            Assert.That(files.Writes, Is.EqualTo(1)); Assert.That(lane.PreviewVolume, Is.Null);
            Assert.That(new ControlPointVolumeTimeline(audio.ControlPoints).GetGain(0), Is.EqualTo(disabled ? 1f : .75f));
        }
        finally { files.Release(); UiTestPump.Run(owner.DeactivateAsync()); }
    }

    [TestCase("escape")][TestCase("capture")][TestCase("reset")][TestCase("unload")][TestCase("gate")]
    [TestCase("context")][TestCase("delete")][TestCase("revision")][TestCase("scale")][TestCase("cancel")][TestCase("lostcapture")][TestCase("scroll")]
    public void CancelledVolumeGestureNeverSubmits(string reason)
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120], ControlPoints = [new() { Timestamp = 1000, Volume = 50 }, new() { Timestamp = 5000, Volume = 60 }] };
        var files = new DelayedProjectFileService(); var owner = WorkstationControlPointStateTests.Create(project, new(), files);
        try
        {
            UiTestPump.Run(WorkstationControlPointStateTests.Ready(owner)); var lane = OHsControlPointDragTests.Connect(owner);
            var state = owner.ControlPointsState!; var id = state.Points.Single(p => p.Timestamp == 1000).Id;
            if (reason == "capture") lane.CapturePointer = () => false;
            lane.BeginVolumeDrag(id, 100, 200); lane.UpdateVolumeDrag(50);
            switch (reason)
            {
                case "escape": lane.HandlePointKey(System.Windows.Input.Key.Escape, System.Windows.Input.ModifierKeys.None, lane); break;
                case "reset": lane.ResetInteraction(); break;
                case "unload": lane.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); break;
                case "gate": lane.IsEnabled = false; break;
                case "context": UiTestPump.Run(owner.SettleControlPointsAsync()); break;
                case "delete": lane.DeletePoint(id); break;
                case "revision": owner.SetControlPointCommand.Execute(new(state.Context, state.Points.Single(p => p.Timestamp == 5000).Id, 40, null)); break;
                case "scale": lane.ContentWidth *= 2; break;
                case "scroll": lane.HorizontalOffset = 5000; break;
                case "lostcapture": lane.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = System.Windows.Input.Mouse.LostMouseCaptureEvent }); break;
                default: lane.CancelVolumeDrag(); break;
            }
            Assert.That(lane.CompleteVolumeDrag(), Is.False); Assert.That(lane.IsVolumePreviewActive, Is.False);
            Assert.That(lane.PreviewVolume, Is.Null);
            if (reason != "delete") Assert.That(project.ControlPoints[0].Volume, Is.EqualTo(50));
        }
        finally { files.Release(); UiTestPump.Run(owner.DeactivateAsync()); }
    }

    [Test]
    public void SaveStatusOnlySnapshotKeepsPreviewAndNoOpReleaseWritesNothing()
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120], ControlPoints = [new() { Volume = 50 }] };
        var files = new DelayedProjectFileService(); var owner = WorkstationControlPointStateTests.Create(project, new(), files);
        try
        {
            UiTestPump.Run(WorkstationControlPointStateTests.Ready(owner)); var lane = OHsControlPointDragTests.Connect(owner);
            var id = owner.ControlPointsState!.Points[0].Id;
            lane.BeginVolumeDrag(id, 100, 200); lane.UpdateVolumeDrag(50);
            owner.RetrySaveCommand.Execute(null); files.Release(); UiTestPump.Run(owner.FlushSavesAsync());
            Assert.That(lane.PreviewVolume, Is.EqualTo(75));
            var accepted = lane.CompleteVolumeDrag();
            Assert.That(accepted, Is.True, $"owner={owner.ControlPointsState?.Revision}/{owner.ControlPointsState?.Points[0].Volume}, lane={lane.State?.Revision}/{lane.State?.Points[0].Volume}, preview={lane.PreviewVolume}");
            UiTestPump.Run(owner.FlushSavesAsync());
            UiTestPump.Run(System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle).Task);
            Assert.That(files.Writes, Is.EqualTo(2));
            lane.BeginVolumeDrag(id, 100, 200); lane.UpdateVolumeDrag(50); lane.UpdateVolumeDrag(100);
            Assert.That(lane.CompleteVolumeDrag(), Is.False); Assert.That(files.Writes, Is.EqualTo(2));
        }
        finally { files.Release(); UiTestPump.Run(owner.DeactivateAsync()); }
    }

    [TestCase(0)][TestCase(4)]
    public void UntouchedLegacyClickAndCancelKeepStoredVolume(int volume)
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120], ControlPoints = [new() { Volume = volume }] };
        var owner = WorkstationControlPointStateTests.Create(project, new());
        try
        {
            UiTestPump.Run(WorkstationControlPointStateTests.Ready(owner)); var lane = OHsControlPointDragTests.Connect(owner); var id = owner.ControlPointsState!.Points[0].Id;
            lane.BeginVolumeDrag(id, 100, 200); Assert.That(lane.CompleteVolumeDrag(), Is.False);
            lane.BeginVolumeDrag(id, 100, 200); lane.UpdateVolumeDrag(50); lane.CancelVolumeDrag();
            Assert.That(project.ControlPoints[0].Volume, Is.EqualTo(volume));
        }
        finally { UiTestPump.Run(owner.DeactivateAsync()); }
    }
}
