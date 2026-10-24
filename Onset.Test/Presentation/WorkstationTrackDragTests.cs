using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Collections.Specialized;
using Onset.Resources.CustomControls;

namespace Onset.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class WorkstationTrackDragTests
{
    [Test]
    public void DrawModeCannotArmTrackDrag()
    {
        using var context = new WorkstationTrackDragTestContext();
        context.Vm.IsAdditionMode = true;
        Assert.That(context.Owner.BeginTrackPointer(context.Row(0), context.PointOn(context.Row(0))), Is.False);
        Assert.That(context.Captures, Is.Zero);
    }

    [Test]
    public void RepeatedDropGesturesLeaveNoOrphanAdorners()
    {
        using var context = new WorkstationTrackDragTestContext();
        for (var i = 0; i < 3; i++)
        {
            context.Start();
            context.Owner.CompleteTrackPointer(new Point(-1, -1));
            context.View.UpdateLayout();
            Assert.That(context.Owner.DraggedTrack, Is.Null);
            Assert.That(AdornerLayer.GetAdornerLayer(context.List)!.GetAdorners(context.List), Is.Null);
        }
        Assert.That(context.Saves.Writes, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SwitchingToDrawCancelsArmedOrActiveGesture(bool active)
    {
        using var context = new WorkstationTrackDragTestContext();
        if (active) context.Start();
        else context.Owner.BeginTrackPointer(context.Row(0), context.PointOn(context.Row(0)));
        context.Vm.IsAdditionMode = true;
        Assert.That(context.Owner.DraggedTrack, Is.Null);
        context.Vm.IsAdditionMode = false;
        context.View.UpdateLayout();
        Assert.That(context.Owner.IsTrackDragActive, Is.False);
        Assert.That(AdornerLayer.GetAdornerLayer(context.List)!.GetAdorners(context.List), Is.Null);
        Assert.That(context.Releases, Is.EqualTo(active ? 1 : 0));
        Assert.That(context.Saves.Writes, Is.Zero);
    }

    [Test]
    public void ReentrantCaptureLeavesNoOrphanAdorners()
    {
        using var context = new WorkstationTrackDragTestContext();
        var point = context.PointOn(context.Row(0));
        var moved = new Point(point.X + SystemParameters.MinimumHorizontalDragDistance + 1, point.Y);
        var calls = 0;
        context.Owner.CaptureTrackPointer = () =>
        {
            if (++calls == 1) context.Owner.UpdateTrackPointer(moved, true);
            return true;
        };
        context.Start();
        context.Owner.CancelTrackPointer();
        context.View.UpdateLayout();
        Assert.That(AdornerLayer.GetAdornerLayer(context.List)!.GetAdorners(context.List), Is.Null);
    }

    [Test]
    public void CancellationDuringPreviewAcquisitionLeavesNoDragPresentation()
    {
        using var context = new WorkstationTrackDragTestContext();
        context.Owner.CaptureTrackPreview = _ => { context.Owner.CancelTrackPointer(); return context.Preview; };
        context.Start();
        context.View.UpdateLayout();
        Assert.That(context.Owner.DraggedTrack, Is.Null);
        Assert.That(AdornerLayer.GetAdornerLayer(context.List)!.GetAdorners(context.List), Is.Null);
        foreach (var item in WorkstationTrackDragTestContext.Descendants<ListBoxItem>(context.List))
            Assert.That(Onset.Resources.Helpers.OHsTrackDragPresentation.GetIsSource(item), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RoutedReleaseRemovesAllDragPresentationEvenWhenAlreadyHandled(bool handled)
    {
        using var context = new WorkstationTrackDragTestContext();
        context.Start();
        context.Row(0).RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        { RoutedEvent = Mouse.PreviewMouseUpEvent, Handled = handled });
        context.View.UpdateLayout();
        Assert.That(context.Owner.IsTrackDragActive, Is.False);
        Assert.That(context.Owner.DraggedTrack, Is.Null);
        Assert.That(AdornerLayer.GetAdornerLayer(context.List)!.GetAdorners(context.List), Is.Null);
        foreach (var container in WorkstationTrackDragTestContext.Descendants<ListBoxItem>(context.List))
        {
            Assert.That(Onset.Resources.Helpers.OHsTrackDragPresentation.GetIsSource(container), Is.False);
            Assert.That(Onset.Resources.Helpers.OHsTrackDragPresentation.GetGapBefore(container), Is.Zero);
            Assert.That(Onset.Resources.Helpers.OHsTrackDragPresentation.GetGapAfter(container), Is.Zero);
        }
    }

    [Test]
    public void LeavingBeforeThresholdCancelsPendingGesture()
    {
        using var context = new WorkstationTrackDragTestContext();
        context.Owner.BeginTrackPointer(context.Row(0), context.PointOn(context.Row(0)));
        context.Owner.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
        Assert.That(context.Owner.DraggedTrack, Is.Null);
        Assert.That(context.Captures, Is.Zero);
    }

    [Test]
    public void NoninteractiveContainerChromeUsesItsPublicRowIdentity()
    {
        using var context = new WorkstationTrackDragTestContext();
        var container = (ListBoxItem)context.List.ItemContainerGenerator.ContainerFromIndex(0);
        Assert.That(context.Owner.BeginTrackPointer(container, context.PointOn(context.Row(0))), Is.True);
        Assert.That(context.Owner.DraggedTrack, Is.SameAs(context.Vm.Tracks[0]));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MissingPreviewInfrastructureCancelsAndReleasesCapture(bool missingLayer)
    {
        using var context = new WorkstationTrackDragTestContext();
        var row = context.Row(0); var point = context.PointOn(row);
        Assert.That(context.Owner.BeginTrackPointer(row, point), Is.True);
        if (missingLayer) WorkstationTrackDragTestContext.Descendants<AdornerDecorator>(context.Owner).Single().Child = null;
        else context.Owner.CaptureTrackPreview = _ => null;
        context.Owner.UpdateTrackPointer(new Point(point.X + SystemParameters.MinimumHorizontalDragDistance + 1, point.Y), true);
        Assert.That(context.Owner.IsTrackDragActive, Is.False);
        Assert.That(context.Owner.DraggedTrack, Is.Null);
        Assert.That(context.Releases, Is.EqualTo(1));
        Assert.That(context.Saves.Writes, Is.Zero);
    }

    [TestCase(0, 3, true)]
    [TestCase(3, 0, false)]
    [TestCase(1, 2, true)]
    [TestCase(2, 1, false)]
    [TestCase(1, 1, false)]
    public void TopBottomAdjacentAndNoOpDrops(int source, int destination, bool after)
    {
        using var context = new WorkstationTrackDragTestContext();
        var tracks = context.Vm.Tracks.ToList(); var track = tracks[source];
        var moves = 0;
        context.Vm.Tracks.CollectionChanged += (_, e) => { if (e.Action == NotifyCollectionChangedAction.Move) moves++; };
        context.Vm.SelectedTrack = track;
        context.Start(source);
        var row = context.Row(destination);
        var point = row.TranslatePoint(new Point(2, after ? row.ActualHeight - 1 : 1), context.Owner);
        var slot = destination + (after ? 1 : 0); var final = slot > source ? slot - 1 : slot;
        Assert.That(context.Owner.CompleteTrackPointer(point), Is.EqualTo(final != source));
        tracks.RemoveAt(source); tracks.Insert(final, track);
        Assert.That(context.Vm.Tracks, Is.EqualTo(tracks));
        Assert.That(context.Vm.Project!.Tracks, Is.EqualTo(tracks));
        Assert.That(context.Vm.SelectedTrack, Is.SameAs(track));
        Assert.That(moves, Is.EqualTo(final == source ? 0 : 1));
        UiTestPump.Run(context.Vm.FlushSavesAsync());
        Assert.That(context.Saves.Writes, Is.EqualTo(moves));
        Assert.That(context.Owner.CompleteTrackPointer(point), Is.False);
        Assert.That(context.Owner.IsTrackDragActive, Is.False);
        Assert.That(AdornerLayer.GetAdornerLayer(context.List)!.GetAdorners(context.List), Is.Null);
    }

    [TestCase("escape")]
    [TestCase("capture")]
    [TestCase("owner")]
    [TestCase("unload")]
    [TestCase("context")]
    [TestCase("remove")]
    [TestCase("reset")]
    [TestCase("add")]
    [TestCase("move")]
    [TestCase("outside")]
    [TestCase("nonfinite")]
    [TestCase("gated")]
    public void OutsideDropAndAllCancellationRoutesNeverMove(string route)
    {
        using var context = new WorkstationTrackDragTestContext();
        context.Start();
        var point = context.PointOn(context.Row(3));
        switch (route)
        {
            case "escape": context.Owner.HandleTrackDragKey(Key.Escape); break;
            case "capture": context.Owner.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.LostMouseCaptureEvent }); break;
            case "owner": context.Owner.CancelNodePointer(); break;
            case "unload": context.Owner.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, context.Owner)); break;
            case "context": context.Owner.DataContext = null; break;
            case "remove": context.Vm.Tracks.RemoveAt(0); break;
            case "reset": context.Vm.Tracks.Clear(); break;
            case "add": context.Vm.Tracks.Add(new Onset.Core.Entities.Track()); break;
            case "move": context.Vm.Tracks.Move(2, 3); break;
            case "outside": point = new Point(-1, -1); break;
            case "nonfinite": point = new Point(double.NaN, 0); break;
            case "gated": context.Owner.IsVolumeOverlayOpen = true; break;
        }
        Assert.That(context.Owner.CompleteTrackPointer(point), Is.False);
        Assert.That(context.Owner.DraggedTrack, Is.Null);
        Assert.That(context.Releases, Is.EqualTo(1));
        Assert.That(AdornerLayer.GetAdornerLayer(context.List)!.GetAdorners(context.List), Is.Null);
        UiTestPump.Run(context.Vm.FlushSavesAsync()); Assert.That(context.Saves.Writes, Is.Zero);
    }

