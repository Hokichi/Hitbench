using CommunityToolkit.Mvvm.Input;
using osuHitsounder.Resources.CustomControls;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public sealed class BalloonComboBoxTests
{
    private static void Drain() => UiTestPump.Run(Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task);
    private static void Load(FrameworkElement control) => control.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
    private static void Unload(FrameworkElement control) => control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
    private static IInvokeProvider Provider(MenuItem item) => (IInvokeProvider)new MenuItemAutomationPeer(item).GetPattern(PatternInterface.Invoke);

    private static StubBalloonComboBox Create(params MenuItem[] items)
    {
        var control = new StubBalloonComboBox { ButtonText = "Actions", ContextMenu = new ContextMenu() };
        control.ContextMenu.SetBinding(UIElement.IsEnabledProperty, new Binding(nameof(BalloonComboBox.IsMenuOwnerActive))
            { Source = control, Mode = BindingMode.OneWay });
        foreach (var item in items)
        {
            control.ContextMenu.Items.Add(item);
            // Native visual inheritance is tested with the actual template in the resource fixture.
            item.SetBinding(UIElement.IsEnabledProperty, new Binding(nameof(UIElement.IsEnabled))
                { Source = control.ContextMenu, Mode = BindingMode.OneWay });
        }
        Load(control);
        return control;
    }

    [Test]
    public void TriggerOpensConfiguredMenuWithoutChangingLabel()
    {
        var control = Create(new MenuItem { Header = "First" });
        var source = new object();
        control.DataContext = source;
        control.Activate();
        Assert.Multiple(() =>
        {
            Assert.That(control.IsDropDownOpen, Is.True);
            Assert.That(control.ButtonText, Is.EqualTo("Actions"));
            Assert.That(control.DataContext, Is.SameAs(source));
            Assert.That(control.ContextMenu.PlacementTarget, Is.SameAs(control));
            Assert.That(control.ContextMenu.IsOpen, Is.False);
            Assert.That(control.PresentationRequests.Count(x => x.Open), Is.EqualTo(1));
        });
        Unload(control);
    }

    [Test]
    public void EachItemKeepsItsEventCommandAndParameter()
    {
        var parameters = new List<object?>();
        var firstParameter = new object();
        var secondParameter = new object();
        var command = new RelayCommand<object>(parameters.Add);
        var first = new MenuItem { Header = "First", Command = command, CommandParameter = firstParameter };
        var second = new MenuItem { Header = "Second", Command = command, CommandParameter = secondParameter };
        var firstClicks = 0;
        var secondClicks = 0;
        first.Click += (_, _) => firstClicks++;
        second.Click += (_, _) => secondClicks++;
        var control = Create(first, second);
        foreach (var item in new[] { first, second })
        {
            control.Activate();
            Provider(item).Invoke();
            Drain();
            Assert.That(control.IsDropDownOpen, Is.False);
        }
        Assert.Multiple(() =>
        {
            Assert.That(firstClicks, Is.EqualTo(1));
            Assert.That(secondClicks, Is.EqualTo(1));
            Assert.That(parameters, Is.EqualTo(new[] { firstParameter, secondParameter }));
            Assert.That(first.Command, Is.SameAs(command));
            Assert.That(control.ButtonText, Is.EqualTo("Actions"));
        });
        Unload(control);
    }

    [Test]
    public void SameItemInvokesOnEachOpening()
    {
        var clicks = 0;
        var executions = 0;
        var command = new RelayCommand(() => executions++);
        var item = new MenuItem { Header = "Again", Command = command };
        item.Click += (_, _) => clicks++;
        var control = Create(item);
        for (var i = 0; i < 2; i++)
        {
            control.Activate();
            Provider(item).Invoke();
            Drain();
        }
        Assert.That(clicks, Is.EqualTo(2));
        Assert.That(executions, Is.EqualTo(2));
        Assert.That(item.Command, Is.SameAs(command));
        Unload(control);
    }

    [Test]
    public void DisabledAndCommandGatedItemsCannotActivate()
    {
        var allowed = false;
        var executions = 0;
        var clicks = 0;
        var command = new RelayCommand(() => executions++, () => allowed);
        var item = new MenuItem { Header = "Gated", Command = command };
        item.Click += (_, _) => clicks++;
        var control = Create(item);
        control.Activate();
        Assert.Throws<ElementNotEnabledException>(() => Provider(item).Invoke());
        allowed = true;
        command.NotifyCanExecuteChanged();
        item.IsEnabled = false;
        Assert.Throws<ElementNotEnabledException>(() => Provider(item).Invoke());
        Assert.That(clicks, Is.Zero);
        Assert.That(executions, Is.Zero);
        item.IsEnabled = true;
        Provider(item).Invoke();
        Drain();
        Assert.That(clicks, Is.EqualTo(1));
        Assert.That(executions, Is.EqualTo(1));
        Unload(control);
    }

    [Test]
    public void EscapeAndDismissalDoNotInvokeItems()
    {
        var executions = 0;
        var control = Create(new MenuItem { Header = "Action", Command = new RelayCommand(() => executions++) });
        control.Activate();
        control.PressKey(Key.Escape);
        Assert.That(control.IsDropDownOpen, Is.False);
        Assert.That(control.FocusRequests, Is.EqualTo(1));
        control.Activate();
        control.ContextMenu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent));
        Assert.That(control.IsDropDownOpen, Is.False);
        Assert.That(control.FocusRequests, Is.EqualTo(1), "Pointer dismissal must preserve external focus.");
        Assert.That(executions, Is.Zero);
        Unload(control);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void InactiveOwnerRejectsNewActivationAndAcceptedInvocationFinishes(bool unload)
    {
        var executions = 0;
        var item = new MenuItem { Header = "Action", Command = new RelayCommand(() => executions++) };
        var control = Create(item);
        control.Activate();
        Provider(item).Invoke();
        if (unload) Unload(control); else control.IsEnabled = false;
        Assert.That(control.IsMenuOwnerActive, Is.False);
        Assert.That(control.IsDropDownOpen, Is.False);
        Assert.Throws<ElementNotEnabledException>(() => Provider(item).Invoke());
        Drain();
        Assert.That(executions, Is.EqualTo(1));
        Assert.That(control.FocusRequests, Is.Zero);
        Assert.That(control.ContextMenu.PlacementTarget, Is.Null);
        Unload(control);
    }

    [Test]
    public void ReplacementRetemplateAndReloadDetachOldSessions()
    {
        var control = Create(new MenuItem { Header = "Old" });
        var oldMenu = control.ContextMenu;
        control.Activate();
        control.ContextMenu = new ContextMenu { Items = { new MenuItem { Header = "New" } } };
        Assert.That(oldMenu.PlacementTarget, Is.Null);
        control.Activate();
        oldMenu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent));
        Assert.That(control.IsDropDownOpen, Is.True);
        control.OnApplyTemplate();
        Assert.That(control.IsDropDownOpen, Is.False);
        Unload(control);
        Load(control);
        control.Activate();
        var requests = control.PresentationRequests.Count;
        control.ContextMenu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent));
        oldMenu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent));
        Assert.That(control.IsDropDownOpen, Is.False);
        Assert.That(control.PresentationRequests, Has.Count.EqualTo(requests));
        Unload(control);
    }

    [Test]
    public void NoMenuAndEmptyMenuAreSafe()
    {
        var control = Create();
        control.Activate();
        control.ContextMenu = null;
        control.Activate();
        Assert.That(control.IsDropDownOpen, Is.False);
        Assert.That(control.PresentationRequests, Is.Empty);
        Unload(control);
    }

    [Test]
    public void TriggerAutomationExposesNameAndGuardedInvoke()
    {
        var control = Create(new MenuItem { Header = "First" });
        var peer = UIElementAutomationPeer.CreatePeerForElement(control)!;
        Assert.That(peer.GetAutomationControlType(), Is.EqualTo(AutomationControlType.Button));
        Assert.That(peer.GetName(), Is.EqualTo("Actions"));
        AutomationProperties.SetName(control, "More actions");
        Assert.That(peer.GetName(), Is.EqualTo("More actions"));
        var provider = (IInvokeProvider)peer.GetPattern(PatternInterface.Invoke);
        provider.Invoke();
        Drain();
        Assert.That(control.IsDropDownOpen, Is.True);
        control.PressKey(Key.Escape);
        provider.Invoke();
        Unload(control);
        Drain();
        Assert.That(control.IsDropDownOpen, Is.False);
        control.IsEnabled = false;
        Assert.Throws<ElementNotEnabledException>(() => provider.Invoke());
    }

    [TestCase(Key.Down)]
    [TestCase(Key.Enter)]
    public void OpenKeysActivateOnce(Key key)
    {
        var control = Create(new MenuItem { Header = "First" });
        control.PressKey(key);
        Assert.That(control.IsDropDownOpen, Is.True);
        Assert.That(control.PresentationRequests.Count(x => x.Open), Is.EqualTo(1));
        Unload(control);
    }
}
