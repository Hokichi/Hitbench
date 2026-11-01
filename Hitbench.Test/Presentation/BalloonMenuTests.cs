using CommunityToolkit.Mvvm.Input;
using Hitbench.Resources.CustomControls;
using System.Windows;
using System.Windows.Data;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class BalloonMenuTests
{
    private static IInvokeProvider InvokeProvider(UIElement element) =>
        (IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(element)!.GetPattern(PatternInterface.Invoke);

    [TestCase(Key.Enter)] [TestCase(Key.Space)] [TestCase(Key.Down)]
    public void OpenKeysFocusFirstEligibleOption(Key key)
    {
        var menu = BalloonMenuPresentationTests.Create();
        try
        {
            menu.Options[0].IsEnabled = false;
            menu.PressKey(key);
            Assert.That(menu.IsExpanded, Is.True);
            Assert.That(menu.ActiveOption, Is.SameAs(menu.Options[1]));
            Assert.That(menu.FocusRequests, Is.EqualTo(new[] { menu.Options[1] }));
            Assert.That(menu.PresentationRequests.Count(open => open), Is.EqualTo(1));
        }
        finally { BalloonMenuPresentationTests.Unload(menu); }
    }

    [TestCase(FlowDirection.LeftToRight, Key.Right)]
    [TestCase(FlowDirection.RightToLeft, Key.Left)]
    public void ArrowHomeEndRespectFlowAndSkipDisabled(FlowDirection direction, Key next)
    {
        var menu = BalloonMenuPresentationTests.Create();
        try
        {
            menu.FlowDirection = direction;
            menu.Options[1].IsEnabled = false;
            menu.OpenMenu(true);
            menu.PressKey(next, menu.Options[0]);
            Assert.That(menu.ActiveOption, Is.SameAs(menu.Options[2]));
            menu.PressKey(Key.End, menu.Options[2]);
            Assert.That(menu.ActiveOption, Is.SameAs(menu.Options[3]));
            menu.PressKey(next, menu.Options[3]);
            Assert.That(menu.ActiveOption, Is.SameAs(menu.Options[0]));
            menu.PressKey(Key.Home, menu.Options[3]);
            Assert.That(menu.ActiveOption, Is.SameAs(menu.Options[0]));
        }
        finally { BalloonMenuPresentationTests.Unload(menu); }
    }

    [Test]
    public void KeyboardSessionSurvivesPointerDepartureAndEscapeRestoresEligibleLauncher()
    {
        var menu = BalloonMenuPresentationTests.Create();
        try
        {
            menu.OpenMenu(true); menu.Leave(); BalloonMenuPresentationTests.Drain();
            Assert.That(menu.IsExpanded, Is.True);
            menu.PressKey(Key.Escape, menu.Options[0]);
            Assert.That(menu.IsExpanded, Is.False);
            Assert.That(menu.FocusRequests.Last(), Is.SameAs(menu));
            menu.OpenMenu(true);
            var count = menu.FocusRequests.Count;
            menu.IsEnabled = false; menu.CloseMenu(true);
            Assert.That(menu.FocusRequests, Has.Count.EqualTo(count));
        }
        finally { BalloonMenuPresentationTests.Unload(menu); }
    }

    [Test]
    public void OutsideFocusClosesWithoutRestoration()
    {
        var menu = BalloonMenuPresentationTests.Create();
        try
        {
            menu.OpenMenu(true);
            var count = menu.FocusRequests.Count;
            menu.LoseFocus(); BalloonMenuPresentationTests.Drain();
            Assert.That(menu.IsExpanded, Is.False);
            Assert.That(menu.FocusRequests, Has.Count.EqualTo(count));
        }
        finally { BalloonMenuPresentationTests.Unload(menu); }
    }

    [Test]
    public void PointerReentryPreservesKeyboardOptionFocus()
    {
        var menu = BalloonMenuPresentationTests.Create(3);
        try
        {
            menu.OpenMenu(true);
            menu.PressKey(Key.End, menu.Options[0]);
            menu.Enter();
            Assert.That(menu.ActiveOption, Is.SameAs(menu.Options[2]));
        }
        finally { BalloonMenuPresentationTests.Unload(menu); }
    }

    [Test]
    public void EmptyAndAllDisabledMenusHaveIntentionalActivation()
    {
        var menu = BalloonMenuPresentationTests.Create(0);
        try
        {
            Assert.That(menu.OpenMenu(true), Is.False);
            menu.Options.Add(new BalloonMenuOption { IsEnabled = false, ButtonText = "Unavailable" });
            Assert.That(menu.OpenMenu(true), Is.True);
            Assert.That(menu.ActiveOption, Is.Null);
            Assert.That(menu.FocusRequests.Last(), Is.SameAs(menu));
            Assert.Throws<ElementNotEnabledException>(() => InvokeProvider(menu.Options[0]).Invoke());
        }
        finally { BalloonMenuPresentationTests.Unload(menu); }
    }

    [TestCase(Key.Enter)] [TestCase(Key.Space)]
    public void OptionInvokesCurrentCommandAndParameterOnce(Key key)
    {
        var menu = BalloonMenuPresentationTests.Create(1);
        try
        {
            var values = new List<object?>();
            var parameter = new object();
            menu.Options[0].Command = new RelayCommand<object>(values.Add);
            menu.Options[0].CommandParameter = parameter;
            menu.OpenMenu(true);
            menu.PressKey(key, menu.Options[0]);
            Assert.That(values, Is.EqualTo(new[] { parameter }));
            Assert.That(menu.IsExpanded, Is.False);
        }
        finally { BalloonMenuPresentationTests.Unload(menu); }
    }

    [Test]
    public void CanExecuteChangesAndRebindingPreventStaleExecution()
    {
        var menu = BalloonMenuPresentationTests.Create(1);
        var calls = 0;
        var allowed = true;
        var command = new RelayCommand(() => calls++, () => allowed);
        try
        {
            menu.Options[0].Command = command;
            menu.OpenMenu(true);
            InvokeProvider(menu.Options[0]).Invoke();
            allowed = false; command.NotifyCanExecuteChanged();
            BalloonMenuPresentationTests.Drain();
            Assert.That(calls, Is.Zero);
            Assert.That(menu.Options[0].IsEnabled, Is.False);
            menu.DataContext = new object();
            Assert.That(menu.IsExpanded, Is.False);
        }
        finally { BalloonMenuPresentationTests.Unload(menu); }
    }

    [Test]
    public void OptionOpeningAnotherWorkflowKeepsItsFocus()
    {
        var menu = BalloonMenuPresentationTests.Create(1);
        try
        {
            menu.Options[0].Command = new RelayCommand(() =>
            {
                Assert.That(menu.IsExpanded, Is.False, "Close before a command enters a modal loop.");
                menu.FocusInside = false;
            });
            menu.OpenMenu(true);
            var focusCalls = menu.FocusRequests.Count;
            InvokeProvider(menu.Options[0]).Invoke(); BalloonMenuPresentationTests.Drain();
            Assert.That(menu.FocusInside, Is.False);
            Assert.That(menu.FocusRequests, Has.Count.EqualTo(focusCalls));
        }
        finally { BalloonMenuPresentationTests.Unload(menu); }
    }

    [Test]
    public void ThrowingCommandLeavesNoOpenPresentation()
    {
        var menu = BalloonMenuPresentationTests.Create(1);
        try
        {
            menu.Options[0].Command = new RelayCommand(() => throw new InvalidOperationException("command failed"));
            menu.OpenMenu(true);
            Assert.Throws<InvalidOperationException>(() => menu.Options[0].InvokeAction());
            Assert.That(menu.IsExpanded, Is.False);
            Assert.That(menu.IsHitTestVisible, Is.True);
        }
        finally { BalloonMenuPresentationTests.Unload(menu); }
    }

    [Test]
    public void CommandReopeningMenuKeepsNewSession()
    {
        var menu = BalloonMenuPresentationTests.Create(1);
        try
        {
            menu.Options[0].Command = new RelayCommand(() => menu.OpenMenu(true));
            menu.OpenMenu(true);
            InvokeProvider(menu.Options[0]).Invoke(); BalloonMenuPresentationTests.Drain();
            Assert.That(menu.IsExpanded, Is.True);
        }
        finally { BalloonMenuPresentationTests.Unload(menu); }
    }

    [Test]
    public void AutomationExposesNameStateChildrenAndGuardedActions()
    {
        var menu = BalloonMenuPresentationTests.Create(2);
        try
        {
            var peer = UIElementAutomationPeer.CreatePeerForElement(menu)!;
            var expand = (IExpandCollapseProvider)peer.GetPattern(PatternInterface.ExpandCollapse);
            Assert.That(peer.GetName(), Is.EqualTo("Actions"));
            Assert.That(expand.ExpandCollapseState, Is.EqualTo(ExpandCollapseState.Collapsed));
            expand.Expand(); BalloonMenuPresentationTests.Drain();
            Assert.That(expand.ExpandCollapseState, Is.EqualTo(ExpandCollapseState.Expanded));
            Assert.That(peer.GetChildren().Select(child => child.GetName()), Is.EqualTo(new[] { "Action 0", "Action 1" }));
            expand.Collapse(); BalloonMenuPresentationTests.Drain();
            Assert.That(menu.IsExpanded, Is.False);
            menu.IsEnabled = false;
            Assert.Throws<ElementNotEnabledException>(expand.Expand);
        }
        finally { BalloonMenuPresentationTests.Unload(menu); }
    }

    [Test]
    public void QueuedAutomationCannotCrossOwnerGeneration()
    {
        var menu = BalloonMenuPresentationTests.Create(1);
        var calls = 0;
        try
        {
            menu.Options[0].Command = new RelayCommand(() => calls++);
            menu.OpenMenu(true);
            InvokeProvider(menu.Options[0]).Invoke();
            menu.CloseMenu(); menu.OpenMenu(true); BalloonMenuPresentationTests.Drain();
            Assert.That(calls, Is.Zero);
            InvokeProvider(menu).Invoke();
            BalloonMenuPresentationTests.Unload(menu); BalloonMenuPresentationTests.Load(menu);
            BalloonMenuPresentationTests.Drain();
            Assert.That(menu.IsExpanded, Is.False);
        }
        finally { BalloonMenuPresentationTests.Unload(menu); }
    }

    [Test]
    public void HoverNeverExecutesLauncherCommand()
    {
        var menu = BalloonMenuPresentationTests.Create(1);
        var calls = 0;
        try
        {
            menu.Command = new RelayCommand(() => calls++);
            menu.Enter(); menu.CloseMenu(); menu.PressKey(Key.Enter);
            Assert.That(calls, Is.Zero);
        }
        finally { BalloonMenuPresentationTests.Unload(menu); }
    }

    [TestCase(typeof(BalloonMenu))]
    [TestCase(typeof(BalloonMenuOption))]
    public void TextCannotBeEnabled(Type type)
    {
        var control = (BalloonButton)Activator.CreateInstance(type)!;
        Assert.That(control.ShouldShowText, Is.False);
        control.ShouldShowText = true;
        Assert.That(control.ShouldShowText, Is.False);
        control.ClearValue(BalloonControl.ShouldShowTextProperty);
        control.Style = new Style(type)
        {
            Setters = { new Setter(BalloonControl.ShouldShowTextProperty, true) },
            Triggers = { new Trigger { Property = UIElement.IsEnabledProperty, Value = true,
                Setters = { new Setter(BalloonControl.ShouldShowTextProperty, true) } } }
        };
        Assert.That(control.ShouldShowText, Is.False);
        var source = new BalloonButton();
        control.SetBinding(BalloonControl.ShouldShowTextProperty,
            new Binding(nameof(BalloonControl.ShouldShowText)) { Source = source, Mode = BindingMode.OneWay });
        source.ShouldShowText = true;
        Assert.That(control.ShouldShowText, Is.False);
        control.SetCurrentValue(BalloonControl.ShouldShowTextProperty, true);
        Assert.That(control.ShouldShowText, Is.False);
    }

    [Test]
    public void OptionsAreInstanceOwned()
    {
        var first = new BalloonMenu();
        var second = new BalloonMenu();
        var parameter = new object();
        var command = new RelayCommand<object>(_ => { });
        var option = new BalloonMenuOption { Command = command, CommandParameter = parameter };
        first.Options.Add(option);
        Assert.That(second.Options, Is.Empty);
        Assert.That(option.Command, Is.SameAs(command));
        Assert.That(option.CommandParameter, Is.SameAs(parameter));
        Assert.That(first.IsExpanded, Is.False);
    }

    [TestCase(typeof(BalloonMenu), "BaseBalloonMenuStyle")]
    [TestCase(typeof(BalloonMenuOption), "BaseBalloonMenuOptionStyle")]
    public void ControlsResolveAssignedBalloonContracts(Type type, string baseKey)
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var control = (BalloonButton)Activator.CreateInstance(type)!;
        var host = new System.Windows.Controls.Grid { Resources = Application.Current.Resources };
        host.Children.Add(control);
        UiTestPump.Layout(host, 100, 100);
        Assert.That(control.Style, Is.SameAs(control.FindResource(type)));
        Assert.That(control.Style.BasedOn, Is.SameAs(control.FindResource(baseKey)));
        UiControlContract.AssertTemplate(control, "BalloonControlTemplate");
    }
}
