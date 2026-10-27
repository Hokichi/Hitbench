using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Hitbench.Resources.CustomControls;
using Hitbench.Views.Pages;

namespace Hitbench.Test.Presentation;

internal static class WorkstationActionMenus
{
    internal static void Invoke(MenuItem item)
    {
        var peer = new System.Windows.Automation.Peers.MenuItemAutomationPeer(item);
        ((System.Windows.Automation.Provider.IInvokeProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();
        UiTestPump.Run(item.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle).Task);
    }

    internal static MenuItem Find(OHsWorkstationAudioCard card, ICommand command)
    {
        var trigger = (BalloonComboBox)card.FindName("ProjectActionsButton");
        trigger.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        trigger.ContextMenu.PlacementTarget = trigger;
        UiTestPump.Layout(trigger.ContextMenu, 320, 300);
        return trigger.ContextMenu.Items.Cast<MenuItem>().Single(item => ReferenceEquals(item.Command, command));
    }
}
