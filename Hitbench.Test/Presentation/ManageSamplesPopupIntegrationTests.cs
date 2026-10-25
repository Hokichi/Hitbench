using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.CustomControls;
using Hitbench.ViewModels.Popups;
using Hitbench.Views.Popups;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA)] [NonParallelizable]
public sealed class ManageSamplesPopupIntegrationTests
{
    private static System.Windows.Window CreateHost(ManageSamplesDrawer drawer) => new() { Content = drawer, Width = 720, Height = 480 };

    [Test]
    public void RowActionsUseIndependentMenusAndCorrectCommands()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var vm = CreateManager(new StubSampleFileService(["hit.wav", "kick.wav"]));
        var drawer = new ManageSamplesDrawer(vm); var host = CreateHost(drawer);
        try
        {
            UiTestPump.Run(vm.InitializeAsync(default)); using var view = new InMemoryView(host, scheduleLayout: false);
            var triggers = Descendants(view.Root).OfType<BalloonComboBox>().ToArray();
            Assert.That(triggers.Length, Is.EqualTo(2));
            Assert.That(triggers[0].Style, Is.SameAs(drawer.FindResource("OHsManageSamplesMenuStyle")));
            Assert.That(triggers[0].ContextMenu, Is.Not.SameAs(triggers[1].ContextMenu));
            foreach (var trigger in triggers)
            {
                Assert.That(trigger.ContextMenu.Style, Is.SameAs(drawer.FindResource("OHsActionContextMenuStyle")));
                UiTestPump.Layout(trigger.ContextMenu, 320, 200);
                var items = trigger.ContextMenu.Items.Cast<System.Windows.Controls.MenuItem>().ToArray();
                Assert.That(items.Select(item => item.Header), Is.EqualTo(new[] { "Edit", "Delete", "Save" }));
                Assert.That(items.Select(item => item.Command), Is.EqualTo(new System.Windows.Input.ICommand[] { vm.EditCommand, vm.DeleteCommand, vm.SaveCommand }));
                Assert.That(items.All(item => ReferenceEquals(item.CommandParameter, trigger.DataContext)), Is.True, $"row={trigger.DataContext}, target={trigger.ContextMenu.PlacementTarget}, context={trigger.ContextMenu.DataContext}, params={string.Join(",", items.Select(item => item.CommandParameter?.ToString() ?? "null"))}");
                Assert.That(items[1].Style, Is.SameAs(drawer.FindResource("OHsDestructiveActionMenuItemStyle")));
                Assert.That(items[2].Style, Is.SameAs(drawer.FindResource("OHsActionMenuItemStyle")));
            }
            triggers[0].DataContext = vm.Entries[1]; view.UpdateLayout(); UiTestPump.Layout(triggers[0].ContextMenu, 320, 200);
            Assert.That(triggers[0].ContextMenu.Items.Cast<System.Windows.Controls.MenuItem>().All(item => ReferenceEquals(item.CommandParameter, vm.Entries[1])), Is.True);
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); host.Close(); }
    }

    [Test]
    public void RenameConfirmationIsDistinctFromLibrarySave()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var files = new StubSampleFileService(["hit.wav", "kick.wav"]) { Save = (_, _, _) => throw new AssertionException("Rename must not save to library") };
        var vm = CreateManager(files); var drawer = new ManageSamplesDrawer(vm); var host = CreateHost(drawer);
        try
        {
            UiTestPump.Run(vm.InitializeAsync(default)); using var view = new InMemoryView(host, scheduleLayout: false);
            var row = vm.Entries[0]; var other = vm.Entries[1];
            UiTestPump.Run(vm.EditCommand.ExecuteAsync(row)); view.UpdateLayout();
            var confirm = Descendants(view.Root).OfType<BalloonButton>().Single(button => ReferenceEquals(button.Command, vm.CommitRenameCommand) && ReferenceEquals(button.DataContext, row));
            Assert.That(confirm.Command, Is.SameAs(vm.CommitRenameCommand));
            Assert.That(confirm.Style, Is.SameAs(drawer.FindResource("OHsManageSamplesConfirmRenameStyle")));
            Assert.That(System.Windows.Automation.AutomationProperties.GetName(confirm), Does.StartWith("Confirm rename"));
            Assert.That(confirm.IsEnabled, Is.True);
            Assert.That(vm.SaveCommand.CanExecute(row), Is.False);
            Assert.That(other.IsInteractionEnabled, Is.False);
            Assert.That(((BalloonButton)drawer.FindName("AddNewSampleButton")).IsEnabled, Is.False);
            vm.CancelEditCommand.Execute(null); view.UpdateLayout();
            Assert.That(confirm.Visibility, Is.EqualTo(System.Windows.Visibility.Collapsed));
            Assert.That(other.IsInteractionEnabled, Is.True);
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); host.Close(); }
    }

    [Test]
    public void InvalidInputDisablesRenameConfirmationAndEnterRespectsValidation()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var renameCalls = 0;
        var files = new StubSampleFileService(["hit.wav"]) { Rename = (_, _, _, _) => { renameCalls++; return Task.FromResult("kick.wav"); } };
        var vm = CreateManager(files); var drawer = new ManageSamplesDrawer(vm); var host = CreateHost(drawer);
        try
        {
            UiTestPump.Run(vm.InitializeAsync(default)); using var view = new InMemoryView(host, scheduleLayout: false);
            UiTestPump.Run(vm.EditCommand.ExecuteAsync(vm.Entries.Single())); view.UpdateLayout();
            var input = Descendants(view.Root).OfType<System.Windows.Controls.TextBox>().Single();
            var save = Descendants(view.Root).OfType<BalloonButton>().Single(b => ReferenceEquals(b.Command, vm.CommitRenameCommand));
            input.Text = "CON";
            Assert.That(save.IsEnabled, Is.False); Assert.That(System.Windows.Controls.Validation.GetHasError(input), Is.True);
            UiTestPump.Run(drawer.HandleEnterAsync()); Assert.That(renameCalls, Is.Zero); Assert.That(vm.ActiveEdit, Is.Not.Null);
            input.Text = "kick"; Assert.That(save.IsEnabled, Is.True);
            UiTestPump.Run(drawer.HandleEnterAsync()); Assert.That(renameCalls, Is.EqualTo(1)); Assert.That(vm.ActiveEdit, Is.Null);
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); host.Close(); }
    }

    [Test]
    public void FocusLossDoesNotRenameAndEscapeCancelsBeforeClosing()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var files = new StubSampleFileService(["hit.wav"]) { Rename = (_, _, _, _) => throw new AssertionException("Focus loss cannot save") };
        var vm = CreateManager(files); var drawer = new ManageSamplesDrawer(vm); var host = CreateHost(drawer); var closeRequests = 0;
        drawer.CloseRequested += (_, _) => closeRequests++;
        try
        {
            UiTestPump.Run(vm.InitializeAsync(default)); using var view = new InMemoryView(host, scheduleLayout: false);
            UiTestPump.Run(vm.EditCommand.ExecuteAsync(vm.Entries.Single())); view.UpdateLayout();
            var input = Descendants(view.Root).OfType<System.Windows.Controls.TextBox>().Single(); input.Text = "kick";
            input.RaiseEvent(new System.Windows.Input.KeyboardFocusChangedEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, Environment.TickCount, input, drawer)
                { RoutedEvent = System.Windows.Input.Keyboard.LostKeyboardFocusEvent });
            Assert.That(vm.ActiveEdit, Is.Not.Null); Assert.That(vm.Entries.Single().FileName, Is.EqualTo("hit.wav"));
            Assert.That(drawer.HandleEscape(), Is.True); Assert.That(vm.ActiveEdit, Is.Null); Assert.That(closeRequests, Is.Zero);
            Assert.That(drawer.HandleEscape(), Is.True); Assert.That(closeRequests, Is.EqualTo(1));
            Assert.That(System.Windows.Input.KeyboardNavigation.GetTabNavigation(drawer), Is.EqualTo(System.Windows.Input.KeyboardNavigationMode.Cycle));
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); host.Close(); }
    }

    [Test]
    public void IdleCloseRaisesRequest()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var vm = CreateManager(new StubSampleFileService([])); var drawer = new ManageSamplesDrawer(vm); var requested = 0;
        drawer.CloseRequested += (_, _) => requested++;
        try
        {
            UiTestPump.Run(vm.InitializeAsync(default));
            var close = (BalloonButton)drawer.FindName("CloseDrawerButton");
            close.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.That(requested, Is.EqualTo(1));
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); }
    }

    private static ManageSamplesPopupVM CreateManager(StubSampleFileService files) => new(new("project.json", null, Guid.NewGuid()), files, new(files),
        new StubSampleFileDialogService([]), new StubSampleRemovalDialogService(), new WeakReferenceMessenger(),
        new Hitbench.Services.Ui.UiScheduler(System.Windows.Threading.Dispatcher.CurrentDispatcher));

    [Test] public void RowPlayBindsToPreviewAndRowUsesHoverContract()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var files=new StubSampleFileService(["hit.wav"]); var preview=new StubSamplePreviewService();
        var vm=new ManageSamplesPopupVM(new("project.json",null,Guid.NewGuid()),files,new(files),new StubSampleFileDialogService(Array.Empty<string>()),new StubSampleRemovalDialogService(),new WeakReferenceMessenger(),new Hitbench.Services.Ui.UiScheduler(System.Windows.Threading.Dispatcher.CurrentDispatcher),preview);
        var drawer=new ManageSamplesDrawer(vm); var popup=CreateHost(drawer);
        try
        {
            UiTestPump.Run(vm.InitializeAsync(default)); using var view=new InMemoryView(popup,scheduleLayout:false);
            var play=Descendants(view.Root).OfType<BalloonButton>().Single(b=>b.ButtonText=="Play");
            Assert.That(play.Command,Is.SameAs(vm.PlayCommand)); Assert.That(play.CommandParameter,Is.SameAs(vm.Entries.Single()));
            Assert.That(play.ButtonIcon,Is.SameAs(drawer.FindResource("MediaPlay")));
            UiTestPump.Run(vm.PlayCommand.ExecuteAsync(play.CommandParameter)); Assert.That(preview.Playing,Is.True);
            Assert.That(play.ButtonIcon,Is.SameAs(drawer.FindResource("MediaPause")));
            Assert.That(play.ButtonText,Is.EqualTo("Pause"));
            var menu = Descendants(view.Root).OfType<BalloonComboBox>().Single().ContextMenu;
            UiTestPump.Layout(menu, 320, 200);
            Assert.That(menu.Items.Cast<System.Windows.Controls.MenuItem>().All(item => item.IsEnabled), Is.True);
            var row=Descendants(view.Root).OfType<System.Windows.Controls.Border>().Single(b=>ReferenceEquals(b.DataContext,vm.Entries.Single()) && b.Style==drawer.FindResource("OHsManageSamplesRowStyle"));
            Assert.That(row.Style.BasedOn,Is.SameAs(drawer.FindResource("BaseBorderStyle")));
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); popup.Close(); }
    }
    [Test] public void LongCatalogUsesVirtualizedContainers()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var names = Enumerable.Range(0,2000).Select(i=>$"sample{i}.wav").ToArray(); var files=new StubSampleFileService(names);
        var vm=new ManageSamplesPopupVM(new("project.json",null,Guid.NewGuid()),files,new(files),new StubSampleFileDialogService(Array.Empty<string>()),new StubSampleRemovalDialogService(),new WeakReferenceMessenger(),new Hitbench.Services.Ui.UiScheduler(System.Windows.Threading.Dispatcher.CurrentDispatcher));
        var drawer=new ManageSamplesDrawer(vm); var popup=CreateHost(drawer);
        try
        {
            UiTestPump.Run(vm.InitializeAsync(default)); using var view=new InMemoryView(popup,scheduleLayout:false);
            var list=(System.Windows.Controls.ItemsControl)drawer.FindName("SampleRows");
            Assert.That(list.Items.Count,Is.EqualTo(2000));
            Assert.That(list.ItemContainerGenerator.ContainerFromIndex(1999),Is.Null);
            Assert.That(list.ItemContainerGenerator.ContainerFromIndex(0),Is.Not.Null);
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); popup.Close(); }
    }
    [Test] public void RowEditUpdatesDraftThroughRealBinding()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var files = new StubSampleFileService(["hit.wav"]);
        var vm = new ManageSamplesPopupVM(new("project.json", null, Guid.NewGuid()), files, new(files), new StubSampleFileDialogService(Array.Empty<string>()),
            new StubSampleRemovalDialogService(), new WeakReferenceMessenger(), new Hitbench.Services.Ui.UiScheduler(System.Windows.Threading.Dispatcher.CurrentDispatcher));
        var drawer = new ManageSamplesDrawer(vm); var popup = CreateHost(drawer);
        try
        {
            UiTestPump.Run(vm.InitializeAsync(default)); using var view = new InMemoryView(popup, scheduleLayout:false);
            var edit = (System.Windows.Controls.MenuItem)Descendants(view.Root).OfType<BalloonComboBox>().Single().ContextMenu.Items[0];
            Assert.That(edit.Command, Is.SameAs(vm.EditCommand));
            UiTestPump.Run(vm.EditCommand.ExecuteAsync(edit.CommandParameter)); view.UpdateLayout();
            var input = Descendants(view.Root).OfType<System.Windows.Controls.TextBox>().Single();
            input.Text = "kick"; Assert.That(vm.DraftStem, Is.EqualTo("kick"));
            vm.CancelEditCommand.Execute(null); Assert.That(vm.Entries.Single().FileName, Is.EqualTo("hit.wav"));
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); popup.Close(); }
    }
    private static IEnumerable<System.Windows.DependencyObject> Descendants(System.Windows.DependencyObject root)
    { yield return root; for(var i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++) foreach(var c in Descendants(System.Windows.Media.VisualTreeHelper.GetChild(root,i))) yield return c; }
    [Test] public void AddActionAndRowsBindToManagerCommands()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var files = new StubSampleFileService([]);
        var vm = new ManageSamplesPopupVM(new("project.json", null, Guid.NewGuid()), files, new(files),
            new StubSampleFileDialogService(Array.Empty<string>()), new StubSampleRemovalDialogService(), new WeakReferenceMessenger(), new Hitbench.Services.Ui.UiScheduler(System.Windows.Threading.Dispatcher.CurrentDispatcher));
        var drawer = new ManageSamplesDrawer(vm); var popup = CreateHost(drawer);
        try
        {
            UiTestPump.Run(vm.InitializeAsync(default)); using var view = new InMemoryView(popup, scheduleLayout: false);
            var add = (BalloonButton)drawer.FindName("AddNewSampleButton");
            Assert.That(add.Command, Is.SameAs(vm.AddNewSampleCommand));
            Assert.That(add.ButtonIcon, Is.SameAs(drawer.FindResource("PlusSolid")));
            Assert.That(add.IsEnabled, Is.True);
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); popup.Close(); }
    }
}
