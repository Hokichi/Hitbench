using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Track = Hitbench.Core.Entities.Track;
using Hitbench.Resources.CustomControls;
using Hitbench.Resources.Helpers;
using Hitbench.ViewModels.Shell;
using Hitbench.Views.Pages;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class WorkstationTabNavigationTests
{
    [SetUp]
    public void SetUp() => new WorkstationPageIntegrationTests().SetUp();

    [Test]
    public void WorkstationSuppressesRealizedAndDynamicallyAddedControls()
    {
        var page = WorkstationTestPage.Create();
        var vm = (WorkstationPageVM)page.DataContext;
        vm.Tracks.Add(new Track { Name = "Kick" });
        using var view = new InMemoryView(new Window { Content = page, Width = 1100, Height = 700 });
        try
        {
            Assert.That(Descendants(page).OfType<OHsWorkstationTrackRow>().Any(), Is.True);
            AssertNoTabStops(page);
            var content = (Grid)page.FindName("WorkstationContent");
            var input = new TextBox { IsTabStop = true, Text = "Kick" };
            KeyboardNavigationPolicy.SetAllowTabNavigation(input, true);
            content.Children.Add(input);
            view.UpdateLayout();
            Assert.That(input.IsTabStop, Is.False);
            Assert.That(input.Focusable, Is.True);
            input.SelectAll();
            input.SelectedText = "Snare";
            Assert.That(input.Text, Is.EqualTo("Snare"));
            var footer = new OHsWorkstationFooter { DataContext = vm };
            content.Children.Add(footer);
            page.ShowVolumeOverlay(false, new Point(), false, false);
            view.UpdateLayout();
            AssertNoTabStops(page);
        }
        finally { UiTestPump.Run(page.DeactivateAsync()); }
    }

    [Test]
    public void ContextMenuFollowsPlacementTargetAndRestoresOptIns()
    {
        var target = new BalloonButton();
        KeyboardNavigationPolicy.SetSuppressTabNavigation(target, true);
        var item = new MenuItem { Header = "Action", IsTabStop = true };
        KeyboardNavigationPolicy.SetAllowTabNavigation(item, true);
        var child = new MenuItem { Header = "Nested", IsTabStop = true };
        KeyboardNavigationPolicy.SetAllowTabNavigation(child, true);
        item.Items.Add(child);
        var menu = new ContextMenu
        {
            PlacementTarget = target,
            Style = (Style)Application.Current.FindResource("OHsActionContextMenuStyle")
        };
        menu.Items.Add(item);
        UiTestPump.Layout(menu, 300, 160);
        UiTestPump.Run(menu.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle).Task);
        Assert.That(KeyboardNavigationPolicy.GetSuppressTabNavigation(menu), Is.True);
        Assert.That(item.IsTabStop, Is.False);
        Assert.That(child.IsTabStop, Is.False);
        menu.Items.Add("Generated action");
        UiTestPump.Layout(menu, 300, 200);
        var generated = (MenuItem)menu.ItemContainerGenerator.ContainerFromIndex(1);
        Assert.That(generated.IsTabStop, Is.False);
        menu.PlacementTarget = new BalloonButton();
        UiTestPump.Layout(menu, 300, 160);
        Assert.That(item.IsTabStop, Is.True);
        Assert.That(child.IsTabStop, Is.True);
        Assert.That(generated.IsTabStop, Is.True);
        menu.PlacementTarget = target;
        Assert.That(item.IsTabStop, Is.False);
        KeyboardNavigationPolicy.SetSuppressTabNavigation(target, false);
        Assert.That(item.IsTabStop, Is.True);
        menu.PlacementTarget = null;
        Assert.That(item.IsTabStop, Is.True);
    }

    [Test]
    public void LogicalPopupAndComboTemplateChildrenInheritSuppressionWithoutOpening()
    {
        var root = new Grid();
        KeyboardNavigationPolicy.SetSuppressTabNavigation(root, true);
        var input = new TextBox { IsTabStop = true };
        KeyboardNavigationPolicy.SetAllowTabNavigation(input, true);
        var popup = new Popup { Child = input };
        root.Children.Add(popup);
        Assert.That(input.IsTabStop, Is.False);
        var combo = new ComboBox
        {
            Style = (Style)Application.Current.FindResource("OHsComboBoxStyle")
        };
        root.Children.Add(combo);
        UiTestPump.Layout(root, 300, 100);
        var dropdown = (Popup)combo.Template.FindName("PART_Popup", combo);
        Assert.That(dropdown.IsOpen, Is.False);
        Assert.That(KeyboardNavigationPolicy.GetSuppressTabNavigation(dropdown.Child), Is.True);
        AssertNoTabStops(dropdown.Child);
        KeyboardNavigationPolicy.SetSuppressTabNavigation(root, false);
        Assert.That(input.IsTabStop, Is.True);
    }

    [Test]
    public void OwnerScopeBindingsPropagateThroughNestedPopupsAndCanBeReleased()
    {
        var owner = new Grid();
        KeyboardNavigationPolicy.SetSuppressTabNavigation(owner, true);
        var input = new TextBox { IsTabStop = true };
        KeyboardNavigationPolicy.SetAllowTabNavigation(input, true);
        var first = new Grid();
        var second = new Grid { Children = { input } };
        KeyboardNavigationPolicy.BindSuppressionScope(first, owner);
        KeyboardNavigationPolicy.BindSuppressionScope(second, first);
        Assert.That(input.IsTabStop, Is.False);
        KeyboardNavigationPolicy.SetSuppressTabNavigation(owner, false);
        Assert.That(input.IsTabStop, Is.True);
        KeyboardNavigationPolicy.SetSuppressTabNavigation(owner, true);
        Assert.That(input.IsTabStop, Is.False);
        KeyboardNavigationPolicy.BindSuppressionScope(first, null);
        Assert.That(input.IsTabStop, Is.True);
        Assert.That(BindingOperations.IsDataBound(first, KeyboardNavigationPolicy.SuppressTabNavigationProperty), Is.False);
    }

    [Test]
    public void PopupLifecycleAppliesItsOwnerScopeBeforeInput()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var owner = new Grid();
        KeyboardNavigationPolicy.SetSuppressTabNavigation(owner, true);
        var input = new TextBox { IsTabStop = true };
        KeyboardNavigationPolicy.SetAllowTabNavigation(input, true);
        var popup = new BasePopup { Content = input };
        try
        {
            popup.ApplyTabNavigationOwnerScope(owner);
            Assert.That(input.IsTabStop, Is.False);
            Assert.That(input.Focusable, Is.True);
            KeyboardNavigationPolicy.SetSuppressTabNavigation(owner, false);
            Assert.That(input.IsTabStop, Is.True);
        }
        finally { popup.Close(); }
    }

    [Test]
    public void TrackConfigurationAndControlPointEditorContentFollowOwningScope()
    {
        var page = WorkstationTestPage.Create();
        var vm = (WorkstationPageVM)page.DataContext;
        vm.Tracks.Add(new Track { Name = "Kick" });
        using var view = new InMemoryView(new Window { Content = page, Width = 1100, Height = 700 });
        try
        {
            var row = Descendants(page).OfType<OHsWorkstationTrackRow>().First();
            var config = (Popup)row.FindName("SampleConfigurationPopup");
            var lane = Descendants(page).OfType<OHsControlPointLane>().Single();
            var editor = (Popup)lane.FindName("EditorPopup");
            foreach (var popup in new[] { config, editor })
            {
                Assert.That(popup.IsOpen, Is.False);
                Assert.That(KeyboardNavigationPolicy.GetSuppressTabNavigation(popup.Child), Is.True);
                UiTestPump.Layout((FrameworkElement)popup.Child, 300, 300);
                AssertNoTabStops(popup.Child);
            }
        }
        finally { UiTestPump.Run(page.DeactivateAsync()); }
    }

    private static void AssertNoTabStops(DependencyObject root)
    {
        foreach (var element in Descendants(root).OfType<UIElement>())
            Assert.That(KeyboardNavigation.GetIsTabStop(element), Is.False, element.GetType().Name);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
}