    [Test]
    public void ReloadReusingTrackReferenceInvalidatesGesture()
    {
        using var context = new WorkstationTrackDragTestContext();
        var track = context.Vm.Tracks[0]; context.Start();
        UiTestPump.Run(context.Vm.LoadAsync("replacement.json", default));
        context.Vm.AcknowledgePresentationReady(context.Vm.LoadGeneration); context.View.UpdateLayout();
        Assert.That(context.Vm.Tracks[0], Is.SameAs(track));
        Assert.That(context.Owner.CompleteTrackPointer(context.PointOn(context.Row(3))), Is.False);
        Assert.That(context.Owner.DraggedTrack, Is.Null);
    }

    [Test]
    public void ReleaseReentrancyCannotApplyStaleSlot()
    {
        using var context = new WorkstationTrackDragTestContext(); context.Start();
        context.Owner.ReleaseTrackPointer = () => context.Vm.Tracks.Move(2, 3);
        Assert.That(context.Owner.CompleteTrackPointer(context.PointOn(context.Row(3))), Is.False);
        Assert.That(context.Vm.Tracks[0].Name, Is.EqualTo("Track 0"));
        Assert.That(context.Saves.Writes, Is.Zero);
    }

    [Test]
    public void ArmsOnlyLiveNoninteractiveRowAreas()
    {
        using var context = new WorkstationTrackDragTestContext();
        var row = context.Row(0); var point = context.PointOn(row);
        Assert.That(context.Owner.BeginTrackPointer(row, point), Is.True);
        Assert.That(context.Owner.DraggedTrack, Is.SameAs(row.Track));
        context.Owner.CancelTrackPointer();
        foreach (var input in WorkstationTrackDragTestContext.Descendants<DependencyObject>(row).Where(x =>
            x is ButtonBase or TextBoxBase or Selector or Slider or BalloonButton or BeatGridView))
            Assert.That(context.Owner.BeginTrackPointer(input, point), Is.False, input.GetType().Name);
        Assert.That(context.Owner.BeginTrackPointer(null, point), Is.False);
        context.Owner.IsVolumeOverlayOpen = true;
        Assert.That(context.Owner.BeginTrackPointer(row, point), Is.False);
    }

