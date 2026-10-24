using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Hitbench.Core.Entities;
using Hitbench.Resources.CustomControls;
using Hitbench.Resources.Helpers;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class WorkstationTrackDragScrollingTests
{
    [Test]
    public void WheelDragSurvivesSourceRecyclingAndDropsAtDistantTrack()
    {
        using var context = new WorkstationTrackDragTestContext(120);
        var source = context.Vm.Tracks[0]; context.Start();
        var scroll = WorkstationTrackDragTestContext.Descendants<ScrollViewer>(context.List).First();
        for (var i = 0; i < 250 && scroll.VerticalOffset < scroll.ScrollableHeight; i++)
        {
            Assert.That(context.Owner.HandleNodeDragWheel(ModifierKeys.None, -120), Is.True);
            context.View.UpdateLayout();
        }
        Assert.That(context.List.ItemContainerGenerator.ContainerFromIndex(0), Is.Null);
        Assert.That(context.Owner.DraggedTrack, Is.SameAs(source));
        Assert.That(WorkstationTrackDragTestContext.Descendants<ListBoxItem>(context.List).Count(), Is.LessThan(120));
        var adorner = AdornerLayer.GetAdornerLayer(context.List)!.GetAdorners(context.List)!.OfType<OHsTrackDragAdorner>().Single();
        Assert.That(adorner.PreviewSource, Is.SameAs(context.Preview));
        var last = context.Row(119);
        var drop = last.TranslatePoint(new Point(2, last.ActualHeight - 1), context.Owner);
        Assert.That(context.Owner.CompleteTrackPointer(drop), Is.True);
        Assert.That(context.Vm.Tracks[^1], Is.SameAs(source));
        Assert.That(context.Vm.Project!.Tracks, Is.EqualTo(context.Vm.Tracks));
        foreach (var item in WorkstationTrackDragTestContext.Descendants<ListBoxItem>(context.List))
        {
            Assert.That(OHsTrackDragPresentation.GetIsSource(item), Is.False);
            Assert.That(OHsTrackDragPresentation.GetGapBefore(item) + OHsTrackDragPresentation.GetGapAfter(item), Is.Zero);
        }
    }

    [Test]
    public void PartialWheelDeltasAccumulateAndReset()
    {
        using var context = new WorkstationTrackDragTestContext(120);
        var scroll = WorkstationTrackDragTestContext.Descendants<ScrollViewer>(context.List).First();
        context.Start();
        var offset = scroll.VerticalOffset;
        Assert.That(context.Owner.HandleTrackDragWheel(ModifierKeys.None, -60), Is.True);
        context.View.UpdateLayout(); Assert.That(scroll.VerticalOffset, Is.EqualTo(offset));
        context.Owner.HandleTrackDragWheel(ModifierKeys.None, -60); context.View.UpdateLayout();
        Assert.That(scroll.VerticalOffset, Is.GreaterThan(offset));
        context.Owner.HandleTrackDragWheel(ModifierKeys.None, -60); context.Owner.CancelTrackPointer();
        scroll.ScrollToTop(); context.View.UpdateLayout(); context.Start();
        offset = scroll.VerticalOffset;
        context.Owner.HandleTrackDragWheel(ModifierKeys.None, -60); context.View.UpdateLayout();
        Assert.That(scroll.VerticalOffset, Is.EqualTo(offset));
        Assert.That(context.Owner.HandleTrackDragWheel(ModifierKeys.Alt, -120), Is.False);
    }

    [Test]
    public void CurrentViewportReplacesStaleSlotGeometry()
    {
        using var context = new WorkstationTrackDragTestContext(120);
        var pointer = context.Start();
        var scroll = WorkstationTrackDragTestContext.Descendants<ScrollViewer>(context.List).First();
        scroll.ScrollToVerticalOffset(scroll.ExtentHeight / 2); context.View.UpdateLayout();
        context.Owner.RefreshViewport();
        Assert.That(context.Owner.TrackInsertionSlot, Is.GreaterThan(0));
        Assert.That(context.Owner.CompleteTrackPointer(pointer), Is.True);
        Assert.That(context.Vm.Tracks[0].Name, Is.Not.EqualTo("Track 0"));
    }

    [TestCase("Dark", FlowDirection.LeftToRight)]
    [TestCase("Light", FlowDirection.RightToLeft)]
    public void MoveMenuCommandsRemainKeyboardEquivalent(string theme, FlowDirection direction)
    {
        using var context = new WorkstationTrackDragTestContext(theme: theme, direction: direction);
        var row = context.Row(0); var track = row.Track!;
        var trigger = WorkstationTrackDragTestContext.Descendants<BalloonComboBox>(row).Single();
        var menu = trigger.ContextMenu!;
        var up = menu.Items.OfType<MenuItem>().Single(item => (string)item.Header == "Move Up");
        var down = menu.Items.OfType<MenuItem>().Single(item => (string)item.Header == "Move Down");
        Assert.That(up.CommandParameter, Is.SameAs(track));
        Assert.That(up.Command!.CanExecute(track), Is.False);
        Assert.That(down.Command!.CanExecute(track), Is.True);
        down.Command.Execute(track);
        Assert.That(context.Vm.Tracks[1], Is.SameAs(track));
        up.Command.Execute(track);
        Assert.That(context.Vm.Tracks[0], Is.SameAs(track));
        Assert.That(menu.IsOpen, Is.False);
    }
}
