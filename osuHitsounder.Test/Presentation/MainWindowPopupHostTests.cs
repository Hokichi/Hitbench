using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Views.Pages;
using osuHitsounder.ViewModels.Shell.HomePage;
using osuHitsounder.Resources.Messages;
using System.Windows.Threading;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public sealed class MainWindowPopupHostTests
{
    [SetUp]
    public void Setup() => new SongsResponsiveLayoutTests().SetUp();

    private static MainWindow Create()
    {
        var settings = new StubAppSettingsService();
        var messenger = new WeakReferenceMessenger();
        HomePage CreateHome() => new(new HomePageVM(new StubHomeFolderService(), settings, new StubInstallationDialogService(),
            new StubProjectCreationDialogService(), new ImmediateUiScheduler(),
            new StubProjectFileService((_, _, _) => Task.FromResult("saved.json")), messenger));
        var page = CreateHome();
        return TestShell.Create(page, messenger, WorkstationTestPage.Create, (HomePageVM)page.DataContext, settings, createHome: CreateHome);
    }

    [Test]
    public void HeaderBindingTracksVolumeAndNavigation()
    {
        var window = Create();
        window.Receive(new ProjectOpenRequestedMessage("project.json"));
        var page = (WorkstationPage)((ContentControl)window.FindName("PageHost")).Content;
        using var view = new InMemoryView(window);
        try
        {
            UiTestPump.Run(page.LoadCompletion);
            var host = (Border)window.FindName("HeaderVolumeBlurHost");
            Assert.That(host, Is.Not.Null);
            page.ShowVolumeOverlay(false, new Point(), false, false);
            UiTestPump.Run(window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task);
            Assert.That(host.Effect, Is.Not.Null.And.SameAs(page.HeaderBlurEffect));
            UiTestPump.Run(window.ReturnHomeAsync());
            Assert.That(((ContentControl)window.FindName("PageHost")).Content, Is.TypeOf<HomePage>());
            Assert.That(host.Effect, Is.Null);
            Assert.That(System.Windows.Data.BindingOperations.IsDataBound(host, UIElement.EffectProperty), Is.False);
        }
        finally { window.Close(); }
    }

    [Test]
    public void ClosingOneOverlayPreservesOtherHeaderBlur()
    {
        var window = Create();
        try
        {
            window.ApplyTemplate();
            var vm = (HomePageVM)window.DataContext;
            vm.IsProcessing = true;
            UiTestPump.Layout((FrameworkElement)window.Content, 1280, 720);
            var header = (Border)window.FindName("HeaderScanBlurHost");
            Assert.That(header, Is.Not.Null);
            Assert.That(header.Effect, Is.Not.Null);
            var scanEffect = header.Effect;
            window.ShowPopupOverlay();
            UiTestPump.Layout((FrameworkElement)window.Content, 1280, 720);
            window.HidePopupOverlay();
            UiTestPump.Layout((FrameworkElement)window.Content, 1280, 720);
            Assert.That(header.Effect, Is.SameAs(scanEffect));
            vm.IsProcessing = false;
            Assert.That(header.Effect, Is.Null);
        }
        finally { window.Close(); }
    }

    [Test]
    public void PopupHostBlocksShellAndRestoresExistingScanState()
    {
        var window = Create();
        var vm = (HomePageVM)window.DataContext;
        vm.IsProcessing = true;
        vm.IsInteractionBlocked = true;
        var content = (Border)window.FindName("ModalShellContent");
        Assert.That(content.Style, Is.SameAs(window.FindResource("MainWindowModalContentStyle")));
        window.ApplyTemplate();
        UiTestPump.Run(window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle).Task);
        UiTestPump.Layout((FrameworkElement)window.Content, 1280, 720);
        window.ShowPopupOverlay();
        UiTestPump.Layout((FrameworkElement)window.Content, 1280, 720);
        Assert.That(content.IsEnabled, Is.False);
        window.HidePopupOverlay();
        UiTestPump.Layout((FrameworkElement)window.Content, 1280, 720);
        Assert.That(content.IsEnabled, Is.True);
        var pageHost = (ContentControl)window.FindName("PageHost");
        Assert.That(pageHost.IsEnabled, Is.False);
        window.Close();
    }

    [Test]
    public void NestedPopupsKeepOverlayUntilLastClose()
    {
        var window = Create();
        window.ShowPopupOverlay();
        window.ShowPopupOverlay();
        window.HidePopupOverlay();
        Assert.That(window.IsPopupOverlayVisible, Is.True);
        window.HidePopupOverlay();
        Assert.That(window.IsPopupOverlayVisible, Is.False);
        window.Close();
    }

    [Test]
    public void HandoffDoesNotClearOverlayUnderNextPopup()
    {
        var window = Create();
        window.ShowPopupOverlay();
        window.BeginPopupHandoff();
        window.HidePopupOverlay();
        Assert.That(window.IsPopupOverlayVisible, Is.True);
        window.ShowPopupOverlay();
        window.HidePopupOverlay();
        Assert.That(window.IsPopupOverlayVisible, Is.False);
        window.Close();
    }

    [Test]
    public void ModalResourcesReuseGlobalFocusContract()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var app = Application.Current!;
        var focus = (Style)app.FindResource("DefaultFocusVisualStyle");
        Assert.That(focus, Is.SameAs(app.FindResource("DefaultFocusVisualStyle")));
        var cue = new Control { Style = focus };
        UiControlContract.AssertStyle<Control>(cue, "DefaultFocusVisualStyle");
        var close = new osuHitsounder.Resources.CustomControls.BalloonButton
        { Style = (Style)app.FindResource("OHsPopupCloseActionStyle") };
        UiControlContract.AssertStyle<osuHitsounder.Resources.CustomControls.BalloonButton>(close, "OHsPopupCloseActionStyle");
        Assert.That(close.FocusVisualStyle, Is.SameAs(focus));
    }
}
