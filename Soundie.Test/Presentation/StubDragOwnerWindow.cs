using System.Windows;

namespace Soundie.Test.Presentation;

public sealed class StubDragOwnerWindow : Window
{
    public void NotifyDeactivated() => OnDeactivated(EventArgs.Empty);
}
