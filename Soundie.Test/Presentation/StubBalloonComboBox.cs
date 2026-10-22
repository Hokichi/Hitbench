using System.Windows.Controls;
using System.Windows.Input;
using Soundie.Resources.CustomControls;

namespace Soundie.Test.Presentation;

public sealed class StubBalloonComboBox : BalloonComboBox
{
    public List<(ContextMenu Menu, bool Open)> PresentationRequests { get; } = [];
    public int FocusRequests { get; private set; }

    protected override void SetMenuOpen(ContextMenu menu, bool isOpen) => PresentationRequests.Add((menu, isOpen));
    protected override void RestoreTriggerFocus() => FocusRequests++;
    public void Activate() => InvokeActionMenu();
    public void PressKey(Key key)
    {
        using var source = new InMemoryPresentationSource();
        RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = KeyDownEvent });
        if (key == Key.Space)
            RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = KeyUpEvent });
    }
}
