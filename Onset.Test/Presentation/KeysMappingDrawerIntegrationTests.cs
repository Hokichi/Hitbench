using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Messaging;
using Onset.Core.Entities;
using Onset.Resources.DataModels;
using Onset.ViewModels.Popups;
using Onset.Views.Popups;
namespace Onset.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class KeysMappingDrawerIntegrationTests
{
    [Test]
    public void MouseWheelCanReachBothEndsWithoutSelectingAKey()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var ctx = new InstrumentalTrackContext("project.json", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var vm = new KeysMappingDrawerVM(new(ctx, "Piano", 100, new Dictionary<string, string>(), [], [], 0), new WeakReferenceMessenger(), new ImmediateUiScheduler(), new StubSampleFileService([]), new StubSampleFileDialogService((string?)null));
        var drawer = new OHsKeysMappingDrawer(vm);
        var host = new Window { Content = drawer, Width = 600, Height = 500 };
        using var view = new InMemoryView(host);
        try
        {
            drawer.FocusInitialAction(); view.UpdateLayout();
            var selector = (ListBox)drawer.FindName("KeySelector");
            var scroll = Descendants(selector).OfType<ScrollViewer>().First();
            var bar = (System.Windows.Controls.Primitives.ScrollBar)drawer.FindName("PianoScrollBar");
            Assert.That(bar.Style, Is.SameAs(bar.FindResource(typeof(System.Windows.Controls.Primitives.ScrollBar))));
            var offset = scroll.ScrollableWidth / 2;
            bar.RaiseEvent(new System.Windows.Controls.Primitives.ScrollEventArgs(System.Windows.Controls.Primitives.ScrollEventType.ThumbTrack, offset));
            view.UpdateLayout();
            Assert.That(scroll.HorizontalOffset, Is.EqualTo(offset).Within(.01));
            Assert.That(bar.Value, Is.EqualTo(scroll.HorizontalOffset).Within(.01));
            foreach (var delta in new[] { -120, 120 })
            {
                for (var step = 0; step < 108; step++)
                {
                    selector.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, delta)
                    { RoutedEvent = Mouse.PreviewMouseWheelEvent });
                    view.UpdateLayout();
                }
                Assert.That(scroll.HorizontalOffset, Is.EqualTo(delta < 0 ? scroll.ScrollableWidth : 0).Within(0.01));
                Assert.That(bar.Value, Is.EqualTo(scroll.HorizontalOffset).Within(.01));
                var index = delta < 0 ? 107 : 0;
                Assert.That(((FrameworkElement)selector.ItemContainerGenerator.ContainerFromIndex(index)).DataContext,
                    Is.SameAs(vm.Rows[index]));
            }
            Assert.That(vm.SelectedEntry, Is.Null);
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); host.Close(); }
    }

    [Test]
    public void OctaveCenterUsesExplicitViewportAndClamps()
    {
        Assert.That(OHsKeysMappingDrawer.GetInitialKeyboardOffset(480,600,200,1080), Is.EqualTo(440));
        Assert.That(OHsKeysMappingDrawer.GetInitialKeyboardOffset(0,120,200,1080), Is.Zero);
    }
    [Test]
    public void DrawerCloseDrainsCancelledQueuedEditsAndCommitsDrafts()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var track = new Onset.Core.Entities.InstrumentalTrack { Name = "Piano" };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var attempts = 0;
        var files = new StubSampleFileService(["new.wav"]) { Import = async (_, _, token) =>
        { if (++attempts == 1) { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); } return "Piano/new.wav"; } };
        var messenger = new WeakReferenceMessenger(); var dialogs = new StubInstrumentalTrackDialogService();
        var owner = new Onset.ViewModels.Shell.WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", new Onset.Core.Entities.ProjectData { Tracks = [track], Offsets = [0], BPMs = [120] })) },
            new Onset.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), sampleFiles: files, messenger: messenger, instrumentDialogs: dialogs);
        dialogs.Mapping = async (snapshot, _) =>
        {
            var vm = new KeysMappingDrawerVM(snapshot, messenger, new ImmediateUiScheduler(), files, new StubSampleFileDialogService((string?)null));
            var drawer = new OHsKeysMappingDrawer(vm); var host = new Window { Content = drawer, DataContext = vm, Width = 600, Height = 400 };
            using var view = new InMemoryView(host);
            try
            {
                vm.Rows.Single(r => r.Key == "C3").DraftFileName = "new.wav";
                vm.Rows.Single(r => r.Key == "D3").DraftFileName = "new.wav";
                var first = vm.CommitMappingAsync("C3", "new.wav"); await entered.Task;
                var second = vm.CommitMappingAsync("D3", "new.wav");
                Assert.That(drawer.TryPrepareClose(), Is.True);
                var closing = vm.DeactivateAsync(); await first;
                try { await second; } catch (OperationCanceledException) { }
                await closing;
            }
            finally { try { await vm.DeactivateAsync(); } finally { host.Close(); } }
        };
        try
        {
            UiTestPump.Run(owner.LoadAsync("project.json", default)); owner.AcknowledgePresentationReady(owner.LoadGeneration);
            UiTestPump.Run(owner.KeysMappingCommand.ExecuteAsync(track));
            Assert.That(owner.ErrorMessage, Is.Null);
            Assert.That(track.KeyFiles["C3"], Is.EqualTo("Piano/new.wav")); Assert.That(track.KeyFiles["D3"], Is.EqualTo("Piano/new.wav"));
        }
        finally { UiTestPump.Run(owner.DeactivateAsync()); }
    }
    [Test]
    public void OuterOpenBindsFolderCommandAndSharedActionStyle()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var ctx = new InstrumentalTrackContext("project.json", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var vm = new KeysMappingDrawerVM(new(ctx, "Piano", 100, new Dictionary<string, string>(), [], [], 0), new WeakReferenceMessenger(), new ImmediateUiScheduler(), new StubSampleFileService([]), new StubSampleFileDialogService((string?)null), new StubMappingConflictDialogService());
        var drawer = new OHsKeysMappingDrawer(vm);
        var host = new Window { Content = drawer, DataContext = vm, Width = 600, Height = 400 };
        using var view = new InMemoryView(host);
        try
        {
            UiTestPump.Run(drawer.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.DataBind).Task);
            var open = Descendants(drawer).OfType<Onset.Resources.CustomControls.BalloonButton>().Single(button => System.Windows.Automation.AutomationProperties.GetName(button) == "Import key mappings from folder");
            open.GetBindingExpression(System.Windows.Controls.Primitives.ButtonBase.CommandProperty)!.UpdateTarget();
            Assert.That(open.Command, Is.SameAs(vm.OpenFolderCommand));
            Assert.That(open.ButtonText, Is.EqualTo("Load Samples Folder"));
            UiControlContract.AssertStyle<ListBox>((ListBox)drawer.FindName("KeySelector"), "OHsKeysKeyboardStyle");
            Assert.That(vm.SelectedEntry, Is.Null);
            Assert.That(open.Style, Is.SameAs(drawer.FindResource("OHsBalloonNeutralActionStyle")));
            Assert.That(open.IsEnabled, Is.True);
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); host.Close(); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { yield return root; for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants(System.Windows.Media.VisualTreeHelper.GetChild(root, i))) yield return child; }
    [Test]
    public void KeyboardNavigationCanReachLastVirtualizedKey()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var ctx = new InstrumentalTrackContext("project.json", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var vm = new KeysMappingDrawerVM(new(ctx, "Piano", 100, new Dictionary<string, string>(), [], [], 0), new WeakReferenceMessenger(), new ImmediateUiScheduler(), new StubSampleFileService([]), new StubSampleFileDialogService((string?)null));
        var drawer = new OHsKeysMappingDrawer(vm);
        var host = new Window { Content = drawer, DataContext = vm, Width = 600, Height = 400 };
        using var view = new InMemoryView(host);
        try
        {
            view.UpdateLayout();
            var rows = (ItemsControl)drawer.FindName("KeySelector");
            UiTestPump.Run(drawer.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.DataBind).Task);
            rows.GetBindingExpression(ItemsControl.ItemsSourceProperty)!.UpdateTarget();
            view.UpdateLayout();
            Assert.That(rows.DataContext, Is.SameAs(vm));
            Assert.That(rows.ItemsSource, Is.SameAs(vm.Rows));
            Assert.That(drawer.HandleNavigationKey(Key.System, Key.End, ModifierKeys.Alt), Is.True);
            Assert.That(((FrameworkElement)rows.ItemContainerGenerator.ContainerFromIndex(107)).DataContext, Is.TypeOf<KeyMappingEntry>());
            Assert.That(((KeyMappingEntry)((FrameworkElement)rows.ItemContainerGenerator.ContainerFromIndex(107)).DataContext).Key, Is.EqualTo("B9"));
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); host.Close(); }
    }
    [TestCase(FlowDirection.LeftToRight)]
    [TestCase(FlowDirection.RightToLeft)]
    public void NativeSelectionRecyclesStableKeysAndUsesAssignedContracts(FlowDirection flow)
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var creation=OHsKeysMappingTestContext.CreateAsync();UiTestPump.Run(creation);var c=creation.Result;
        var drawer=new OHsKeysMappingDrawer(c.VM);var host=new Window {Content=drawer,DataContext=c.VM,Width=600,Height=500,FlowDirection=flow};
        using var view=new InMemoryView(host);
        try
        {
            var selector=(ListBox)drawer.FindName("KeySelector");view.UpdateLayout();
            Assert.That(selector.SelectedItem,Is.Null);
            Assert.That(c.VM.CanEditSelectedMapping,Is.False);
            selector.SetCurrentValue(System.Windows.Controls.Primitives.Selector.SelectedValueProperty,"C5");
            UiTestPump.Run(selector.Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.DataBind).Task);
            Assert.That(c.VM.SelectedEntry!.Key,Is.EqualTo("C5"));
            var entry=c.VM.SelectedEntry;
            Assert.That(drawer.HandleNavigationKey(Key.End,ModifierKeys.Alt),Is.True);
            view.UpdateLayout();
            var container=(ListBoxItem)selector.ItemContainerGenerator.ContainerFromIndex(107);
            UiControlContract.AssertStyle<ListBoxItem>(container,"OHsKeysKeyboardItemStyle");
            UiControlContract.AssertTemplate(container,"OHsKeysKeyboardItemTemplate");
            Assert.That(container.Style.BasedOn,Is.SameAs(container.FindResource("BaseListBoxItemStyle")));
            Assert.That(System.Windows.Automation.AutomationProperties.GetName(container),Is.EqualTo("B9"));
            var realized=Enumerable.Range(0,selector.Items.Count).Count(n=>selector.ItemContainerGenerator.ContainerFromIndex(n) is not null);
            Assert.That(realized,Is.LessThan(selector.Items.Count));
            Assert.That(drawer.HandleNavigationKey(Key.Home,ModifierKeys.Alt),Is.True);
            Assert.That(c.VM.Rows.Single(r=>r.Key=="C5"),Is.SameAs(entry));
        }
        finally {UiTestPump.Run(c.DisposeAsync().AsTask());host.Close();}
    }
    [Test]
    public void CancelClearFromFocusedDraftDoesNotCommitIt()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var creation=OHsKeysMappingTestContext.CreateAsync((_,_)=>Task.FromResult(false));UiTestPump.Run(creation);var c=creation.Result;
        var drawer=new OHsKeysMappingDrawer(c.VM);var host=new Window {Content=drawer,DataContext=c.VM,Width=700,Height=500};
        using var view=new InMemoryView(host);
        try
        {
            UiTestPump.Run(c.VM.SelectKeyAsync("C5"));view.UpdateLayout();
            UiTestPump.Run(drawer.Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.DataBind).Task);
            var editor=Descendants(drawer).OfType<TextBox>().Single();
            UiControlContract.AssertStyle<TextBox>(editor,"OHsKeysInputStyle");
            var clear=(Onset.Resources.CustomControls.BalloonButton)drawer.FindName("ClearAllMappingsAction");
            c.VM.SelectedEntry!.DraftFileName="new.wav";
            editor.GetBindingExpression(TextBox.TextProperty)!.UpdateTarget();
            editor.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice,0,editor,clear){RoutedEvent=Keyboard.LostKeyboardFocusEvent});
            UiTestPump.Run(c.VM.ClearAllMappingsCommand.ExecuteAsync(null));
            Assert.That(c.Track.KeyFiles["C5"],Is.EqualTo("Piano/C5.wav"));
            Assert.That(c.VM.SelectedEntry.DraftFileName,Is.EqualTo("new.wav"));
        }
        finally {UiTestPump.Run(c.DisposeAsync().AsTask());host.Close();}
    }    [Test]
    public void FirstOpenCenteringDoesNotRepeatOnFocusOrResize()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var ctx=new InstrumentalTrackContext("project.json",null,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid());
        var vm=new KeysMappingDrawerVM(new(ctx,"Piano",100,new Dictionary<string,string>(),[],[],0),new WeakReferenceMessenger(),new ImmediateUiScheduler(),new StubSampleFileService([]),new StubSampleFileDialogService((string?)null));
        var drawer=new OHsKeysMappingDrawer(vm);var host=new Window {Content=drawer,Width=600,Height=500};
        using var view=new InMemoryView(host);
        try {
            drawer.FocusInitialAction();view.UpdateLayout();
            var scroll=Descendants((ListBox)drawer.FindName("KeySelector")).OfType<ScrollViewer>().First();
            scroll.ScrollToHorizontalOffset(20);view.UpdateLayout();
            drawer.FocusInitialAction();view.UpdateLayout();
            Assert.That(scroll.HorizontalOffset,Is.EqualTo(20));
            host.Width=700;view.UpdateLayout();
            Assert.That(scroll.HorizontalOffset,Is.EqualTo(20));
            Assert.That(vm.SelectedEntry,Is.Null);
        } finally {UiTestPump.Run(vm.DeactivateAsync());host.Close();}
    }
}
