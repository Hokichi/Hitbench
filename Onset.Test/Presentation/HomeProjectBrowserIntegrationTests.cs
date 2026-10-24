using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Messaging;
using Onset.Resources.CustomControls;
using Onset.Resources.DataModels;
using Onset.ViewModels.Shell.HomePage;
using Onset.Views.Pages;

namespace Onset.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class HomeProjectBrowserIntegrationTests
{
    [Test]
    public void SlashShortcutIsHandledOutsideInputsOnlyWhenPageIsAvailable()
    {
        new SongsResponsiveLayoutTests().SetUp();
        var vm = new HomePageVM(new StubHomeFolderService(), new StubAppSettingsService(), new StubInstallationDialogService(),
            new StubProjectCreationDialogService(), new ImmediateUiScheduler(), new StubProjectFileService((_, _, _) => Task.FromResult("saved")), new WeakReferenceMessenger());
        var page = new HomePage(vm);
        var window = new Window { Content = page, Width = 1280, Height = 720 };
        using var view = new InMemoryView(window);
        try
        {
            Assert.That(page.HandleSearchShortcut(System.Windows.Input.Key.OemQuestion, System.Windows.Input.ModifierKeys.None, page), Is.True);
            Assert.That(page.HandleSearchShortcut(System.Windows.Input.Key.OemQuestion, System.Windows.Input.ModifierKeys.None, (TextBox)page.FindName("ProjectSearchTextBox")), Is.False);
            Assert.That(page.HandleSearchShortcut(System.Windows.Input.Key.OemQuestion, System.Windows.Input.ModifierKeys.Control, page), Is.False);
            vm.IsInteractionBlocked = true;
            Assert.That(page.HandleSearchShortcut(System.Windows.Input.Key.OemQuestion, System.Windows.Input.ModifierKeys.None, page), Is.False);
        }
        finally { page.Deactivate(); window.Close(); }
    }

    [Test]
    public void PersistentSearchAndProjectActionsUseExistingControlContracts()
    {
        new SongsResponsiveLayoutTests().SetUp();
        var row = new FolderEntry("project", "Middle match", default) { HasProjectMetadata = true };
        var vm = new HomePageVM(new StubHomeFolderService { Projects = _ => Task.FromResult(FolderSnapshot.ForProjects([row], true)) },
            new StubAppSettingsService(), new StubInstallationDialogService(), new StubProjectCreationDialogService(),
            new ImmediateUiScheduler(), new StubProjectFileService((_, _, _) => Task.FromResult("saved")), new WeakReferenceMessenger());
        var page = new HomePage(vm);
        var window = new Window { Content = page, Width = 1280, Height = 720 };
        using var view = new InMemoryView(window);
        try
        {
            UiTestPump.Run(page.LoadAsync(default)); view.UpdateLayout();
            var input = page.FindName("ProjectSearchTextBox") as TextBox;
            Assert.That(input, Is.Not.Null);
            Assert.That(input!.IsVisible, Is.True);
            Assert.That(((TextBox)page.FindName("SongSearchTextBox")).IsVisible, Is.True);
            input.Text = "match"; UiTestPump.Run(vm.SearchProjectsCommand.ExecutionTask!); view.UpdateLayout();
            Assert.That(vm.ProjectItems.All(item => !item.IsHeading), Is.True);
            var list = (ItemsControl)page.FindName("ProjectsList");
            var trigger = Children<BalloonComboBox>(list).Single();
            UiControlContract.AssertStyle<BalloonComboBox>(trigger, "OHsHomePageMenuTriggerStyle");
            Assert.That(trigger.ContextMenu.Style, Is.SameAs(trigger.FindResource("OHsActionContextMenuStyle")));
            var pin = (MenuItem)trigger.ContextMenu.Items[0];
            Assert.That(trigger.ContextMenu.PlacementTarget, Is.SameAs(trigger));
            Assert.That(trigger.ContextMenu.DataContext, Is.SameAs(row));
            // Closed native menus have no presented logical item tree. Supply that inheritance
            // boundary explicitly while verifying the owner's real placement/DataContext bindings above.
            foreach (MenuItem item in trigger.ContextMenu.Items) item.DataContext = trigger.ContextMenu.DataContext;
            trigger.ContextMenu.Items.Remove(pin);
            using var itemView = new InMemoryView(new Window { Content = pin, Width = 240, Height = 80 });
            pin.GetBindingExpression(MenuItem.CommandParameterProperty)!.UpdateTarget();
            pin.GetBindingExpression(MenuItem.CommandProperty)!.UpdateTarget();
            Assert.That(pin.Header, Is.EqualTo("Pin"));
            Assert.That(pin.CommandParameter, Is.SameAs(row));
            Assert.That(pin.Command, Is.SameAs(vm.ToggleProjectPinCommand));
            Assert.That(Children<Button>(list).Any(button => button.Command == vm.OpenProjectCommand), Is.True);
            vm.IsInteractionBlocked = true; view.UpdateLayout();
            Assert.That(trigger.IsEnabled, Is.False);
        }
        finally { page.Deactivate(); window.Close(); }
    }

    [Test]
    public void ProjectViewportRequestsNextPageThroughSharedViewerTemplate()
    {
        new SongsResponsiveLayoutTests().SetUp();
        var rows = Enumerable.Range(0, 200).Select(i => new FolderEntry(i.ToString(), $"Project {i}", default));
        var vm = new HomePageVM(new StubHomeFolderService { Projects = _ => Task.FromResult(FolderSnapshot.ForProjects(rows, true)) },
            new StubAppSettingsService(), new StubInstallationDialogService(), new StubProjectCreationDialogService(),
            new ImmediateUiScheduler(), new StubProjectFileService((_, _, _) => Task.FromResult("saved")), new WeakReferenceMessenger());
        var page = new HomePage(vm);
        var window = new Window { Content = page, Width = 1280, Height = 720 };
        using var view = new InMemoryView(window);
        try
        {
            UiTestPump.Run(page.LoadAsync(default)); view.UpdateLayout();
            var list = (ItemsControl)page.FindName("ProjectsList");
            var viewer = Children<ScrollViewer>(list).First();
            viewer.ScrollToEnd(); view.UpdateLayout();
            UiTestPump.Run(window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle).Task);
            Assert.That(vm.Projects.Count, Is.GreaterThan(64));
            view.UpdateLayout();
            Assert.That(viewer.VerticalOffset, Is.GreaterThan(0), "Appending a project page must preserve the current scroll position.");
        }
        finally { page.Deactivate(); window.Close(); }
    }

    private static IEnumerable<T> Children<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T item) yield return item;
            foreach (var descendant in Children<T>(child)) yield return descendant;
        }
    }
}
