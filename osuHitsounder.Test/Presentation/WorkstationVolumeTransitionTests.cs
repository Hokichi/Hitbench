using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Effects;
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
        page.ShowVolumeOverlay(false, new Point(), true, false);
        AssertLocked();
        UiTestPump.Run(Task.Delay(100));
        AssertLocked();
        UiTestPump.Run(Task.Delay(150));
        AssertLocked();
        page.ProcessVolumeOverlayIdle(TimeSpan.FromSeconds(1), true);
        UiTestPump.Run(Task.Delay(100));
        AssertLocked();
        page.RecordVolumeActivity();
        AssertLocked();
        UiTestPump.Run(Task.Delay(250));
        AssertLocked();
        page.ProcessVolumeOverlayIdle(TimeSpan.FromSeconds(1), true);
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
    public void EntryAndExitFadeTogetherAndActivityPreservesEntry()
    {
        var page = CreatePage();
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);
        page.ShowVolumeOverlay(false, new Point(), true, false);
        var panel = (Border)page.FindName("VolumeOverlay");
        Assert.That(panel.Opacity, Is.LessThan(1));
        var content = (Grid)page.FindName("WorkstationContent");
        Assert.That(content.Effect, Is.TypeOf<BlurEffect>());
        var blur = (BlurEffect)content.Effect;
        page.RecordVolumeActivity();
        Assert.That(panel.Opacity, Is.LessThan(1));
        UiTestPump.Run(Task.Delay(100));
        Assert.That(panel.Opacity, Is.InRange(0.01, 0.99));
        Assert.That(blur.Radius, Is.InRange(0.01, 5.99));
        UiTestPump.Run(Task.Delay(150));
        Assert.That(panel.Opacity, Is.EqualTo(1).Within(0.001));
        Assert.That(blur.Radius, Is.EqualTo(6).Within(0.001));
        page.ProcessVolumeOverlayIdle(TimeSpan.FromSeconds(1), true);
        UiTestPump.Run(Task.Delay(100));
        Assert.That(page.IsVolumeOverlayOpen, Is.True);
        Assert.That(panel.Opacity, Is.InRange(0.01, 0.99));
        Assert.That(blur.Radius, Is.InRange(0.01, 5.99));
        UiTestPump.Run(Task.Delay(150));
        Assert.That(page.IsVolumeOverlayOpen, Is.False);
        Assert.That(content.Effect, Is.Null);
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
        page.ProcessVolumeOverlayIdle(TimeSpan.FromSeconds(1), true);
        UiTestPump.Run(Task.Delay(100));
        var panel = (Border)page.FindName("VolumeOverlay");
        var opacity = panel.Opacity;
        page.RecordVolumeActivity();
        Assert.That(panel.Opacity, Is.EqualTo(opacity).Within(0.01));
        UiTestPump.Run(Task.Delay(250));
        Assert.That(page.IsVolumeOverlayOpen, Is.True);
        Assert.That(panel.Opacity, Is.EqualTo(1).Within(0.001));
        page.DismissVolumeOverlay();
        Assert.That(((Grid)page.FindName("WorkstationContent")).Effect, Is.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DisabledAnimationAppliesFinalStatesAndHighContrastStaysSharp(bool highContrast)
    {
        var page = CreatePage();
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);
        page.ShowVolumeOverlay(false, new Point(), false, highContrast);
        var panel = (Border)page.FindName("VolumeOverlay");
        var content = (Grid)page.FindName("WorkstationContent");
        Assert.That(panel.Opacity, Is.EqualTo(1));
        Assert.That(panel.HasAnimatedProperties, Is.False);
        if (highContrast) Assert.That(content.Effect, Is.Null);
        else Assert.That(((BlurEffect)content.Effect).Radius, Is.EqualTo(6));
        page.ProcessVolumeOverlayIdle(TimeSpan.FromMilliseconds(999), false);
        Assert.That(page.IsVolumeOverlayOpen, Is.True);
        page.ProcessVolumeOverlayIdle(TimeSpan.FromSeconds(1), false);
        Assert.That(page.IsVolumeOverlayOpen, Is.False);
        Assert.That(content.Effect, Is.Null);
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
        Assert.That(panel.HasAnimatedProperties, Is.False);
        Assert.That(((Grid)page.FindName("WorkstationContent")).Effect, Is.Null);
        UiTestPump.Run(Task.Delay(250));
        Assert.That(page.IsVolumeOverlayOpen, Is.False);
        Assert.That(page.ActiveVolumeWheel, Is.Null);
        Assert.That(((Grid)page.FindName("WorkstationContent")).IsHitTestVisible, Is.True);
    }

    [Test]
    public void ReopeningWithChangedSystemSettingsSettlesMotionAndClearsBlur()
    {
        var page = CreatePage();
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);
        page.ShowVolumeOverlay(false, new Point(), true, false);
        var panel = (Border)page.FindName("VolumeOverlay");
        var content = (Grid)page.FindName("WorkstationContent");
        page.ShowVolumeOverlay(false, new Point(), false, true);
        Assert.That(panel.HasAnimatedProperties, Is.False);
        Assert.That(panel.Opacity, Is.EqualTo(1));
        Assert.That(content.Effect, Is.Null);
        page.ShowVolumeOverlay(false, new Point(), false, false);
        Assert.That(((BlurEffect)content.Effect).Radius, Is.EqualTo(6));
    }

    [Test]
    public void ReopeningDuringExitAndEscapeClearOldCompletion()
    {
        var page = CreatePage();
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);
        page.ShowVolumeOverlay(false, new Point(), false, false);
        page.ProcessVolumeOverlayIdle(TimeSpan.FromSeconds(1), true);
        UiTestPump.Run(Task.Delay(100));
        var panel = (Border)page.FindName("VolumeOverlay");
        var opacity = panel.Opacity;
        page.ShowVolumeOverlay(false, new Point(), true, false);
        Assert.That(panel.Opacity, Is.EqualTo(opacity).Within(0.01));
        UiTestPump.Run(Task.Delay(250));
        Assert.That(page.IsVolumeOverlayOpen, Is.True);
        Assert.That(panel.Opacity, Is.EqualTo(1));
        page.HandleVolumeOverlayKey(System.Windows.Input.Key.Escape, System.Windows.Input.ModifierKeys.None);
        Assert.That(page.IsVolumeOverlayOpen, Is.False);
        Assert.That(((Grid)page.FindName("WorkstationContent")).Effect, Is.Null);
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
