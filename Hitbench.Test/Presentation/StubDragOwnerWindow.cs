using System.Windows;

namespace Hitbench.Test.Presentation;

public sealed class StubDragOwnerWindow : Window
{
    public void NotifyDeactivated() => OnDeactivated(EventArgs.Empty);
}
