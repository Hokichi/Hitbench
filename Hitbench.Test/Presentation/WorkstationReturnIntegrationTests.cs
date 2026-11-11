using Hitbench.Test;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using Hitbench.Resources.Helpers;
using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Core.Entities;
using Hitbench.Resources.CustomControls;
using Hitbench.Resources.Messages;
using Hitbench.Services;
using Hitbench.Services.ProjectFiles;
using Hitbench.ViewModels.Shell;
using Hitbench.ViewModels.Shell.HomePage;
using Hitbench.Views.Pages;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class WorkstationReturnIntegrationTests
{
    private MainWindow window = null!;
    private InMemoryView view = null!;
    private WeakReferenceMessenger messenger = null!;
    private StubWorkstationAudioService audio = null!;
    private WorkstationPageVM vm = null!;
    private WorkstationPage workstation = null!;
    private readonly List<HomePage> homes = [];
    private IProjectFileService saves = null!;

    [SetUp]
    public void SetUp()
    {
        new WorkstationPageIntegrationTests().SetUp();
        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/Hitbench;component/Resources/Styles/Image.xaml", UriKind.Relative)
        });
        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/Hitbench;component/Resources/Styles/Window.xaml", UriKind.Relative)
        });
        messenger = new WeakReferenceMessenger();
        homes.Clear();
    }

    private HomePage CreateHome()
    {
        var home = new HomePage(new HomePageVM(new StubHomeFolderService(), new StubAppSettingsService(),
            new StubInstallationDialogService(), new ImmediateUiScheduler(),
            new StubProjectFileService((_, _, _) => Task.FromResult("project.json")), messenger));
        homes.Add(home);
        return home;
    }

    private void Open(IProjectFileService projectFiles)
    {
        saves = projectFiles;
        var first = CreateHome();
        window = new MainWindow(first, messenger, () =>
        {
            audio = new StubWorkstationAudioService();
            vm = new WorkstationPageVM(new StubWorkstationSourceService
            {
                Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song",
                    new ProjectData { UninheritedPoints = TimingTestData.Points([0], [120]), Tracks = [new Track()] }))
            }, new MarkerPlaybackCoordinator(audio, MarkerPlaybackCoordinator.DelayAsync), new ImmediateUiScheduler(),
                new StubSampleFileService(["hit.wav"]), projectFiles: saves, messenger: messenger);
            return workstation = new WorkstationPage(vm);
        }, (HomePageVM)first.DataContext, new StubAppSettingsService(),
            error => throw new AssertionException(error), _ => throw new AssertionException("Native window state requested"),
            _ => new Size(1920, 1080), CreateHome);
        view = new InMemoryView(window);
        UiTestPump.Run(window.LoadAsync());
        messenger.Send(new ProjectOpenRequestedMessage("project.json"));
        view.UpdateLayout();
        UiTestPump.Run(workstation.LoadCompletion);
        view.UpdateLayout();
    }

    [TearDown]
    public void TearDown()
    {
        if (workstation is not null) UiTestPump.Run(workstation.DeactivateAsync());
        view?.Dispose();
        window?.Close();
    }

    [Test]
    public void ProjectIdentityFollowsActiveWorkstationAndClearsOnHome()
    {
        var immediate = new DelayedProjectFileService();
        immediate.Release();
        Open(immediate);
        var header = (Hitbench.Views.Shell.OHsShellHeader)window.FindName("ShellHeader");
        Assert.That(header.ProjectName, Is.EqualTo(vm.ProjectName).And.Not.Empty);
        UiTestPump.Run(window.ReturnHomeAsync());
        Assert.That(header.ProjectName, Is.Empty);
        messenger.Send(new ProjectOpenRequestedMessage(@"C:\Projects\Second\project.json"));
        view.UpdateLayout();
        UiTestPump.Run(workstation.LoadCompletion);
        view.UpdateLayout();
        Assert.That(header.ProjectName, Is.EqualTo("Second"));
        Assert.That(System.Windows.Data.BindingOperations.GetBinding(header, Hitbench.Views.Shell.OHsShellHeader.ProjectNameProperty)!.Source, Is.SameAs(vm));
    }

    [Test]
    public void WorkstationScopeSuppressesShellAndRestoresAfterReturn()
    {
        var immediate = new DelayedProjectFileService();
        immediate.Release();
        Open(immediate);
        var header = (Hitbench.Views.Shell.OHsShellHeader)window.FindName("ShellHeader");
        var close = (BalloonButton)header.FindName("CloseButton");
        var probe = new TextBox { IsTabStop = true };
        KeyboardNavigationPolicy.SetAllowTabNavigation(probe, true);
        Assert.That(probe.IsTabStop, Is.True, "Independent scope retains its opt-in.");
        Assert.That(close.IsTabStop, Is.False);
        Assert.That(KeyboardNavigationPolicy.GetSuppressTabNavigation(window), Is.True);
        UiTestPump.Run(window.ReturnHomeAsync());
        view.UpdateLayout();
        Assert.That(KeyboardNavigationPolicy.GetSuppressTabNavigation(window), Is.False);
        Assert.That(KeyboardNavigationPolicy.GetSuppressTabNavigation(header), Is.False);
        Assert.That(probe.IsTabStop, Is.True);
        KeyboardNavigationPolicy.SetAllowTabNavigation(close, true);
        close.IsTabStop = true;
        Assert.That(close.IsTabStop, Is.True);
    }

    [Test]
    public void ReturnWaitBlocksEditingAndLeavesOnlyAfterSaved()
    {
        var delayed = new DelayedProjectFileService();
        Open(delayed);
        var returning = window.ReturnHomeAsync();
        try
        {
            UiTestPump.Run(StubWorkstationAudioService.WaitUntilAsync(() => vm.IsSavingForReturn));
            view.UpdateLayout();
            Assert.That(returning.IsCompleted, Is.False);
            Assert.That(((ContentControl)window.FindName("PageHost")).Content, Is.SameAs(workstation));
            Assert.That(((Border)workstation.FindName("LoadingOverlay")).Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(audio.Disposed, Is.False);
            Assert.That(window.ReturnHomeAsync(), Is.SameAs(returning));
        }
        finally { delayed.Release(); UiTestPump.Run(returning); }
        Assert.That(((ContentControl)window.FindName("PageHost")).Content, Is.SameAs(homes[1]));
        Assert.That(audio.Disposed, Is.True);
        Assert.That(((FrameworkElement)((Hitbench.Views.Shell.OHsShellHeader)window.FindName("ShellHeader")).FindName("WorkstationActions")).Visibility, Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void FailedSaveClearsFeedbackAndAllowsHeaderRetry()
    {
        Open(new FailOnceProjectFileService());
        UiTestPump.Run(window.ReturnHomeAsync());
        view.UpdateLayout();
        Assert.That(((ContentControl)window.FindName("PageHost")).Content, Is.SameAs(workstation));
        Assert.That(((Border)workstation.FindName("LoadingOverlay")).Visibility, Is.EqualTo(Visibility.Collapsed));
        Assert.That(vm.SaveError, Does.Contain("Disk full"));
        Assert.That(vm.CanEditTracks, Is.True);
        Assert.That(KeyboardNavigationPolicy.GetSuppressTabNavigation(window), Is.True);
        var back = (BalloonButton)((Hitbench.Views.Shell.OHsShellHeader)window.FindName("ShellHeader")).FindName("GoBackButton");
        Assert.That(back, Is.Not.Null);
        Assert.That(back.IsEnabled, Is.True);
        Assert.That(AutomationProperties.GetName(back), Is.EqualTo("Return to projects"));
        back.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        UiTestPump.Run(StubWorkstationAudioService.WaitUntilAsync(() => homes.Count == 2));
        UiTestPump.Run(window.ReturnHomeAsync());
        Assert.That(((ContentControl)window.FindName("PageHost")).Content, Is.SameAs(homes[1]));
    }

    [Test]
    public void ClosingJoinsReturnSaveWithoutNavigatingHome()
    {
        var delayed = new DelayedProjectFileService();
        Open(delayed);
        var returning = window.ReturnHomeAsync();
        UiTestPump.Run(StubWorkstationAudioService.WaitUntilAsync(() => vm.IsSavingForReturn));
        var closing = window.PrepareProjectForCloseAsync();
        Assert.That(closing.IsCompleted, Is.False);
        delayed.Release();
        UiTestPump.Run(Task.WhenAll(returning, closing));
        Assert.That(closing.Result, Is.True);
        Assert.That(homes, Has.Count.EqualTo(1));
        Assert.That(audio.Disposed, Is.True);
        Assert.That(((ContentControl)window.FindName("PageHost")).Content, Is.SameAs(workstation));
    }

    [TestCase(640, FlowDirection.LeftToRight)]
    [TestCase(640, FlowDirection.RightToLeft)]
    [TestCase(1280, FlowDirection.LeftToRight)]
    public void HeaderActionsFitWithoutClipping(int width, FlowDirection direction)
    {
        var immediate = new DelayedProjectFileService();
        immediate.Release();
        Open(immediate);
        window.Width = width;
        window.FlowDirection = direction;
        view.UpdateLayout();
        var back = (BalloonButton)((Hitbench.Views.Shell.OHsShellHeader)window.FindName("ShellHeader")).FindName("GoBackButton");
        var close = (BalloonButton)((Hitbench.Views.Shell.OHsShellHeader)window.FindName("ShellHeader")).FindName("CloseButton");
        foreach (var button in new[] { back, close })
        {
            var bounds = button.TransformToAncestor(view.Root).TransformBounds(new Rect(button.RenderSize));
            Assert.That(bounds.Left, Is.GreaterThanOrEqualTo(0));
            Assert.That(bounds.Right, Is.LessThanOrEqualTo(width));
        }
    }

    [Test]
    public void RepeatedNavigationCreatesUsableHomeLifetimes()
    {
        var immediate = new DelayedProjectFileService();
        immediate.Release();
        Open(immediate);
        for (var cycle = 0; cycle < 2; cycle++)
        {
            UiTestPump.Run(window.ReturnHomeAsync());
            view.UpdateLayout();
            var home = (HomePage)((ContentControl)window.FindName("PageHost")).Content;
            Assert.That(home, Is.SameAs(homes[cycle + 1]));
            Assert.That(((HomePageVM)home.DataContext).CanInteract, Is.True);
            if (cycle == 0)
            {
                messenger.Send(new ProjectOpenRequestedMessage("another.json"));
                view.UpdateLayout();
                UiTestPump.Run(workstation.LoadCompletion);
            }
        }
        Assert.That(homes.Distinct().Count(), Is.EqualTo(3));
    }
}
