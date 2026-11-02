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

    [TestCase(FlowDirection.LeftToRight, 40, 8, 1)]
    [TestCase(FlowDirection.RightToLeft, 40, 8, 1)]
    [TestCase(FlowDirection.LeftToRight, 64, 12, 1.5)]
    [TestCase(FlowDirection.RightToLeft, 64, 12, 1.5)]
    public void PopupPlacementKeepsSurfaceOverLauncher(FlowDirection direction, double cell, double inset, double scale)
    {
        var menu = Create();
        try
        {
            menu.FlowDirection = direction;
            var popup = (Popup)menu.Template.FindName("PART_MenuPopup", menu);
            Assert.That(popup.Placement, Is.EqualTo(PlacementMode.Custom));
            Assert.That(popup.CustomPopupPlacementCallback, Is.Not.Null);
            var popupSize = new Size((cell + 2 * inset) * scale, (4 * cell + 2 * inset) * scale);
            var placement = popup.CustomPopupPlacementCallback!(popupSize,
                new Size(cell * scale, cell * scale), new Point(-inset, -inset)).Single();
            // WPF transforms the target's offset before adding the callback's point.
            var anchorX = direction == FlowDirection.LeftToRight ? -inset * scale : (cell + inset) * scale;
            Assert.That(anchorX + placement.Point.X + inset * scale, Is.EqualTo(0).Within(.001));
            Assert.That(-inset * scale + placement.Point.Y + inset * scale, Is.EqualTo(0).Within(.001));
        }
        finally { Unload(menu); }
    }

    [Test]
    public void PopupRequestsOpenAndCloseWithoutNativePresentation()
    {
        var menu = Create();
        try
        {
            menu.OpenMenu(true); menu.OpenMenu(true);
            Assert.That(menu.PopupRequests, Is.EqualTo(new[] { true }));
            var popup = (Popup)menu.Template.FindName("PART_MenuPopup", menu);
            Assert.That(popup.IsOpen, Is.False);
            menu.NotifyPopupClosed();
            Assert.That(menu.IsExpanded, Is.False);
            Assert.That(menu.PopupRequests, Is.EqualTo(new[] { true, false }));
            menu.OpenMenu(true);
            menu.Options[0].RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            menu.CloseMenu(); menu.OpenMenu(true);
            Assert.That(menu.CanInvokeOption(menu.Options[0]), Is.True);
        }
        finally { Unload(menu); }
    }

    [Test]
    public void PopupOpeningFailureRestoresLauncher()
    {
        var menu = Create();
        menu.PresentPopup = open => { if (open) throw new InvalidOperationException("native failure"); };
        try
        {
            Assert.Throws<InvalidOperationException>(() => menu.OpenMenu(true));
            Assert.That(menu.IsExpanded, Is.False);
            Assert.That(menu.Options.Any(menu.CanInvokeOption), Is.False);
            Assert.That(menu.IsHitTestVisible, Is.True);
        }
        finally { Unload(menu); }
    }

    [Test]
    public void ExpansionWorksWithoutAdornerHost()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var menu = new BalloonMenu { PresentPopup = _ => { }, ButtonText = "Actions", ButtonIcon = Application.Current.FindResource("BarsSolid") };
        menu.Options.Add(new BalloonMenuOption { ButtonText = "Open", ButtonIcon = Application.Current.FindResource("Folder") });
        var host = new Grid { Resources = Application.Current.Resources };
        host.Children.Add(menu);
        UiTestPump.Layout(host, 300, 80); Load(menu);
        try { Assert.That(menu.OpenMenu(true), Is.True); Assert.That(menu.CanInvokeOption(menu.Options[0]), Is.True); }
        finally { Unload(menu); }
    }


    [TestCase(FlowDirection.LeftToRight)]
    [TestCase(FlowDirection.RightToLeft)]
    public void VerticalLayoutPreservesOptionContentDirection(FlowDirection ownerDirection)
    {
        var menu = Create();
        try
        {
            menu.FlowDirection = ownerDirection;
            menu.OpenMenu(true);
            Assert.That(menu.Options.All(option => option.FlowDirection == FlowDirection.LeftToRight), Is.True);
        }
        finally { Unload(menu); }
    }


    [Test]
    public void HoverOwnershipIncludesOptionRegion()
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

    [TestCase(0)] [TestCase(.1)] [TestCase(.5)] [TestCase(.9)]
    public void OptionsStayInactiveUntilRevealCompletes(double progress)
    {
        var menu = Create();
        try
        {
            menu.Animate = true; menu.Enter();
            menu.ApplyRevealProgress(progress, menu.PresentationGeneration);
            foreach (var option in menu.Options)
            {
                Assert.That(option.IsHitTestVisible, Is.False);
                Assert.That(option.Focusable, Is.False);
                Assert.That(menu.CanInvokeOption(option), Is.False);
            }
            menu.ApplyRevealProgress(1, menu.PresentationGeneration);
            Assert.That(menu.Options.All(menu.CanInvokeOption), Is.True);
        }
        finally { Unload(menu); }
    }

    [TestCase(1, 48)] [TestCase(4, 48)] [TestCase(4, 32)]
    public void PopupLeavesFollowingControlInPlace(int count, double size)
    {
        var menu = Create(count); var parent = (Grid)menu.Parent;
        parent.Children.Remove(menu);
        var host = new StackPanel { Orientation = Orientation.Vertical };
        menu.ButtonSize = size;
        var next = new Border { Height = 50 };
        host.Children.Add(menu); host.Children.Add(next);
        UiTestPump.Layout(host, 800, 800);
        var before = next.TranslatePoint(new Point(), host).Y;
        menu.OpenMenu(true); UiTestPump.Layout(host, 800, 800);
        Assert.That(next.TranslatePoint(new Point(), host).Y - before, Is.EqualTo(0).Within(.01));
        menu.CloseMenu(); UiTestPump.Layout(host, 800, 800);
        Assert.That(next.TranslatePoint(new Point(), host).Y, Is.EqualTo(before).Within(.01));
        Unload(menu);
    }

    [Test]
    public void RepeatedOpenDoesNotRestartActiveTransition()
    {
        var menu = Create();
        try
        {
            menu.Animate = true; menu.Enter(); menu.ApplyRevealProgress(.5, menu.PresentationGeneration);
            var generation = menu.PresentationGeneration;
            menu.Enter();
            Assert.That(menu.PresentationGeneration, Is.EqualTo(generation));
            Assert.That(menu.RevealProgress, Is.EqualTo(.5));
        }
        finally { Unload(menu); }
    }

    [Test]
    public void ClosingRejectsQueuedOptionInvocation()
    {
        var menu = Create(); var calls = 0;
        try
        {
            menu.Options[0].Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => calls++);
            menu.OpenMenu(true);
            var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(menu.Options[0])!;
            ((System.Windows.Automation.Provider.IInvokeProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();
            menu.CloseMenu(); Drain();
            Assert.That(calls, Is.Zero);
        }
        finally { Unload(menu); }
    }

    [TestCase(true)] [TestCase(false)]
    public void ExpandedHandlerCanDisableOrCloseOwnerWithoutLeavingActiveOptions(bool disable)
    {
        var menu = Create();
        menu.Expanded += (_, _) => { if (disable) menu.IsEnabled = false; else menu.CloseMenu(); };
        try
        {
            Assert.That(menu.OpenMenu(true), Is.False);
            Assert.That(menu.IsExpanded, Is.False);
            Assert.That(menu.Options.Any(menu.CanInvokeOption), Is.False);
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
            Assert.That(menu.IsHitTestVisible, Is.True);
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
    public void PresentationFailureRestoresLauncher()
    {
        var menu = Create();
        menu.Expanded += (_, _) => throw new InvalidOperationException("presentation failed");
        try
        {
            Assert.Throws<InvalidOperationException>(() => menu.OpenMenu(false));
            Assert.That(menu.IsExpanded, Is.False);
            Assert.That(menu.IsHitTestVisible, Is.True);
        }
        finally { Unload(menu); }
    }


    [Test]
    public void OwnerResizeDismissesActiveSession()
    {
        var menu = Create();
        try
        {
            menu.OpenMenu(false);
            var generation = menu.PresentationGeneration;
            menu.ButtonSize = 48;
            UiTestPump.Layout((FrameworkElement)menu.Parent, 400, 100);
            Assert.That(menu.IsExpanded, Is.False);
            Assert.That(menu.PresentationGeneration, Is.GreaterThan(generation));
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