    [Test]
    public void ContentElementAndDisabledInputAncestryAreRespected()
    {
        using var context = new WorkstationTrackDragTestContext();
        var row = context.Row(0); var point = context.PointOn(row);
        var label = new TextBlock();
        WorkstationTrackDragTestContext.Descendants<StackPanel>(row).Single(panel => panel.Name == "TrackLeftControls").Children.Add(label);
        var run = new Run("Label"); label.Inlines.Add(run);
        Assert.That(context.Owner.BeginTrackPointer(run, point), Is.True);
        context.Owner.CancelTrackPointer();
        var input = new TextBox();
        WorkstationTrackDragTestContext.Descendants<StackPanel>(row).Single(panel => panel.Name == "TrackLeftControls").Children.Add(input); context.View.UpdateLayout();
        input.IsEnabled = false;
        Assert.That(context.Owner.BeginTrackPointer(input, point), Is.False);
        var grid = row.SampleTimeline!;
        Assert.That(context.Owner.BeginTrackPointer(row, grid.TranslatePoint(new Point(1, 1), context.Owner)), Is.False);
    }

    [Test]
    public void ThresholdPreservesClickAndSelection()
    {
        using var context = new WorkstationTrackDragTestContext();
        var row = context.Row(0); var point = context.PointOn(row);
        context.Vm.SelectedTrack = row.Track;
        Assert.That(context.Owner.BeginTrackPointer(row, point), Is.True);
        context.Owner.UpdateTrackPointer(point, true);
        Assert.That(context.Owner.IsTrackDragActive, Is.False);
        Assert.That(context.Captures, Is.Zero);
        Assert.That(context.Vm.SelectedTrack, Is.SameAs(row.Track));
        context.Owner.UpdateTrackPointer(new Point(point.X + SystemParameters.MinimumHorizontalDragDistance, point.Y), true);
        Assert.That(context.Owner.IsTrackDragActive, Is.True);
        Assert.That(context.Captures, Is.EqualTo(1));
        Assert.That(context.Saves.Writes, Is.Zero);
    }

    [Test]
    public void CaptureFailureAndButtonReleaseCancel()
    {
        using var context = new WorkstationTrackDragTestContext { CanCapture = false };
        context.Start();
        Assert.That(context.Owner.IsTrackDragActive, Is.False);
        Assert.That(context.Owner.DraggedTrack, Is.Null);
        context.CanCapture = true; context.Start();
        context.Owner.UpdateTrackPointer(new Point(), false);
        Assert.That(context.Owner.DraggedTrack, Is.Null);
        Assert.That(context.Releases, Is.EqualTo(1));
    }

    [Test]
    public void CancellationIsIdempotent()
    {
        using var context = new WorkstationTrackDragTestContext();
        context.Start();
        context.Owner.ReleaseTrackPointer = () => { context.Owner.CancelTrackPointer(); };
        Assert.That(context.Owner.HandleNodeKey(Key.Escape, ModifierKeys.None, false, context.Row(0)), Is.True);
        context.Owner.CancelTrackPointer();
        Assert.That(context.Owner.IsTrackDragActive, Is.False);
        Assert.That(context.Owner.DraggedTrack, Is.Null);
        Assert.That(context.Saves.Writes, Is.Zero);
    }
}
