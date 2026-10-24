using Hitbench.Resources.CustomControls;
using System.Windows.Input;

namespace Hitbench.Test.Presentation;

public sealed class StubSegmentedToggleOption : SegmentedToggleOption
{
    public void Activate() => OnClick();
    public void PressKey(Key key)
    {
        using var source = new InMemoryPresentationSource();
        var down = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = KeyDownEvent };
        RaiseEvent(down);
        if (!down.Handled || key == Key.Space)
            RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = KeyUpEvent });
    }
}
