using System.Windows;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace Hitbench;

public partial class MainWindow
{
    public void RestoreAndActivate()
    {
        if (closed) return;
        RestoreAndActivateCore(this, WindowState, lastIsMaximized, state => SetCurrentValue(WindowStateProperty, state),
            owner => owner.OwnedWindows.Cast<Window>(), window => window.IsVisible && window.IsEnabled,
            window =>
            {
                if (window != this && window.WindowState == WindowState.Minimized) SystemCommands.RestoreWindow(window);
                window.Activate();
                var handle = new WindowInteropHelper(window).Handle;
                if (handle != 0)
                {
                    // Common file pickers are native owned windows, absent from WPF OwnedWindows.
                    var target = FindNativeActivationTarget(handle, GetLastActivePopup, IsWindowVisible, IsWindowEnabled);
                    SetForegroundWindow(target);
                }
            });
    }

    internal static nint FindNativeActivationTarget(nint root, Func<nint, nint> lastPopup,
        Predicate<nint> visible, Predicate<nint> enabled)
    {
        var seen = new HashSet<nint>();
        var target = root;
        for (var current = root; seen.Add(current);)
        {
            var next = lastPopup(current);
            if (next == 0 || next == current || !visible(next)) break;
            current = next;
            if (enabled(current)) target = current;
        }
        return target;
    }

    [DllImport("user32.dll")]
    private static extern nint GetLastActivePopup(nint window);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")]
    private static extern bool IsWindowEnabled(nint window);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint window);

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
