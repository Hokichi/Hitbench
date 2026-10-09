using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Effects;
using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Views.Pages;
using osuHitsounder.ViewModels.Shell.HomePage;

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
        var vm = new HomePageVM(new StubHomeFolderService(), settings, new StubInstallationDialogService(),
            new StubProjectCreationDialogService(), new ImmediateUiScheduler(),
            new StubProjectFileService((_, _, _) => Task.FromResult("saved.json")), messenger);
        return TestShell.Create(new HomePage(vm), messenger, WorkstationTestPage.Create, vm, settings);
    }

    [Test]
    public void PopupHostBlursWholeShellAndRestoresExistingScanState()
    {
        var window = Create();
        var vm = (HomePageVM)window.DataContext;
        vm.IsProcessing = true;
        vm.IsInteractionBlocked = true;
        var content = (Border)window.FindName("ModalShellContent");
        UiTestPump.Layout((FrameworkElement)window.Content, 1280, 720);
        window.ShowPopupOverlay();
        UiTestPump.Layout((FrameworkElement)window.Content, 1280, 720);
        Assert.That(content.Effect, Is.TypeOf<BlurEffect>());
        Assert.That(((BlurEffect)content.Effect).Radius, Is.EqualTo(8));
        Assert.That(content.IsEnabled, Is.False);
        window.HidePopupOverlay();
        UiTestPump.Layout((FrameworkElement)window.Content, 1280, 720);
        Assert.That(content.Effect, Is.Null);
        Assert.That(content.IsEnabled, Is.True);
        var pageHost = (ContentControl)window.FindName("PageHost");
        Assert.That(((Border)pageHost.Parent).Effect, Is.TypeOf<BlurEffect>());
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
    public void ModalResourcesProvideAccessibleFocusAndHighContrastContracts()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var app = Application.Current!;
        var focus = (Style)app.FindResource("OHsKeyboardFocusVisualStyle");
        Assert.That(focus.BasedOn, Is.SameAs(app.FindResource("BaseControlStyle")));
        var cue = new Control { Style = focus };
        Assert.That(cue.ApplyTemplate(), Is.True);
        Assert.That(cue.Template, Is.Not.Null);
        var close = new osuHitsounder.Resources.CustomControls.BalloonButton
        { Style = (Style)app.FindResource("OHsPopupCloseActionStyle") };
        Assert.That(close.ShouldShowText, Is.True);
        Assert.That(close.FocusVisualStyle, Is.SameAs(focus));
    }
}
