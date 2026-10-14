using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Automation;
using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Resources.Messages;
using osuHitsounder.ViewModels.Popups;
using osuHitsounder.Views.Popups;
using osuHitsounder.Core.Entities;
using osuHitsounder.Services;
using osuHitsounder.Services.ProjectFiles;
using osuHitsounder.ViewModels.Shell;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class ControlPointsDrawerIntegrationTests
{
    [Test]
    public void ConnectedOwnerDrawerDragReplaceDeleteAndReopenKeepPlaybackLive()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var project = new ProjectData { Offsets = [0], BPMs = [120],
            ControlPoints = [new() { Timestamp = 500, Volume = 50 }, new() { Timestamp = 1000, Volume = 75 }] };
        var messages = new WeakReferenceMessenger(); var dialog = new StubControlPointsDialogService();
        var files = new DelayedProjectFileService(); var audio = new StubWorkstationAudioService();
        var owner = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", project)) },
            new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler(), projectFiles: files, messenger: messages, controlPointsDialog: dialog);
        var replace = false; var confirmations = 0; var opens = 0;
        dialog.Confirm = (_, _, _, _) => { confirmations++; return Task.FromResult(replace); };
        dialog.Show = (initial, _) =>
        {
            var editor = new ControlPointsDrawerVM(initial, messages, new ImmediateUiScheduler()); editor.Initialize();
            try
            {
                var drawer = new OHsControlPointsDrawer(editor); UiTestPump.Layout(drawer, 720, 220);
                if (++opens == 1)
                {
                    var row = editor.Rows[0]; var id = row.Id;
                    var input = new StubDragNumericUpDown { Style = (Style)drawer.FindResource("OHsControlPointsVolumeStyle"), DataContext = row };
                    BindingOperations.SetBinding(input, NumericUpDown.ValueProperty, new Binding(nameof(row.Volume)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
                    UiTestPump.Layout(input, 180, 40);
                    input.BeginVerticalDrag(100, 1, false); input.ContinueVerticalDrag(92, true); input.CancelVerticalDrag();
                    Assert.That(project.ControlPoints[0].Volume, Is.EqualTo(52));
                    Assert.That(new ControlPointVolumeTimeline(audio.ControlPoints).GetGain(22050), Is.EqualTo(.52f));
                    Assert.That(files.FirstWriteStarted, Is.True); Assert.That(files.LatestJson, Is.Null);
                    editor.SnapPointCommand.Execute(id); Assert.That(project.ControlPoints, Has.Count.EqualTo(2));
                    Assert.That(row.Timestamp, Is.EqualTo(500));
                    replace = true; editor.SnapPointCommand.Execute(id);
                    Assert.That(confirmations, Is.EqualTo(2)); Assert.That(project.ControlPoints, Has.Count.EqualTo(1));
                    Assert.That(editor.Rows.Single(), Is.SameAs(row)); Assert.That(row.Timestamp, Is.EqualTo(1000));
                    Assert.That(new ControlPointVolumeTimeline(audio.ControlPoints).GetGain(44100), Is.EqualTo(.52f));
                    files.Release();
                }
                else if (opens == 2)
                {
                    Assert.That((editor.Rows.Single().Timestamp, editor.Rows.Single().Volume), Is.EqualTo((1000, 52m)));
                    editor.DeletePointCommand.Execute(editor.Rows.Single().Id);
                    Assert.That(editor.Rows, Is.Empty); Assert.That(project.ControlPoints, Is.Empty);
                    Assert.That(new ControlPointVolumeTimeline(audio.ControlPoints).GetGain(44100), Is.EqualTo(1f));
                }
                else Assert.That(editor.Rows, Is.Empty);
                return Task.CompletedTask;
            }
            finally { files.Release(); UiTestPump.Run(editor.DeactivateAsync()); }
        };
        try
        {
            UiTestPump.Run(owner.LoadAsync("project.json", default)); owner.AcknowledgePresentationReady(owner.LoadGeneration);
            owner.Position = TimeSpan.FromMilliseconds(1030); owner.SelectedSubdivisionIndex = 3;
            var transport = (audio.PlayCalls, audio.PauseCalls, audio.SeekCalls);
            UiTestPump.Run(owner.AddControlPointCommand.ExecuteAsync(null)); Assert.That(owner.ErrorMessage, Is.Null);
            Assert.That(ProjectDataSerializer.Deserialize(files.LatestJson!).ControlPoints.Single().Volume, Is.EqualTo(52));
            UiTestPump.Run(owner.AddControlPointCommand.ExecuteAsync(null)); Assert.That(owner.ErrorMessage, Is.Null);
            UiTestPump.Run(owner.AddControlPointCommand.ExecuteAsync(null)); Assert.That(owner.ErrorMessage, Is.Null);
            Assert.That((audio.PlayCalls, audio.PauseCalls, audio.SeekCalls), Is.EqualTo(transport));
            Assert.That(ProjectDataSerializer.Deserialize(files.LatestJson!).ControlPoints, Is.Empty);
        }
        finally { files.Release(); UiTestPump.Run(owner.DeactivateAsync()); }
    }
    [Test]
    public void TimestampActionsBindIdentityUpdateAccessibilityAndRespectTimingGate()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var context = new ControlPointsContext("project.json", Guid.NewGuid(), Guid.NewGuid());
        var point = new ControlPointSnapshot(Guid.NewGuid(), 1000, 50, "Normal");
        var messages = new WeakReferenceMessenger(); var snaps = new List<SnapControlPointRequestedMessage>(); var deletes = new List<DeleteControlPointRequestedMessage>();
        messages.Register<SnapControlPointRequestedMessage>(this, (_, m) => snaps.Add(m));
        messages.Register<DeleteControlPointRequestedMessage>(this, (_, m) => deletes.Add(m));
        var vm = new ControlPointsDrawerVM(new(context, [point], 1, true, null, false, null), messages, new ImmediateUiScheduler()); vm.Initialize();
        try
        {
            var drawer = new OHsControlPointsDrawer(vm); UiTestPump.Layout(drawer, 720, 220);
            var rows = (ItemsControl)drawer.FindName("PointRows");
            var snap = Descendants(rows).OfType<BalloonButton>().Single(b => b.ButtonText == "Snap");
            var delete = Descendants(rows).OfType<BalloonButton>().Single(b => b.ButtonText == "Delete");
            UiControlContract.AssertStyle<BalloonButton>(snap, "OHsControlPointsSnapStyle");
            UiControlContract.AssertStyle<BalloonButton>(delete, "OHsControlPointsDeleteStyle");
            Assert.That(snap.ButtonIcon, Is.SameAs(drawer.FindResource("BullseyeSolid")));
            Assert.That(delete.ButtonIcon, Is.SameAs(drawer.FindResource("Delete")));
            var timestamp = Descendants(rows).OfType<TextBlock>().Single(t => BindingOperations.GetBinding(t, TextBlock.TextProperty)?.Path?.Path == "Timestamp");
            Assert.That(BindingOperations.GetBinding(timestamp, TextBlock.TextProperty)!.Mode, Is.EqualTo(BindingMode.OneWay));
            snap.Command.Execute(snap.CommandParameter); delete.Command.Execute(delete.CommandParameter);
            Assert.That(snaps.Single(), Is.EqualTo(new SnapControlPointRequestedMessage(context, point.Id)));
            Assert.That(deletes.Single(), Is.EqualTo(new DeleteControlPointRequestedMessage(context, point.Id)));
            messages.Send(new ControlPointsChangedMessage(new(context, [point with { Timestamp = 2000 }], 2, false, "No timing.", false, null)));
            rows.UpdateLayout();
            Assert.That(timestamp.Text, Is.EqualTo("2000")); Assert.That(AutomationProperties.GetName(snap), Does.Contain("2000"));
            Assert.That(AutomationProperties.GetName(delete), Does.Contain("2000")); Assert.That(snap.IsEnabled, Is.False);
            Assert.That(vm.UnavailableReason, Is.EqualTo("No timing."));
            messages.Send(new ControlPointsChangedMessage(new(context, [], 3, true, null, false, null)));
            Assert.That(rows.Items, Is.Empty);
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); messages.UnregisterAll(this); }
    }

    [Test]
    public void OversizedValidationRowRemainsScrollableAndActionableInShortViewport()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var context = new ControlPointsContext("project.json", Guid.NewGuid(), Guid.NewGuid());
        var vm = new ControlPointsDrawerVM(new(context, [new(Guid.NewGuid(), 1000, 50, "Normal")], 1, true, null, false, null), new WeakReferenceMessenger(), new ImmediateUiScheduler());
        try
        {
            vm.Rows[0].SetError(string.Join(" ", Enumerable.Repeat("Correct this invalid volume before closing.", 80)));
            var drawer = new OHsControlPointsDrawer(vm); UiTestPump.Layout(drawer, 640, 160);
            var rows = (ItemsControl)drawer.FindName("PointRows"); var scroll = Descendants(rows).OfType<ScrollViewer>().First();
            Assert.That(VirtualizingPanel.GetScrollUnit(rows), Is.EqualTo(ScrollUnit.Pixel));
            scroll.ScrollToBottom(); rows.UpdateLayout();
            Assert.That(scroll.VerticalOffset, Is.GreaterThan(0));
            Assert.That(scroll.VerticalOffset + scroll.ViewportHeight, Is.GreaterThanOrEqualTo(scroll.ExtentHeight - 1));
            scroll.ScrollToTop(); rows.UpdateLayout();
            var delete = Descendants(rows).OfType<BalloonButton>().Single(b => b.ButtonText == "Delete");
            Assert.That(delete.Command.CanExecute(delete.CommandParameter), Is.True);
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); }
    }
    [Test]
    public void CollectionRecyclesAndCanReachLastPointWithoutShowingWindow()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var ctx=new ControlPointsContext("project.json",Guid.NewGuid(),Guid.NewGuid());
        var points=Enumerable.Range(0,500).Select(i=>new ControlPointSnapshot(Guid.NewGuid(),i*1000,100,"Normal")).ToArray();
        var messages = new WeakReferenceMessenger();
        var vm=new ControlPointsDrawerVM(new(ctx,points,1,true,null,false,null),messages,new ImmediateUiScheduler()); vm.Initialize();
        try
        {
            var drawer=new OHsControlPointsDrawer(vm); UiTestPump.Layout(drawer,720,480);
            var rows=(ItemsControl)drawer.FindName("PointRows");
            Assert.That(Descendants(rows).OfType<NumericUpDown>().Count(),Is.LessThan(points.Length));
            Assert.That(drawer.HandleNavigationKey(Key.System,Key.End,ModifierKeys.Alt),Is.True);
            Assert.That(((FrameworkElement)rows.ItemContainerGenerator.ContainerFromIndex(499)).DataContext,Is.SameAs(vm.Rows[499]));
            Assert.That(vm.Rows[499].Timestamp,Is.EqualTo(499000));
            var moved = points[^1] with { Timestamp = 1 };
            messages.Send(new ControlPointsChangedMessage(new(ctx, points[..^1].Append(moved).Where(p => p.Id != points[0].Id).ToArray(), 2, true, null, false, null)));
            UiTestPump.Layout(drawer, 640, 180);
            Assert.That(vm.Rows[0].Id, Is.EqualTo(moved.Id));
            Assert.That(drawer.HandleNavigationKey(Key.System, Key.End, ModifierKeys.Alt), Is.True);
            Assert.That(((FrameworkElement)rows.ItemContainerGenerator.ContainerFromIndex(vm.Rows.Count - 1)).DataContext, Is.SameAs(vm.Rows[^1]));
            Assert.That(Descendants(rows).OfType<NumericUpDown>().Count(), Is.LessThan(vm.Rows.Count));
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); }
    }
    [Test]
    public void ServiceTearsDownRecipientsOnPresentationFailureAndCancellation()
    {
        var ctx=new ControlPointsContext("project.json",Guid.NewGuid(),Guid.NewGuid()); var initial=new ControlPointsSnapshot(ctx,[],1,true,null,false,null);
        var messenger=new WeakReferenceMessenger(); ControlPointsDrawerVM? created=null;
        var service=new osuHitsounder.Services.Dialogs.ControlPointsDialogService(()=>null,System.Windows.Threading.Dispatcher.CurrentDispatcher,
            s=>created=new(s,messenger,new ImmediateUiScheduler())) { Present=(_,_)=>Task.FromException(new IOException("presentation failed")) };
        Assert.Throws<IOException>(()=>UiTestPump.Run(service.ShowAsync(initial,default)));
        Assert.That(created,Is.Not.Null); Assert.That(created!.AddCommand.CanExecute(null),Is.False);
        using var cancellation=new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>UiTestPump.Run(service.ShowAsync(initial,cancellation.Token)));
    }
    [Test]
    public void ToolbarUsesClockActionAndOpensDrawerWithoutAddingPoint()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var project=new osuHitsounder.Core.Entities.ProjectData { Offsets=[0],BPMs=[120] }; var shown=0;
        var dialogs=new StubControlPointsDialogService { Show=(_,_)=>{shown++;return Task.CompletedTask;} };
        var vm=new osuHitsounder.ViewModels.Shell.WorkstationPageVM(new StubWorkstationSourceService {Load=(_,_)=>Task.FromResult(new osuHitsounder.Core.Entities.WorkstationSource("map","song",project))},
            new osuHitsounder.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(),Task.Delay),new ImmediateUiScheduler(),
            messenger:new WeakReferenceMessenger(),controlPointsDialog:dialogs);
        try
        {
            UiTestPump.Run(vm.LoadAsync("project.json",default)); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            var page=new osuHitsounder.Views.Pages.WorkstationPage(vm); var button=(BalloonButton)page.FindName("AddControlPointButton");
            UiControlContract.AssertStyle<BalloonButton>(button,"OHsWorkstationControlPointsButtonStyle");
            Assert.That(button.ButtonIcon,Is.SameAs(page.FindResource("ClockPlusOutline")));
            UiTestPump.Run(vm.AddControlPointCommand.ExecuteAsync(null)); Assert.That(shown,Is.EqualTo(1)); Assert.That(project.ControlPoints,Is.Empty);
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) foreach(var child in Descendants(VisualTreeHelper.GetChild(root,i))) yield return child;
    }
    [Test]
    public void InputsCommandsCloseAndStyleContractsWorkWithoutShowingWindow()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var messenger=new WeakReferenceMessenger(); var ctx=new ControlPointsContext("project.json",Guid.NewGuid(),Guid.NewGuid()); var id=Guid.NewGuid();
        var vm=new ControlPointsDrawerVM(new(ctx,[new(id,1000,50,"Normal")],1,true,null,false,null),messenger,new ImmediateUiScheduler()); vm.Initialize();
        var requests=new List<SetControlPointRequestedMessage>(); var added=0;
        messenger.Register<SetControlPointRequestedMessage>(this,(_,m)=>requests.Add(m)); messenger.Register<AddControlPointRequestedMessage>(this,(_,_)=>added++);
        try
        {
            var drawer=new OHsControlPointsDrawer(vm); UiTestPump.Layout(drawer,720,480);
            var list=(ItemsControl)drawer.FindName("PointRows"); list.UpdateLayout();
            UiControlContract.AssertStyle<ItemsControl>(list,"OHsControlPointsItemsStyle");
            Assert.That(list.Style.BasedOn, Is.SameAs(drawer.FindResource("OHsVirtualizedDrawerItemsStyle")));
            Assert.That(VirtualizingPanel.GetIsVirtualizing(list),Is.True); Assert.That(VirtualizingPanel.GetVirtualizationMode(list),Is.EqualTo(VirtualizationMode.Recycling));
            var number=Descendants(list).OfType<NumericUpDown>().Single(); var combo=Descendants(list).OfType<ComboBox>().Single();
            UiControlContract.AssertStyle<NumericUpDown>(number,"OHsControlPointsVolumeStyle"); UiControlContract.AssertStyle<ComboBox>(combo,"OHsControlPointsSamplesetStyle");
            Assert.That((number.LowerLimit, number.UpperLimit, number.Step), Is.EqualTo((5m, 100m, 1m)));
            Assert.That(number.ClampIntegerToRange && number.IsVerticalDragEnabled, Is.True);
            Assert.That(AutomationProperties.GetHelpText(number), Does.Contain("5–100%"));
            Assert.That(BindingOperations.GetBinding(number,NumericUpDown.ValueProperty)!.Mode,Is.EqualTo(BindingMode.TwoWay));
            Assert.That(BindingOperations.GetBinding(combo,ComboBox.SelectedItemProperty)!.Mode,Is.EqualTo(BindingMode.TwoWay));
            var editor=(TextBox)number.Template.FindName("PART_TextBox",number);
            editor.Text="25"; Assert.That(number.CommitInput(),Is.True); Assert.That(requests.Last().Volume,Is.EqualTo(25));
            Assert.That(BindingOperations.IsDataBound(number,NumericUpDown.ValueProperty),Is.True);
            combo.SetCurrentValue(ComboBox.SelectedItemProperty,"Drum"); Assert.That(requests.Last().Sampleset,Is.EqualTo("Drum"));
            editor.Text="25.5"; Assert.That(drawer.TryPrepareClose(),Is.False); Assert.That(requests.Last().Volume,Is.EqualTo(25));
            editor.Text="0"; Assert.That(drawer.TryPrepareClose(),Is.True); Assert.That(requests.Last().Volume,Is.EqualTo(5));
            var add=(BalloonButton)drawer.FindName("AddPointButton"); Assert.That(add.ButtonIcon,Is.SameAs(drawer.FindResource("PlusSolid"))); add.Command.Execute(null); Assert.That(added,Is.EqualTo(1));
            Assert.That(drawer.HandleNavigationKey(Key.PageDown,Key.None,ModifierKeys.None),Is.True);
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); messenger.UnregisterAll(this); }
    }
}
