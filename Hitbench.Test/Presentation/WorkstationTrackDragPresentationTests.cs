using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Hitbench.Resources.CustomControls;
using Hitbench.Resources.Helpers;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class WorkstationTrackDragPresentationTests
{
    [Test]
    public void CaptureUsesDetachedRowWithoutRearrangingLiveSource()
    {
        using var context = new WorkstationTrackDragTestContext();
        var live = context.Row(1);
        var extent = context.Vm.GetTrackExtent(live.Track!);
        context.Owner.CaptureTrackPreview = preview =>
        {
            Assert.That(preview, Is.Not.SameAs(live));
            Assert.That(System.Windows.Media.VisualTreeHelper.GetParent(preview), Is.Null);
            Assert.That(preview.Track, Is.SameAs(live.Track));
            Assert.That(preview.IsArrangeValid, Is.True);
            Assert.That(context.Vm.GetTrackExtent(live.Track!), Is.EqualTo(extent));
            return context.Preview;
        };
        context.Start(1);
        Assert.That(context.Owner.IsTrackDragActive, Is.True);
    }

    [Test]
    public void SourceSpaceClosesWithExactlyOneExplicitInsertionGap()
    {
        using var context = new WorkstationTrackDragTestContext();
        var source = context.Vm.Tracks[0];
        context.Vm.SetTrackMinimumExtent(source, true, 173);
        context.Row(0).RefreshPresentation(); context.View.UpdateLayout();
        var neighbor = context.Row(1);
        var original = neighbor.TranslatePoint(new Point(), context.Owner).Y;
        context.Start();
        context.Owner.UpdateTrackPointer(context.PointOn(context.Row(3)), true);
        context.View.UpdateLayout();
        Assert.That(neighbor.TranslatePoint(new Point(), context.Owner).Y, Is.EqualTo(original - 173).Within(0.01));
        var gap = WorkstationTrackDragTestContext.Descendants<ListBoxItem>(context.List)
            .Sum(item => OHsTrackDragPresentation.GetGapBefore(item) + OHsTrackDragPresentation.GetGapAfter(item));
        Assert.That(gap, Is.EqualTo(173));
        Assert.That(context.Vm.Tracks[0], Is.SameAs(source));
        Assert.That(context.Saves.Writes, Is.Zero);
    }

    [Test]
    public void RecycledSourceCanRefreshPreviewWithoutRestoringOriginalRow()
    {
        using var context = new WorkstationTrackDragTestContext(120);
        var source = context.Vm.Tracks[0]; context.Start();
        WorkstationTrackDragTestContext.Descendants<ScrollViewer>(context.List).First().ScrollToBottom();
        context.View.UpdateLayout();
        source.Name = "Updated while outside viewport";
        context.Owner.TimelineHorizontalOffset = 25;
        context.Owner.RefreshViewport(); context.View.UpdateLayout();
        Assert.That(context.Owner.IsTrackDragActive, Is.True);
        var adorner = AdornerLayer.GetAdornerLayer(context.List)!.GetAdorners(context.List)!.OfType<OHsTrackDragAdorner>().Single();
        Assert.That(adorner.TrackName, Is.EqualTo(source.Name));
        Assert.That(context.List.ItemContainerGenerator.ContainerFromIndex(0), Is.Null);
    }

    [Test]
    public void ArmedGestureRetainsSource()
    {
        using var context = new WorkstationTrackDragTestContext();
        var row = context.Row(0);
        context.Owner.BeginTrackPointer(row, context.PointOn(row));
        Assert.That(row.IsVisible, Is.True);
    }

    [TestCase(0)]
    [TestCase(1)]
    public void ActiveDragCollapsesSourceAndRestoresOnCancel(int index)
    {
        using var context = new WorkstationTrackDragTestContext();
        var row = context.Row(index);
        context.Start(index);
        Assert.That(row.IsVisible, Is.False);
        context.Owner.CancelTrackPointer(); context.View.UpdateLayout();
        Assert.That(row.IsVisible, Is.True);
    }

    [Test]
    public void SourcePresentationRefreshWaitsForValidLayout()
    {
        using var context = new WorkstationTrackDragTestContext();
        var track = context.Vm.Tracks[0];
        context.Vm.SetTrackCollapsed(track, false); context.View.UpdateLayout();
        var validCaptures = new List<bool>();
        context.Owner.CaptureTrackPreview = row => { validCaptures.Add(row.IsArrangeValid); return context.Preview; };
        context.Start();
        context.Vm.SetTrackCollapsed(track, true); context.View.UpdateLayout();
        Assert.That(context.Owner.IsTrackDragActive, Is.True);
        Assert.That(validCaptures, Has.Count.GreaterThan(1));
        Assert.That(validCaptures, Is.All.True, "Image acquisition must follow row presentation layout.");
    }

    [TestCase("Dark", FlowDirection.LeftToRight)]
    [TestCase("Light", FlowDirection.RightToLeft)]
    public void PreviewReusesOwnerResourcesAndActualSourceIdentity(string theme, FlowDirection direction)
    {
        using var context = new WorkstationTrackDragTestContext(theme: theme, direction: direction);
        context.Vm.Tracks[0].Name = new string('N', 180);
        context.Start();
        var adorner = AdornerLayer.GetAdornerLayer(context.List)!.GetAdorners(context.List)!.OfType<OHsTrackDragAdorner>().Single();
        Assert.That(adorner.PreviewSource, Is.SameAs(context.Preview));
        Assert.That(adorner.TrackName, Is.EqualTo(context.Vm.Tracks[0].Name));
        Assert.That(adorner.CursorTag, Is.TypeOf<ContentPresenter>());
        Assert.That(adorner.CursorTag.ContentTemplate, Is.SameAs(context.Owner.FindResource("OHsWorkstationTrackDragTagTemplate")));
        Assert.That(adorner.Phantom.ContentTemplate, Is.SameAs(context.Owner.FindResource("OHsWorkstationTrackDragPhantomTemplate")));
        Assert.That(adorner.IsHitTestVisible || adorner.Focusable, Is.False);
        Assert.That(adorner.FlowDirection, Is.EqualTo(direction));
        Assert.That(OHsTrackDragPresentation.GetIsSource((ListBoxItem)context.List.ItemContainerGenerator.ContainerFromIndex(0)), Is.True);
    }

    [Test]
    public void PreviewDoesNotAddTracksOrChangeSaveState()
    {
        using var context = new WorkstationTrackDragTestContext();
        var tracks = context.Vm.Tracks.ToArray();
        context.Start();
        var destination = context.PointOn(context.Row(2));
        context.Owner.UpdateTrackPointer(destination, true); context.View.UpdateLayout();
        Assert.That(context.Vm.Tracks, Is.EqualTo(tracks));
        Assert.That(context.Vm.Project!.Tracks, Is.EqualTo(tracks));
        Assert.That(context.Saves.Writes, Is.Zero);
        context.Owner.CancelTrackPointer();
        Assert.That(AdornerLayer.GetAdornerLayer(context.List)!.GetAdorners(context.List), Is.Null);
        foreach (var item in WorkstationTrackDragTestContext.Descendants<ListBoxItem>(context.List))
        {
            Assert.That(OHsTrackDragPresentation.GetIsSource(item), Is.False);
            Assert.That(OHsTrackDragPresentation.GetGapBefore(item) + OHsTrackDragPresentation.GetGapAfter(item), Is.Zero);
        }
    }

    [TestCase(true, 0)]
    [TestCase(false, 1)]
    public void PreviewUsesIndividualExplicitExtent(bool collapsed, int source)
    {
        using var context = new WorkstationTrackDragTestContext();
        var track = context.Vm.Tracks[source];
        context.Vm.SetTrackCollapsed(track, collapsed);
        context.View.UpdateLayout();
        context.Vm.SetTrackMinimumExtent(track, collapsed, 173);
        context.Row(source).RefreshPresentation();
        context.Start(source);
        var adorner = AdornerLayer.GetAdornerLayer(context.List)!.GetAdorners(context.List)!.OfType<OHsTrackDragAdorner>().Single();
        Assert.That(adorner.PreviewExtent, Is.EqualTo(173));
    }

    [Test]
    public void RecyclingClearsContainerFeedback()
    {
        using var context = new WorkstationTrackDragTestContext(120);
        var source = (ListBoxItem)context.List.ItemContainerGenerator.ContainerFromIndex(0);
        context.Start();
        WorkstationTrackDragTestContext.Descendants<ScrollViewer>(context.List).First().ScrollToBottom();
        context.View.UpdateLayout();
        Assert.That(context.Owner.DraggedTrack, Is.SameAs(context.Vm.Tracks[0]));
        if (!ReferenceEquals(source.DataContext, context.Vm.Tracks[0])) Assert.That(OHsTrackDragPresentation.GetIsSource(source), Is.False);
        context.Owner.CancelTrackPointer();
        Assert.That(AdornerLayer.GetAdornerLayer(context.List)!.GetAdorners(context.List), Is.Null);
    }
}
