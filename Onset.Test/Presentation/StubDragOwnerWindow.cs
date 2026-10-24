using System.Windows;

namespace Onset.Test.Presentation;

public sealed class StubDragOwnerWindow : Window
{
    public void NotifyDeactivated() => OnDeactivated(EventArgs.Empty);
}
