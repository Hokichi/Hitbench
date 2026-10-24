using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.CustomControls;
using Hitbench.Resources.DataModels;
using Hitbench.ViewModels.Shell.HomePage;
using Hitbench.ViewModels.Shell;
using Hitbench.Views.Pages;
using Hitbench.Core.Entities;
using Hitbench.Resources.Messages;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class SongsResponsiveLayoutTests
{
    [TestCase(FlowDirection.LeftToRight, "Dark")]
    [TestCase(FlowDirection.RightToLeft, "Light")]
    public void CompactTransitionsPreserveDraftSearchAndVirtualizedLastSongAccess(FlowDirection direction, string theme)
    {
        Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary
            { Source = new Uri($"/Hitbench;component/Resources/Themes/{theme}.xaml", UriKind.Relative) };
        var vm = CreateVM();
        for (var i = 0; i < 2000; i++)
            vm.Songs.Add(new FolderEntry($@"C:\Songs\{i}", $"音楽 العربية ภาษาไทย {i}", DateTimeOffset.MinValue));
        var selectedMapset = vm.Songs[37];
        vm.SelectedMapset = selectedMapset;
        var openProject = vm.OpenProjectCommand;
        vm.ProjectSearchText = "音楽 العربية ภาษาไทย";
        var page = new HomePage(vm) { FlowDirection = direction };
        var projectBrowser = page.FindName("ProjectsList");
        var window = new Window { Content = page, Width = 1280, Height = 720 };
        using var view = new InMemoryView(window);
        foreach (var width in new[] { 1280d, 959d, 960d, 640d, 1280d, 640d })
        {
            window.Width = width;
            window.Height = width == 640 ? 360 : 720;
            view.UpdateLayout();
            page.UpdateSongsLayout(new Size(1920, 1040));
            view.UpdateLayout();
            Assert.That(page.IsCompactLayout, Is.EqualTo(width < 960));
            Assert.That(vm.SelectedMapset, Is.SameAs(selectedMapset));
            Assert.That(vm.OpenProjectCommand, Is.SameAs(openProject));
            Assert.That(page.FindName("ProjectsList"), Is.SameAs(projectBrowser));
            Assert.That(((TextBox)page.FindName("ProjectSearchTextBox")).Text, Is.EqualTo("音楽 العربية ภาษาไทย"));
            Assert.That(((TextBox)page.FindName("SongSearchTextBox")).IsEnabled, Is.True);
            Assert.That(vm.Songs, Has.Count.EqualTo(2000));
            var panel = Descendants<SpacedUniformGrid>(page).Single();
            Assert.That(panel.Children.Count, Is.GreaterThan(0).And.LessThan(2000));
            Assert.That(panel.VerticalOffset, Is.InRange(0, Math.Max(0, panel.ExtentHeight - panel.ViewportHeight)));
        }
        var songs = Descendants<SpacedUniformGrid>(page).Single();
        songs.SetVerticalOffset(double.MaxValue);
        view.UpdateLayout();
        Assert.That(Descendants<Button>(songs).Last().DataContext, Is.SameAs(vm.Songs.Last()));
        window.Close();
    }

    [Test]
    public void ScrollFramesDoNotRefreshSongSizingButGeometryChangesDo()
    {
        var vm = CreateVM();
        for (var i = 0; i < 2000; i++) vm.Songs.Add(new FolderEntry($@"C:\Songs\{i}", i.ToString(), DateTimeOffset.MinValue));
        var page = new HomePage(vm);
        var window = TestShell.Create(page, new WeakReferenceMessenger(), WorkstationTestPage.Create, vm, new StubAppSettingsService());
        window.Width = 1922;
        window.Height = 1082;
        using var view = new InMemoryView(window, scheduleLayout: false);
        for (var i = 0; i < 5; i++) { view.UpdateLayout(); SongsScrollPerformanceTests.Drain(); }
        var panel = Descendants<SpacedUniformGrid>(page).Single();
        panel.SetVerticalOffset(3 * panel.RowStride + .25);
        view.UpdateLayout();
        SongsScrollPerformanceTests.Drain();
        var shell = window.SongsLayoutRefreshCount;
        var sizing = page.SongsLayoutCalculationCount;
        var measure = panel.MeasurePassCount;
        for (var i = 0; i < 10; i++)
        {
            panel.SetVerticalOffset(3 * panel.RowStride + (i % 2 == 0 ? .75 : .25));
            view.UpdateLayout();
            SongsScrollPerformanceTests.Drain();
        }
        Assert.Multiple(() =>
        {
            Assert.That(window.SongsLayoutRefreshCount, Is.EqualTo(shell));
            Assert.That(page.SongsLayoutCalculationCount, Is.EqualTo(sizing));
            Assert.That(panel.MeasurePassCount, Is.EqualTo(measure));
        });
        vm.IsSongSearchExpanded = true;
        vm.SongsLoadStatus = new string('W', 400);
        page.FlowDirection = FlowDirection.RightToLeft;
        window.Width = page.MinWidth + 2;
        window.Height = page.MinHeight + 42;
        view.UpdateLayout();
        SongsScrollPerformanceTests.Drain();
        Assert.That(page.SongsLayoutCalculationCount, Is.GreaterThan(sizing));
        Assert.That(panel.VerticalOffset, Is.InRange(0, Math.Max(0, panel.ExtentHeight - panel.ViewportHeight)));
        Assert.That(vm.Songs.Count, Is.EqualTo(2000));
        window.Close();
    }

    [TestCase(FlowDirection.LeftToRight, "Dark")]
    [TestCase(FlowDirection.RightToLeft, "Light")]
    public void TemplateLaneChangeInvalidatesLayoutGuard(FlowDirection direction, string theme)
    {
        Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary
        { Source = new Uri($"/Hitbench;component/Resources/Themes/{theme}.xaml", UriKind.Relative) };
        var page = new HomePage(CreateVM()) { FlowDirection = direction };
        var window = new Window { Content = page, Width = 1920, Height = 1040 };
        using var view = new InMemoryView(window, scheduleLayout: false);
        page.UpdateSongsLayout(new Size(1920, 1040));
        view.UpdateLayout();
        var list = (ItemsControl)page.FindName("SongsList");
        var viewer = Descendants<ScrollViewer>(list).Single();
        var template = viewer.Template;
        viewer.Template = null;
        viewer.ApplyTemplate();
        viewer.Template = template;
        view.UpdateLayout();
        var scrollbar = (System.Windows.Controls.Primitives.ScrollBar)viewer.Template.FindName("PART_VerticalScrollBar", viewer);
        scrollbar.Width = 24;
        view.UpdateLayout();
        page.UpdateSongsLayout(new Size(1920, 1040));
        view.UpdateLayout();
        var panel = Descendants<SpacedUniformGrid>(page).Single();
        Assert.That(panel.ExtentWidth, Is.EqualTo(panel.ViewportWidth).Within(.01));
        window.Close();
    }

    [TestCase(FlowDirection.LeftToRight, "Dark")]
    [TestCase(FlowDirection.RightToLeft, "Light")]
    public void ExpandedSearchKeepsBindingsAndAssignedStylesAcrossResize(FlowDirection direction, string theme)
    {
        Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary
        { Source = new Uri($"/Hitbench;component/Resources/Themes/{theme}.xaml", UriKind.Relative) };
        var vm = CreateVM();
        var page = new HomePage(vm);
        var window = new Window { Content = page, Width = 1920, Height = 1040, FlowDirection = direction };
        using var view = new InMemoryView(window);
        page.UpdateSongsLayout(new Size(1920, 1040));
        vm.IsSongSearchExpanded = true;
        vm.SongSearchText = new string('W', 200);
        view.UpdateLayout();
        window.Width = page.MinWidth;
        window.Height = page.MinHeight;
        view.UpdateLayout();
        var input = (TextBox?)page.FindName("SongSearchTextBox");
        Assert.That(input, Is.Not.Null, "Expanded search input must exist.");
        UiControlContract.AssertStyle<TextBox>(input!, "HomePageSearchInputStyle");
        Assert.That(input!.IsVisible, Is.True);
        Assert.That(vm.SongSearchText, Is.EqualTo(new string('W', 200)));
        window.Close();
    }
    [SetUp]
    public void SetUp()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Clear();
        UiTestPump.EnsureFontResources(app);
        foreach (var resource in new[] { "Themes/Dark.xaml", "Themes/Tokens.xaml", "Converters/Converters.xaml", "Icons.xaml", "Styles/TextBlock.xaml", "Styles/ItemsControl.xaml", "Styles/StackPanel.xaml", "Styles/Image.xaml", "Styles/ColumnDefinition.xaml", "Styles/Grid.xaml", "Styles/Border.xaml", "Styles/ContentPresenter.xaml", "Styles/Button.xaml", "Styles/MenuItem.xaml", "Styles/ContextMenu.xaml", "Styles/BalloonComboBox.xaml", "Styles/SegmentedToggleGroup.xaml", "Styles/SegmentedToggleOption.xaml", "Styles/ToolTip.xaml", "Styles/Window.xaml", "Styles/ScrollViewer.xaml", "Styles/TextBox.xaml", "Styles/ComboBox.xaml", "Styles/ListBox.xaml", "Styles/Slider.xaml" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            { Source = new Uri($"/Hitbench;component/Resources/{resource}", UriKind.Relative) });
    }

    [TestCase(FlowDirection.LeftToRight)]
    [TestCase(FlowDirection.RightToLeft)]
    public void ResizingRetainsVirtualizationAndLastSongAccess(FlowDirection direction)
    {
        var vm = CreateVM();
        for (var i = 0; i < 2000; i++)
            vm.Songs.Add(new FolderEntry($@"C:\Songs\{i}", i.ToString(), DateTimeOffset.MinValue)
            { Artist = "Artist", Title = new string('x', 200), DisplayName = "Artist - Title" });
        var page = new HomePage(vm) { FlowDirection = direction };
        var window = new Window { Content = page, Width = 1920, Height = 1040 };
        using var view = new InMemoryView(window);
        page.UpdateSongsLayout(new Size(1920, 1040));
        view.UpdateLayout();
        var panel = Descendants<SpacedUniformGrid>(page).Single();
        Assert.Multiple(() =>
        {
            Assert.That(panel.ExtentWidth, Is.EqualTo(panel.ViewportWidth).Within(.01));
            Assert.That(panel.Children.Count, Is.LessThanOrEqualTo((panel.VisibleRowCount + 3) * panel.Columns));
        });
        panel.SetVerticalOffset(10000);
        view.UpdateLayout();
        window.Width = page.MinWidth;
        window.Height = page.MinHeight;
        view.UpdateLayout();
        page.UpdateSongsLayout(new Size(1920, 1040));
        view.UpdateLayout();
        var button = Descendants<Button>(panel).First();
        Assert.Multiple(() =>
        {
            UiControlContract.AssertStyle<Button>(button, "HomePageSongCardButtonStyle");
            Assert.That(panel.Children.Count, Is.LessThanOrEqualTo((panel.VisibleRowCount + 3) * panel.Columns));
            Assert.That(panel.ExtentWidth, Is.EqualTo(panel.ViewportWidth).Within(.01));
        });
        panel.SetVerticalOffset(double.MaxValue);
        view.UpdateLayout();
        Assert.That(Descendants<Button>(panel).Last().DataContext, Is.SameAs(vm.Songs.Last()));
        window.Width = 1920;
        window.Height = 1040;
        view.UpdateLayout();
        Assert.That(panel.VerticalOffset, Is.InRange(0, Math.Max(0, panel.ExtentHeight - panel.ViewportHeight)));
    }

    [Test]
    public void MonitorBaselineCanChangeBeforeMaximizing()
    {
        var page = new HomePage(CreateVM());
        var window = new Window { Content = page, Width = 1280, Height = 720 };
        using var view = new InMemoryView(window);
        page.UpdateSongsLayout(new Size(1920, 1040));
        view.UpdateLayout();
        var panel = Descendants<SpacedUniformGrid>(page).Single();
        var height = panel.ItemHeight;
        page.UpdateSongsLayout(new Size(2560, 1400));
        view.UpdateLayout();
        Assert.That(panel.ItemHeight, Is.GreaterThan(height));
    }

    private static HomePageVM CreateVM() => new(new StubHomeFolderService(), new StubAppSettingsService(),
        new StubInstallationDialogService(), new StubProjectCreationDialogService(), new ImmediateUiScheduler(),
        new StubProjectFileService((_, _, _) => Task.FromResult("saved.json")), new WeakReferenceMessenger());

    [Test]
    public void ShellUsesMonitorBeforeMaximizeAndReleasesHomeMinimumOnNavigation()
    {
        var vm = CreateVM();
        var page = new HomePage(vm);
        var monitorSize = new Size(1920, 1080);
        var window = new MainWindow(page, new WeakReferenceMessenger(), WorkstationTestPage.Create, vm,
            new StubAppSettingsService(), error => Assert.Fail(error), _ => { }, _ => monitorSize);
        using var view = new InMemoryView(window);
        view.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        view.UpdateLayout();
        var height = Descendants<SpacedUniformGrid>(page).Single().ItemHeight;
        monitorSize = new Size(2560, 1440);
        window.Width = 1400;
        view.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        view.UpdateLayout();
        Assert.That(Descendants<SpacedUniformGrid>(page).Single().ItemHeight, Is.GreaterThan(height));
        window.Receive(new ProjectCreatedMessage("saved.json"));
        Assert.That(((ContentControl)window.FindName("PageHost")).Content, Is.TypeOf<WorkstationPage>());
        window.Close();
    }

    [Test]
    public void ActiveShellRoutesModifiedWheelToCurrentWorkstationFromAnywhere()
    {
        var homeViewModel = CreateVM();
        var workstationViewModel = new WorkstationPageVM(new StubWorkstationSourceService(), new Hitbench.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        var workstationPage = new WorkstationPage(workstationViewModel);
        var window = TestShell.Create(new HomePage(homeViewModel), new WeakReferenceMessenger(),
            () => workstationPage, homeViewModel, new StubAppSettingsService());
        using var view = new InMemoryView(window);
        try
        {
            Assert.That(window.HandleWindowMouseWheel(true, ModifierKeys.Control, 120), Is.False);

            window.Receive(new ProjectCreatedMessage("saved.json"));
            view.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
            UiTestPump.Run(workstationPage.LoadCompletion);

            Assert.Multiple(() =>
            {
                Assert.That(window.HandleWindowMouseWheel(false, ModifierKeys.Control, 120), Is.False);
                Assert.That(workstationViewModel.ZoomFactor, Is.EqualTo(32));
                Assert.That(window.HandleWindowMouseWheel(true, ModifierKeys.Control, 120), Is.True);
                Assert.That(workstationViewModel.ZoomFactor, Is.EqualTo(33));
                Assert.That(window.HandleWindowMouseWheel(true, ModifierKeys.Alt, 120, workstationPage), Is.True);
                Assert.That(workstationViewModel.ApplicationVolume, Is.EqualTo(100));
                Assert.That(window.HandleWindowMouseWheel(true, ModifierKeys.Shift, 120), Is.True);
                Assert.That(window.HandleWindowMouseWheel(true, ModifierKeys.None, 120), Is.True);
                Assert.That(workstationViewModel.ZoomFactor, Is.EqualTo(33));
            });
            var detachedEditor = new TextBox();
            using var detachedSource = new InMemoryPresentationSource { RootVisual = detachedEditor };
            var key = new KeyEventArgs(Keyboard.PrimaryDevice, detachedSource, 0, Key.Space)
            { RoutedEvent = Keyboard.KeyDownEvent, Source = detachedEditor };
            Assert.That(window.HandleWorkstationInput(false, key, ModifierKeys.None), Is.False);
            Assert.That(window.HandleWorkstationInput(true, key, ModifierKeys.None), Is.True);
            UiTestPump.Run(workstationPage.PlaybackInputCompletion);
            Assert.That(workstationViewModel.IsPlaying, Is.False);
            workstationPage.DismissVolumeOverlay();
            Assert.That(window.HandleWorkstationInput(true, key, ModifierKeys.None), Is.False);
            UiTestPump.Run(workstationPage.PlaybackInputCompletion);
            key.Handled = true;
            Assert.That(window.HandleWorkstationInput(true, key, ModifierKeys.None), Is.False);
            Assert.That(workstationViewModel.IsPlaying, Is.False);
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void ActiveShellRoutesSpaceOnlyToCurrentWorkstation()
    {
        var homeViewModel = CreateVM();
        var workstationViewModel = new WorkstationPageVM(new StubWorkstationSourceService(), new Hitbench.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        var workstationPage = new WorkstationPage(workstationViewModel);
        var window = TestShell.Create(new HomePage(homeViewModel), new WeakReferenceMessenger(),
            () => workstationPage, homeViewModel, new StubAppSettingsService());
        using var view = new InMemoryView(window);
        try
        {
            Assert.That(window.HandleWindowKeyDown(true, Key.Space, ModifierKeys.None, false, window), Is.False);
            window.Receive(new ProjectCreatedMessage("saved.json"));
            view.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
            UiTestPump.Run(workstationPage.LoadCompletion);

            Assert.That(window.HandleWindowKeyDown(false, Key.Space, ModifierKeys.None, false, window), Is.False);
            Assert.That(window.HandleWindowKeyDown(true, Key.Space, ModifierKeys.None, false, window), Is.True);
            Assert.That(workstationViewModel.IsPlaying, Is.True);
            Assert.That(window.HandleWindowKeyDown(true, Key.Space, ModifierKeys.None, true, window), Is.True);
            Assert.That(workstationViewModel.IsPlaying, Is.True);
        }
        finally
        {
            window.Close();
        }
    }

    [TestCase(1366, 728)]
    [TestCase(1536, 824)]
    [TestCase(1920, 1031.333333)]
    public void FractionalViewportAndLongStatusKeepSongsContract(double width, double height)
    {
        var vm = CreateVM();
        var page = new HomePage(vm);
        var window = new Window { Content = page, Width = width, Height = height };
        using var view = new InMemoryView(window);
        page.UpdateSongsLayout(new Size(width, height));
        view.UpdateLayout();
        var panel = Descendants<SpacedUniformGrid>(page).Single();
        window.Width = page.MinWidth;
        view.UpdateLayout();
        vm.SongsLoadStatus = "Unable to read songs. Please check the installation directory and retry loading songs.";
        view.UpdateLayout();
        window.Height = page.MinHeight;
        view.UpdateLayout();
        Assert.Multiple(() =>
        {
            UiControlContract.AssertStyle<ItemsControl>((ItemsControl)page.FindName("SongsList"), "BaseItemsControlStyle");
            Assert.That(panel.VerticalOffset, Is.InRange(0, Math.Max(0, panel.ExtentHeight - panel.ViewportHeight)));
        });
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
