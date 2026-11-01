using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.Messaging;
using Hitbench.ViewModels.Popups;
using Hitbench.Views.Popups;
using Hitbench.Resources.DataModels;
using Hitbench.Resources.CustomControls;
namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class ManageSamplesDragTests
{
    [Test]
    public void DragThresholdAndManagerReadinessGuardNativeInvocation()
    {
        using var c = new ManageSamplesDragTestContext(); var calls=0;
        c.Drawer.SetSampleDragHandler((_,data,_)=>{ calls++;Assert.That(data.GetDataPresent(DataFormats.FileDrop),Is.False);return DragDropEffects.None;});
        Assert.That(c.Drawer.TryStartSampleDrag(c.Entry,new Vector()),Is.False);
        Assert.That(c.Drawer.TryStartSampleDrag(c.Entry,new Vector(100,0)),Is.True);
        Assert.That(calls,Is.EqualTo(1));
        UiTestPump.Run(c.Vm.EditCommand.ExecuteAsync(c.Entry));
        Assert.That(c.Drawer.TryStartSampleDrag(c.Entry,new Vector(100,100)),Is.False);
    }
    [TestCase(DragDropEffects.Copy)]
    [TestCase(DragDropEffects.None)]
    public void NativeLifetimeOwnsOneTagAndRestoresSource(DragDropEffects result)
    {
        using var c=new ManageSamplesDragTestContext();var row=c.RowFor(c.Entry);
        c.Drawer.SetSampleDragHandler((_,data,effects)=>{
            Assert.That(row.IsDragSource,Is.True);
            Assert.That(c.RowFor(c.Vm.Entries[1]).IsDragSource,Is.False);
            Assert.That(c.Host.AdornerLayer.GetAdorners(c.Root)!.OfType<OHsSampleDragAdorner>().Single().SampleName,Is.EqualTo("a.wav"));
            Assert.That(c.Drawer.TryStartSampleDrag(c.Entry,new Vector(100,100)),Is.False);
            row.DataContext=new SampleManagementEntry("a.wav");Assert.That(row.IsDragSource,Is.False);
            row.DataContext=c.Entry;Assert.That(row.IsDragSource,Is.True);return result;
        });
        for(var i=0;i<2;i++)Assert.That(c.Drawer.TryStartSampleDrag(c.Entry,new Vector(100,100)),Is.True);
        Assert.That(row.IsDragSource,Is.False);
        Assert.That(row.ReadLocalValue(OHsSampleRow.IsDragSourceProperty),Is.SameAs(DependencyProperty.UnsetValue));
        Assert.That(c.Host.AdornerLayer.GetAdorners(c.Root)??[],Is.Empty);
    }
    [Test]
    public void ExceptionAndInvalidationClearFeedbackAndCancelNativeLoop()
    {
        using var c=new ManageSamplesDragTestContext();var row=c.RowFor(c.Entry);
        c.Drawer.SetSampleDragHandler((_,_,_)=>throw new InvalidOperationException("native failed"));
        Assert.Throws<InvalidOperationException>(()=>c.Drawer.TryStartSampleDrag(c.Entry,new Vector(100,100)));
        Assert.That(row.IsDragSource,Is.False);
        c.Drawer.SetSampleDragHandler((_,_,_)=>{
            c.Vm.Entries.Remove(c.Entry);Assert.That(row.IsDragSource,Is.False);
            Assert.That(c.Drawer.GetSampleDragContinuation(DragAction.Continue),Is.EqualTo(DragAction.Cancel));
            Assert.That(c.Drawer.TryStartSampleDrag(c.Entry,new Vector(100,100)),Is.False);return DragDropEffects.None;
        });
        c.Drawer.TryStartSampleDrag(c.Entry,new Vector(100,100));
        Assert.That(c.Host.AdornerLayer.GetAdorners(c.Root)??[],Is.Empty);
    }
    [Test]
    public void PointerUpdatesUseStableOwnerAndStopAfterUnload()
    {
        using var c=new ManageSamplesDragTestContext();var point=new Point(100,100);var calls=0;
        c.Drawer.SetSampleDragPointerProvider(owner=>{Assert.That(owner,Is.SameAs(c.Root));calls++;return point;});
        c.Drawer.SetSampleDragHandler((_,_,_)=>{
            var tag=c.Host.AdornerLayer.GetAdorners(c.Root)!.OfType<OHsSampleDragAdorner>().Single();
            point=new Point(300,200);Assert.That(c.Drawer.GetSampleDragContinuation(DragAction.Continue),Is.EqualTo(DragAction.Continue));
            Assert.That(tag.CursorPosition,Is.EqualTo(point));
            point=new Point(-100,200);c.Drawer.GetSampleDragContinuation(DragAction.Continue);
            Assert.That(c.RowFor(c.Entry).IsDragSource,Is.True);
            c.Drawer.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));var previous=calls;
            Assert.That(c.Drawer.GetSampleDragContinuation(DragAction.Continue),Is.EqualTo(DragAction.Cancel));
            Assert.That(calls,Is.EqualTo(previous));return DragDropEffects.None;
        });
        c.Drawer.TryStartSampleDrag(c.Entry,new Vector(100,100));
        Assert.That(c.Host.AdornerLayer.GetAdorners(c.Root)??[],Is.Empty);
    }
    [Test]
    public void OriginsAndPresentationContractsRemainIndependent()
    {
        using var c=new ManageSamplesDragTestContext();var row=c.RowFor(c.Entry);
        UiControlContract.AssertStyle<OHsSampleRow>(row,"OHsManageSamplesDragSourceStyle");
        Assert.That(c.Drawer.BeginSampleDragPointer((DependencyObject)row.FindName("PlayAction"),new Point()),Is.False);
        Assert.That(c.Drawer.BeginSampleDragPointer(new TextBox(),new Point()),Is.False);
        Assert.That(c.Drawer.BeginSampleDragPointer(new Slider(),new Point()),Is.False);
        Assert.That(c.Drawer.BeginSampleDragPointer(row,new Point()),Is.True);
        c.Entry.FileName="\u97f3 \u0627"+new string('x',300)+".wav";
        c.Drawer.FlowDirection=FlowDirection.RightToLeft;
        c.Drawer.SetSampleDragHandler((_,_,_)=>{
            var tag=c.Host.AdornerLayer.GetAdorners(c.Root)!.OfType<OHsSampleDragAdorner>().Single();
            Assert.That(tag.SampleName,Is.EqualTo(c.Entry.FileName));Assert.That(tag.FlowDirection,Is.EqualTo(FlowDirection.RightToLeft));
            Assert.That(tag.CursorTag.ContentTemplate,Is.SameAs(c.Drawer.FindResource("OHsManageSamplesDragTagTemplate")));
            UiControlContract.AssertStyle<ContentPresenter>(tag.CursorTag,"BaseContentPresenterStyle");
            Assert.That(tag.IsHitTestVisible,Is.False);Assert.That(tag.Focusable,Is.False);return DragDropEffects.None;
        });
        Assert.That(c.Drawer.TryStartSampleDrag(c.Entry,new Vector(0,100)),Is.True);
    }
}
