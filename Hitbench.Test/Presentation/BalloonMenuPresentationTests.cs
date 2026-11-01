using Hitbench.Resources.CustomControls;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class BalloonMenuPresentationTests
{
    internal static void Drain() => UiTestPump.Run(Dispatcher.CurrentDispatcher.InvokeAsync(
        () => { }, DispatcherPriority.ApplicationIdle).Task);
    internal static void Load(FrameworkElement control) => control.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
    internal static void Unload(FrameworkElement control) => control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));

    internal static StubBalloonMenu Create(int count = 4)
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var menu = new StubBalloonMenu { ButtonText = "Actions", ButtonIcon = Application.Current.FindResource("BarsSolid"),
            Style = (Style)Application.Current.FindResource(typeof(BalloonMenu)) };
        for (var i = 0; i < count; i++) menu.Options.Add(new BalloonMenuOption
        { ButtonText = $"Action {i}", ButtonIcon = Application.Current.FindResource("Folder") });
        var host = new Grid { Resources = Application.Current.Resources };
        host.Children.Add(menu);
        UiTestPump.Layout(host, 300, 80);
        Load(menu);
        return menu;
    }

    [TestCase(FlowDirection.LeftToRight)]
    [TestCase(FlowDirection.RightToLeft)]
    public void RevealKeepsPhysicalAnchor(FlowDirection direction)
    {
        var menu = Create();
        try
        {
            menu.FlowDirection = direction;
            var bounds = new Rect(20, 30, 40, 40);
            Assert.That(menu.CalculateRevealBounds(bounds, 160, 0), Is.EqualTo(bounds));
            Assert.That(menu.CalculateRevealBounds(bounds, 160, .5), Is.EqualTo(new Rect(20, 30, 100, 40)));
            Assert.That(menu.CalculateRevealBounds(bounds, 160, 1), Is.EqualTo(new Rect(20, 30, 160, 40)));
        }
        finally { Unload(menu); }
    }

    [TestCase(FlowDirection.LeftToRight, -4d)]
    [TestCase(FlowDirection.RightToLeft, -124d)]
    public void PlacementCompensatesNativeFlowMirroring(FlowDirection targetFlow, double expectedX)
    {
        var menu = Create();
        try
        {
            var offset = menu.CalculatePopupOffset(new Rect(20, 30, 40, 40), 160, 24, targetFlow);
            Assert.That(offset, Is.EqualTo(new Point(expectedX, 6)));
            // WPF aligns the child's opposite edge when target/child flow differs.
            var nativeLeft = targetFlow == FlowDirection.LeftToRight ? offset.X : 500 - offset.X - 208;
            var expectedAnchor = targetFlow == FlowDirection.LeftToRight ? 20 : 440;
            Assert.That(nativeLeft + 24, Is.EqualTo(expectedAnchor));
        }
        finally { Unload(menu); }
    }

    [Test]
    public void HoverOwnershipCrossesPopupSeam()
    {
        var menu = Create();
        try
        {
            menu.Enter();
            Assert.That(menu.IsExpanded, Is.True);
            menu.PointerInside = true;
            menu.Leave(); Drain();
            Assert.That(menu.IsExpanded, Is.True);
            menu.PointerInside = false;
            menu.Leave(); Drain();
            Assert.That(menu.IsExpanded, Is.False);
            Assert.That(menu.PresentationRequests, Is.EqualTo(new[] { true, false }));
        }
        finally { Unload(menu); }
    }

    [Test]
    public void ReentryContinuesCurrentProgressAndRejectsOldUpdates()
    {
        var menu = Create();
        try
        {
            menu.Animate = true;
            menu.Enter();
            menu.ApplyRevealProgress(.4, menu.PresentationGeneration);
            menu.Leave(); Drain();
            var old = menu.PresentationGeneration;
            var progress = menu.RevealProgress;
            menu.Enter();
            Assert.That(menu.RevealProgress, Is.EqualTo(progress).Within(.02));
            menu.ApplyRevealProgress(0, old);
            Assert.That(menu.IsExpanded, Is.True);
            Assert.That(menu.RevealProgress, Is.GreaterThan(0));
        }
        finally { Unload(menu); }
    }

    [Test]
    public void InvisibleOptionsCannotReceiveInput()
    {
        var menu = Create();
        try
        {
            menu.Animate = true;
            menu.Enter();
            menu.ApplyRevealProgress(0, menu.PresentationGeneration);
            Assert.That(menu.IsOptionRevealed(menu.Options[0]), Is.False, "The collapsed endpoint has no visible options.");
            Assert.That(menu.IsOptionRevealed(menu.Options[^1]), Is.False);
            Assert.That(menu.Options[^1].IsHitTestVisible, Is.False);
            Assert.That(menu.IsHitTestVisible, Is.False);
            menu.ApplyRevealProgress(1, menu.PresentationGeneration);
            Assert.That(menu.IsOptionRevealed(menu.Options[^1]), Is.True);
            Assert.That(menu.Options[^1].IsHitTestVisible, Is.True);
            menu.CloseMenu();
            Assert.That(menu.IsHitTestVisible, Is.True);
        }
        finally { Unload(menu); }
    }

    [Test]
    public void MembershipMutationClosesAndReopensCurrentOptions()
    {
        var menu = Create();
        try
        {
            menu.OpenMenu(false);
            var removed = menu.Options[0];
            menu.Options.RemoveAt(0);
            Assert.That(menu.IsExpanded, Is.False);
            Assert.That(removed.MenuOwner, Is.Null);
            Assert.That(menu.OpenMenu(false), Is.True);
            Assert.That(menu.Options.All(option => ReferenceEquals(option.MenuOwner, menu)), Is.True);
        }
        finally { Unload(menu); }
    }

    [TestCase("disable")]
    [TestCase("unload")]
    [TestCase("retemplate")]
    public void RetemplateDisableUnloadReleasePresentation(string operation)
    {
        var menu = Create();
        menu.OpenMenu(false);
        if (operation == "disable") menu.IsEnabled = false;
        else if (operation == "unload") Unload(menu);
        else menu.OnApplyTemplate();
        Assert.That(menu.IsExpanded, Is.False);
        Assert.That(menu.PresentationRequests.Last(), Is.False);
        if (operation == "unload") Assert.That(menu.Options.All(option => option.MenuOwner is null), Is.True);
        Unload(menu);
    }

    [Test]
    public void OldLeaveCannotCloseNewSession()
    {
        var menu = Create();
        try
        {
            menu.Enter(); menu.Leave(); menu.CloseMenu(); menu.Enter(); Drain();
            Assert.That(menu.IsExpanded, Is.True);
        }
        finally { Unload(menu); }
    }

    [Test]
    public void NativeCloseCompletionCannotCloseNewSession()
    {
        var menu = Create();
        try
        {
            menu.OpenMenu(false);
            var old = menu.PresentationGeneration;
            menu.CloseMenu(); menu.OpenMenu(false);
            menu.CompleteNativeClose(old);
            Assert.That(menu.IsExpanded, Is.True);
            menu.CompleteNativeClose(menu.PresentationGeneration);
            Assert.That(menu.IsExpanded, Is.False);
        }
        finally { Unload(menu); }
    }

    [Test]
    public void PresentationFailureRestoresLauncher()
    {
        var menu = Create();
        menu.PresentPopup = (_, open) => { if (open) throw new InvalidOperationException("presentation failed"); };
        try
        {
            Assert.Throws<InvalidOperationException>(() => menu.OpenMenu(false));
            Assert.That(menu.IsExpanded, Is.False);
            Assert.That(menu.IsHitTestVisible, Is.True);
        }
        finally { Unload(menu); }
    }

    [Test]
    public void MissingRenderedBoundsDoNotPresentNativePopup()
    {
        var menu = Create();
        try
        {
            menu.Bounds = Rect.Empty;
            Assert.That(menu.OpenMenu(false), Is.False);
            Assert.That(menu.IsExpanded, Is.False);
            Assert.That(menu.PresentationRequests, Is.Empty);
        }
        finally { Unload(menu); }
    }

    [Test]
    public void OwnerMovementRecomputesPlacement()
    {
        var menu = Create();
        try
        {
            menu.OpenMenu(false);
            var popup = (Popup)menu.Template.FindName("PART_StatePopup", menu);
            var before = popup.HorizontalOffset;
            menu.Bounds = new Rect(70, 60, 40, 40);
            menu.MoveOwner();
            Assert.That(popup.HorizontalOffset - before, Is.EqualTo(50));
        }
        finally { Unload(menu); }
    }

    [Test]
    public void ReducedMotionSettlesAndSettingChangesReleaseClocks()
    {
        var menu = Create();
        try
        {
            menu.Enter();
            Assert.That(menu.RevealProgress, Is.EqualTo(1));
            menu.CloseMenu();
            menu.Animate = true; menu.Enter();
            menu.ApplyRevealProgress(.3, menu.PresentationGeneration);
            menu.Animate = false;
            menu.RefreshMotionSettings();
            Assert.That(menu.RevealProgress, Is.EqualTo(1));
            menu.CloseMenu(); Drain();
            Assert.That(menu.IsExpanded, Is.False);
            Assert.That(menu.HasAnimatedProperties, Is.False);
        }
        finally { Unload(menu); }
    }
}
