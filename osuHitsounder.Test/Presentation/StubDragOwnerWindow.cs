using System.Windows;

namespace osuHitsounder.Test.Presentation;

public sealed class StubDragOwnerWindow : Window
{
    public void NotifyDeactivated() => OnDeactivated(EventArgs.Empty);
}
