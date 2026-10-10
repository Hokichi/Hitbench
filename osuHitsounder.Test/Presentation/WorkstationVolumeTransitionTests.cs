using System.Windows;
using System.Windows.Controls;
using osuHitsounder.Services;
using osuHitsounder.ViewModels.Shell;
using osuHitsounder.Views.Pages;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class WorkstationVolumeTransitionTests
{
    [SetUp]
    public void Setup() => new VolumeWheelTests().SetUp();

    [Test]
    public void ContentStaysLockedThroughoutBothFadesAndUnlocksAfterDismissal()
    {
        var page = CreatePage();
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);
        var content = (Grid)page.FindName("WorkstationContent");
        UiControlContract.AssertStyle<Grid>(content, "OHsWorkstationContentStyle");
        page.ShowVolumeOverlay(false, new Point(), true, false);
        AssertLocked();
        UiTestPump.Run(Task.Delay(100));
        AssertLocked();
        UiTestPump.Run(Task.Delay(150));
        AssertLocked();
        page.ProcessVolumeOverlayIdle(TimeSpan.FromMilliseconds(500), true);
        UiTestPump.Run(Task.Delay(100));
        AssertLocked();
        page.RecordVolumeActivity();
        AssertLocked();
        UiTestPump.Run(Task.Delay(250));
        AssertLocked();
        page.ProcessVolumeOverlayIdle(TimeSpan.FromMilliseconds(500), true);
        UiTestPump.Run(Task.Delay(250));
        Assert.That(content.IsEnabled, Is.True);
        Assert.That(content.IsHitTestVisible, Is.True);

        void AssertLocked()
        {
            Assert.That(content.IsEnabled, Is.False);
            Assert.That(content.IsHitTestVisible, Is.False);
            Assert.That(page.ApplicationVolumeWheel.IsEnabled, Is.True);
            Assert.That(page.ApplicationVolumeWheel.IsHitTestVisible, Is.True);
        }
    }

    [Test]
    public void DismissalPreservesInheritedDisabledState()
    {
        var page = CreatePage();
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);
        var root = (Grid)page.FindName("WorkstationRoot");
        var content = (Grid)page.FindName("WorkstationContent");
        UiControlContract.AssertStyle<Grid>(content, "OHsWorkstationContentStyle");
        page.ShowVolumeOverlay(false, new Point(), false, false);
        Assert.That(content.IsHitTestVisible, Is.False);
        root.IsEnabled = false;
        page.DismissVolumeOverlay();
        Assert.That(content.IsEnabled, Is.False);
        Assert.That(content.IsHitTestVisible, Is.True);
        root.ClearValue(UIElement.IsEnabledProperty);
        Assert.That(content.IsEnabled, Is.True);
    }

    [Test]
    public void EntryAndExitPreserveInputGateAndActivity()
    {
        var page = CreatePage();
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);
        page.ShowVolumeOverlay(false, new Point(), true, false);
        var panel = (Border)page.FindName("VolumeOverlay");
        UiControlContract.AssertStyle<Border>(panel, "OHsWorkstationVolumeOverlayStyle");
        var content = (Grid)page.FindName("WorkstationContent");
        UiControlContract.AssertStyle<Grid>(content, "OHsWorkstationContentStyle");
        page.RecordVolumeActivity();
        UiTestPump.Run(Task.Delay(100));
        UiTestPump.Run(Task.Delay(150));
        page.ProcessVolumeOverlayIdle(TimeSpan.FromMilliseconds(500), true);
        UiTestPump.Run(Task.Delay(100));
        Assert.That(page.IsVolumeOverlayOpen, Is.True);
        UiTestPump.Run(Task.Delay(150));
        Assert.That(page.IsVolumeOverlayOpen, Is.False);
        Assert.That(content.IsHitTestVisible, Is.True);
    }

    [Test]
    public void ActivityDuringExitReopensWithoutStaleDismissal()
    {
        var page = CreatePage();
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);
        page.ShowVolumeOverlay(false, new Point(), true, false);
        UiTestPump.Run(Task.Delay(250));
        page.ProcessVolumeOverlayIdle(TimeSpan.FromMilliseconds(500), true);
        UiTestPump.Run(Task.Delay(100));
        var panel = (Border)page.FindName("VolumeOverlay");
        UiControlContract.AssertStyle<Border>(panel, "OHsWorkstationVolumeOverlayStyle");
        page.RecordVolumeActivity();
        Assert.That(page.IsVolumeOverlayOpen, Is.True);
        UiTestPump.Run(Task.Delay(250));
        Assert.That(page.IsVolumeOverlayOpen, Is.True);
        page.DismissVolumeOverlay();
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DisabledAnimationAppliesTerminalInteractionStates(bool highContrast)
    {
        var page = CreatePage();
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);
        page.ShowVolumeOverlay(false, new Point(), false, highContrast);
        var panel = (Border)page.FindName("VolumeOverlay");
        UiControlContract.AssertStyle<Border>(panel, "OHsWorkstationVolumeOverlayStyle");
        var content = (Grid)page.FindName("WorkstationContent");
        UiControlContract.AssertStyle<Grid>(content, "OHsWorkstationContentStyle");
        Assert.That(panel.HasAnimatedProperties, Is.False);
        page.ProcessVolumeOverlayIdle(TimeSpan.FromMilliseconds(499), false);
        Assert.That(page.IsVolumeOverlayOpen, Is.True);
        page.ProcessVolumeOverlayIdle(TimeSpan.FromMilliseconds(500), false);
        Assert.That(page.IsVolumeOverlayOpen, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UnloadAndDeactivationClearTransitions(bool unload)
    {
        var page = CreatePage();
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);
        page.ShowVolumeOverlay(false, new Point(), true, false);
        if (unload) view.Dispose();
        else UiTestPump.Run(page.DeactivateAsync());
        var panel = (Border)page.FindName("VolumeOverlay");
        UiControlContract.AssertStyle<Border>(panel, "OHsWorkstationVolumeOverlayStyle");
        Assert.That(panel.HasAnimatedProperties, Is.False);
        UiTestPump.Run(Task.Delay(250));
        Assert.That(page.IsVolumeOverlayOpen, Is.False);
        Assert.That(page.ActiveVolumeWheel, Is.Null);
        Assert.That(((Grid)page.FindName("WorkstationContent")).IsHitTestVisible, Is.True);
    }

    [Test]
    public void ReopeningWithChangedSystemSettingsSettlesMotion()
    {
        var page = CreatePage();
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);
        page.ShowVolumeOverlay(false, new Point(), true, false);
        var panel = (Border)page.FindName("VolumeOverlay");
        UiControlContract.AssertStyle<Border>(panel, "OHsWorkstationVolumeOverlayStyle");
        var content = (Grid)page.FindName("WorkstationContent");
        UiControlContract.AssertStyle<Grid>(content, "OHsWorkstationContentStyle");
        page.ShowVolumeOverlay(false, new Point(), false, true);
        Assert.That(panel.HasAnimatedProperties, Is.False);
        page.ShowVolumeOverlay(false, new Point(), false, false);
        Assert.That(page.IsVolumeOverlayOpen, Is.True);
        Assert.That(content.IsEnabled, Is.False);
    }

    [Test]
    public void ReopeningDuringExitAndEscapeClearOldCompletion()
    {
        var page = CreatePage();
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);
        page.ShowVolumeOverlay(false, new Point(), false, false);
        page.ProcessVolumeOverlayIdle(TimeSpan.FromMilliseconds(500), true);
        UiTestPump.Run(Task.Delay(100));
        var panel = (Border)page.FindName("VolumeOverlay");
        UiControlContract.AssertStyle<Border>(panel, "OHsWorkstationVolumeOverlayStyle");
        page.ShowVolumeOverlay(false, new Point(), true, false);
        UiTestPump.Run(Task.Delay(250));
        Assert.That(page.IsVolumeOverlayOpen, Is.True);
        page.HandleVolumeOverlayKey(System.Windows.Input.Key.Escape, System.Windows.Input.ModifierKeys.None);
        Assert.That(page.IsVolumeOverlayOpen, Is.False);
        Assert.That(((Grid)page.FindName("WorkstationContent")).IsHitTestVisible, Is.True);
    }

    private static WorkstationPage CreatePage()
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(),
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        return page;
    }
}
