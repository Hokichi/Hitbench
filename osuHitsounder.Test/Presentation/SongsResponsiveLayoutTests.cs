using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.ViewModels.Shell.HomePage;
using osuHitsounder.ViewModels.Shell;
using osuHitsounder.Views.Pages;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.Messages;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class SongsResponsiveLayoutTests
{
    [SetUp]
    public void SetUp()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Clear();
        UiTestPump.EnsureFontResources(app);
        foreach (var resource in new[] { "Themes/Dark.xaml", "Converters/Converters.xaml", "Icons.xaml", "Styles/TextBlock.xaml", "Styles/ItemsControl.xaml", "Styles/Image.xaml", "Styles/Grid.xaml", "Styles/Border.xaml", "Styles/ContentPresenter.xaml", "Styles/Button.xaml", "Styles/ToolTip.xaml", "Styles/Window.xaml", "Styles/ScrollViewer.xaml", "Styles/TextBox.xaml", "Styles/ComboBox.xaml", "Styles/ListBox.xaml", "Styles/Slider.xaml", "BeatGrid.xaml" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            { Source = new Uri($"/osuHitsounder;component/Resources/{resource}", UriKind.Relative) });
    }

    [TestCase(FlowDirection.LeftToRight)]
    [TestCase(FlowDirection.RightToLeft)]
    public void MaximizedThenMinimumRetainsCompleteRowsAndStretchesCards(FlowDirection direction)
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
        var baselineWidth = panel.ItemWidth;
        var baselineHeight = panel.ItemHeight;
        Assert.Multiple(() =>
        {
            Assert.That(panel.Columns, Is.EqualTo(5));
            Assert.That(panel.VisibleRowCount, Is.EqualTo(8));
            Assert.That(panel.ExtentWidth, Is.EqualTo(panel.ViewportWidth).Within(.01));
            Assert.That(panel.Children.Count, Is.InRange(40, 50));
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
            Assert.That(panel.Columns, Is.EqualTo(1));
            Assert.That(panel.VisibleRowCount, Is.EqualTo(3));
            Assert.That(panel.ItemWidth, Is.EqualTo(.75 * baselineWidth).Within(.01));
            Assert.That(panel.ItemHeight, Is.EqualTo(baselineHeight).Within(.01));
            Assert.That(button.ActualWidth, Is.EqualTo(panel.ItemWidth).Within(.01));
            Assert.That(button.ActualHeight, Is.EqualTo(panel.ItemHeight).Within(.01));
            Assert.That(panel.Children.Count, Is.InRange(3, 6));
            Assert.That(panel.ExtentWidth, Is.EqualTo(panel.ViewportWidth).Within(.01));
        });
        panel.SetVerticalOffset(double.MaxValue);
        view.UpdateLayout();
        Assert.That(Descendants<Button>(panel).Last().DataContext, Is.SameAs(vm.Songs.Last()));
        window.Width = 1920;
        window.Height = 1040;
        view.UpdateLayout();
        Assert.That(panel.Columns, Is.EqualTo(5));
        Assert.That(panel.VerticalOffset, Is.LessThanOrEqualTo(panel.ExtentHeight - panel.ViewportHeight));
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
        Assert.That(page.MinHeight, Is.GreaterThan(3 * panel.ItemHeight));
        Assert.That(page.MinWidth, Is.GreaterThan(300 + panel.ItemWidth * .5));
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
        Assert.That(window.MinWidth, Is.EqualTo(page.MinWidth + 2).Within(.01));
        Assert.That(window.MinHeight, Is.EqualTo(page.MinHeight + 42).Within(.01));
        var height = Descendants<SpacedUniformGrid>(page).Single().ItemHeight;
        monitorSize = new Size(2560, 1440);
        window.Width = 1400;
        view.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        view.UpdateLayout();
        Assert.That(Descendants<SpacedUniformGrid>(page).Single().ItemHeight, Is.GreaterThan(height));
        window.Receive(new ProjectCreatedMessage("saved.json"));
        Assert.That(window.MinWidth, Is.EqualTo(640));
        Assert.That(window.MinHeight, Is.EqualTo(360));
        window.Close();
    }

    [Test]
    public void ActiveShellRoutesModifiedWheelToCurrentWorkstationFromAnywhere()
    {
        var homeViewModel = CreateVM();
        var workstationViewModel = new WorkstationPageVM(new StubWorkstationSourceService(),
            new StubWorkstationAudioService(), new ImmediateUiScheduler());
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
                Assert.That(workstationViewModel.ZoomFactor, Is.EqualTo(1));
                Assert.That(window.HandleWindowMouseWheel(true, ModifierKeys.Control, 120), Is.True);
                Assert.That(workstationViewModel.ZoomFactor, Is.EqualTo(2));
                Assert.That(window.HandleWindowMouseWheel(true, ModifierKeys.Alt, 120), Is.True);
                Assert.That(workstationViewModel.SelectedSubdivisionIndex, Is.EqualTo(4));
                Assert.That(window.HandleWindowMouseWheel(true, ModifierKeys.Shift, 120), Is.True);
                Assert.That(window.HandleWindowMouseWheel(true, ModifierKeys.None, 120), Is.False);
            });
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
        var workstationViewModel = new WorkstationPageVM(new StubWorkstationSourceService(),
            new StubWorkstationAudioService(), new ImmediateUiScheduler());
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
    public void FractionalRowsAndWrappedStatusRetainThreeRowsAtMinimum(double width, double height)
    {
        var vm = CreateVM();
        var page = new HomePage(vm);
        var window = new Window { Content = page, Width = width, Height = height };
        using var view = new InMemoryView(window);
        page.UpdateSongsLayout(new Size(width, height));
        view.UpdateLayout();
        var panel = Descendants<SpacedUniformGrid>(page).Single();
        Assert.That(panel.VisibleRowCount, Is.EqualTo(8));
        var baselineHeight = panel.ItemHeight;
        window.Width = page.MinWidth;
        view.UpdateLayout();
        vm.SongsLoadStatus = "Unable to read songs. Please check the installation directory and retry loading songs.";
        view.UpdateLayout();
        window.Height = page.MinHeight;
        view.UpdateLayout();
        Assert.Multiple(() =>
        {
            Assert.That(panel.ItemHeight, Is.EqualTo(baselineHeight));
            Assert.That(panel.VisibleRowCount, Is.EqualTo(3));
            Assert.That(panel.ViewportHeight, Is.EqualTo(3 * baselineHeight + 24).Within(.01));
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
