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

    internal static void Prepare(BalloonMenu menu)
    {
        menu.PresentPresentation = (_, _) => { };
        menu.ApplyTemplate();
        menu.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        UiTestPump.Run(menu.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle).Task);
    }

    internal static void Open(BalloonMenu menu)
    {
        Prepare(menu);
        menu.OpenMenu(false);
        menu.ApplyRevealProgress(1, menu.PresentationGeneration);
    }

    internal static void Invoke(BalloonMenuOption option)
    {
        var menu = option.MenuOwner ?? throw new InvalidOperationException("Option needs an active test owner.");
        Open(menu);
        var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(option)!;
        ((System.Windows.Automation.Provider.IInvokeProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();
        UiTestPump.Run(option.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle).Task);
    }

    internal static BalloonMenuOption Find(OHsWorkstationToolbar card, ICommand command)
    {
        foreach (var name in new[] { "ProjectActionsButton", "OpenFoldersButton", "AddMenuButton" })
            Prepare((BalloonMenu)card.FindName(name));
        return ((BalloonMenu)card.FindName("ProjectActionsButton")).Options.Single(item => ReferenceEquals(item.Command, command));
    }
}
