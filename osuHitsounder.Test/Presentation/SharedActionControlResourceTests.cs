using CommunityToolkit.Mvvm.Input;
using osuHitsounder.Resources.CustomControls;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public sealed class SharedActionControlResourceTests
{
    private ResourceDictionary previousResources = null!;

    [SetUp]
    public void SetUp()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        previousResources = app.Resources;
        app.Resources = new ResourceDictionary();
    }

    [TearDown]
    public void TearDown() => Application.Current.Resources = previousResources;

    private static void LoadResources(string theme, bool deferFonts = false)
    {
        var resources = Application.Current.Resources;
        void Fonts()
        {
            foreach (var (key, file, face) in new[]
            {
                ("Display", "AllerDisplay_Std_Rg", "Aller Display"), ("Regular", "Aller_Std_Rg", "Aller"),
                ("Italic", "Aller_Std_It", "Aller"), ("Bold", "Aller_Std_Bd", "Aller"),
                ("BoldItalic", "Aller_Std_BdIt", "Aller"), ("Light", "Aller_Std_Lt", "Aller Light"),
                ("LightItalic", "Aller_Std_LtIt", "Aller Light")
            }) resources[key] = new FontFamily($"pack://application:,,,/osuHitsounder;component/Resources/Fonts/{file}.ttf#{face}");
        }
        if (!deferFonts) Fonts();
        foreach (var path in new[] { $"Themes/{theme}.xaml", "Themes/Tokens.xaml", "Converters/Converters.xaml", "Icons.xaml",
            "Styles/Grid.xaml", "Styles/StackPanel.xaml", "Styles/TextBlock.xaml", "Styles/ItemsControl.xaml",
            "Styles/Border.xaml", "Styles/ContentPresenter.xaml", "Styles/Button.xaml", "Styles/ScrollViewer.xaml",
            "Styles/MenuItem.xaml", "Styles/ContextMenu.xaml", "Styles/BalloonComboBox.xaml" })
            resources.MergedDictionaries.Add(new ResourceDictionary
                { Source = new Uri($"/osuHitsounder;component/Resources/{path}", UriKind.Relative) });
        if (deferFonts) Fonts();
    }

    [TestCase("Dark", false)]
    [TestCase("Light", false)]
    [TestCase("Dark", true)]
    [TestCase("Light", true)]
    public void AssignedControlContractsResolveInRuntimeScope(string theme, bool deferFonts)
    {
        LoadResources(theme, deferFonts);
        var control = new BalloonComboBox { ButtonText = "Actions", ShouldShowText = true,
            ButtonIcon = Application.Current.FindResource("AngleDown") };
        var host = new Grid { Resources = Application.Current.Resources, Children = { control } };
        UiTestPump.Layout(host, 300, 100);
        UiControlContract.AssertStyle<BalloonComboBox>(control, typeof(BalloonComboBox));
        UiControlContract.AssertTemplate(control, "BalloonControlTemplate");
        Assert.That(control.IsTabStop, Is.True);
        Assert.That(control.FocusVisualStyle, Is.SameAs(control.FindResource("OHsSampleManagerKeyboardFocusStyle")));
        var menu = new ContextMenu { Style = (Style)control.FindResource("OHsActionContextMenuStyle"), PlacementTarget = control };
        var item = new MenuItem { Header = "Action" };
        menu.Items.Add(item);
        UiTestPump.Layout(menu, 300, 200);
        UiControlContract.AssertStyle<ContextMenu>(menu, "OHsActionContextMenuStyle");
        UiControlContract.AssertTemplate(menu, "OHsActionContextMenuTemplate");
        UiControlContract.AssertStyle<MenuItem>(item, "OHsActionMenuItemStyle");
        UiControlContract.AssertTemplate(item, "OHsActionMenuItemTemplate");
        var destructive = (Style)menu.FindResource("OHsDestructiveActionMenuItemStyle");
        Assert.That(destructive.BasedOn, Is.SameAs(menu.FindResource("OHsActionMenuItemStyle")));
        Assert.That(menu.IsOpen, Is.False);
    }

    [TestCase("Dark")]
    [TestCase("Light")]
    public void RealMenuStylePreservesContextFlowCommandAndOwnerGate(string theme)
    {
        LoadResources(theme);
        var executions = 0;
        var command = new RelayCommand(() => executions++);
        var menu = new ContextMenu { Style = (Style)Application.Current.FindResource("OHsActionContextMenuStyle") };
        var item = new MenuItem { Header = "Action" };
        item.SetBinding(MenuItem.CommandProperty, new Binding { Mode = BindingMode.OneWay });
        menu.Items.Add(item);
        var acceptedItem = new MenuItem { Header = "Accepted action", Command = command };
        menu.Items.Add(acceptedItem);
        var control = new StubBalloonComboBox { ContextMenu = menu, DataContext = command, FlowDirection = FlowDirection.RightToLeft };
        control.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        UiTestPump.Layout(menu, 300, 200);
        Assert.That(item.Command, Is.SameAs(command));
        Assert.That(menu.FlowDirection, Is.EqualTo(FlowDirection.RightToLeft));
        Assert.That(menu.IsEnabled, Is.True);
        var provider = (IInvokeProvider)new MenuItemAutomationPeer(item).GetPattern(PatternInterface.Invoke);
        control.Activate();
        provider.Invoke();
        UiTestPump.Run(item.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task);
        Assert.That(executions, Is.EqualTo(1), "Inherited context supplies the bound command while active.");
        control.Activate();
        ((IInvokeProvider)new MenuItemAutomationPeer(acceptedItem).GetPattern(PatternInterface.Invoke)).Invoke();
        control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        Assert.That(menu.IsEnabled, Is.False);
        Assert.That(item.IsEnabled, Is.False);
        Assert.Throws<ElementNotEnabledException>(() => provider.Invoke());
        UiTestPump.Run(item.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task);
        Assert.That(executions, Is.EqualTo(2), "An already accepted native invocation is allowed to finish.");
        Assert.That(control.IsDropDownOpen, Is.False);
        Assert.That(menu.PlacementTarget, Is.Null);
        Assert.That(BindingOperations.IsDataBound(item, MenuItem.CommandProperty), Is.True);
    }

    [Test]
    public void ExplicitContextsAndIconOnlyAccessibleNameArePreserved()
    {
        LoadResources("Dark");
        var menuContext = new object();
        var itemContext = new object();
        var menu = new ContextMenu { Style = (Style)Application.Current.FindResource("OHsActionContextMenuStyle"), DataContext = menuContext };
        var item = new MenuItem { Header = "Action", DataContext = itemContext };
        menu.Items.Add(item);
        var control = new StubBalloonComboBox { ContextMenu = menu, DataContext = new object(), ShouldShowText = false,
            ButtonIcon = Application.Current.FindResource("AngleDown"), ToolTip = "More actions" };
        AutomationProperties.SetName(control, "More actions");
        control.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        UiTestPump.Layout(menu, 300, 200);
        Assert.That(menu.DataContext, Is.SameAs(menuContext));
        Assert.That(item.DataContext, Is.SameAs(itemContext));
        Assert.That(UIElementAutomationPeer.CreatePeerForElement(control)!.GetName(), Is.EqualTo("More actions"));
        control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
    }

    [Test]
    public void ManyGeneratedActionsVirtualizeAndRecycleWithinExplicitViewport()
    {
        LoadResources("Dark");
        var owner = new StubBalloonComboBox();
        owner.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        var menu = new ContextMenu { Style = (Style)Application.Current.FindResource("OHsActionContextMenuStyle"),
            PlacementTarget = owner, ItemsSource = Enumerable.Range(0, 200).Select(i => $"Action {i}").ToArray() };
        UiTestPump.Layout(menu, 300, 200);
        var first = Enumerable.Range(0, 200).Select(i => menu.ItemContainerGenerator.ContainerFromIndex(i)).OfType<MenuItem>().ToHashSet();
        Assert.That(first.Count, Is.GreaterThan(0).And.LessThan(200));
        // Documented scrolling part of OHsActionContextMenuTemplate; used to exercise behavior only.
        var scroll = (ScrollViewer)menu.Template.FindName("PART_ScrollViewer", menu);
        scroll.ScrollToVerticalOffset(100);
        UiTestPump.Layout(menu, 300, 200);
        var later = Enumerable.Range(0, 200).Select(i => menu.ItemContainerGenerator.ContainerFromIndex(i)).OfType<MenuItem>().ToHashSet();
        Assert.That(menu.ItemContainerGenerator.ContainerFromIndex(100), Is.Not.Null);
        Assert.That(later.Count, Is.LessThan(200));
        Assert.That(later.Overlaps(first), Is.True, "Recycling reuses previously generated containers.");
        owner.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
    }
}
