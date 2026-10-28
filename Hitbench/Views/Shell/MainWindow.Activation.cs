using System.Windows;

namespace Hitbench;

public partial class MainWindow
{
    public void RestoreAndActivate()
    {
        if (closed) return;
        RestoreAndActivateCore(this, WindowState, lastIsMaximized, requestWindowState,
            owner => owner.OwnedWindows.Cast<Window>(), window => window.IsVisible && window.IsEnabled,
            window =>
            {
                if (window != this && window.WindowState == WindowState.Minimized) SystemCommands.RestoreWindow(window);
                window.Activate();
            });
    }

    internal static void RestoreAndActivateCore(Window shell, WindowState currentState, bool wasMaximized,
        Action<WindowState> requestState, Func<Window, IEnumerable<Window>> ownedWindows,
        Predicate<Window> eligible, Action<Window> activate)
    {
        if (currentState == WindowState.Minimized) requestState(wasMaximized ? WindowState.Maximized : WindowState.Normal);
        Window Deepest(Window owner)
        {
            foreach (var child in ownedWindows(owner).Reverse())
            {
                var target = Deepest(child);
                if (eligible(target)) return target;
            }
            return owner;
        }
        activate(Deepest(shell));
    }
}
