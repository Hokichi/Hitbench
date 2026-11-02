using Hitbench.Resources.CustomControls;
using System.Windows;
using System.Windows.Input;

namespace Hitbench.Test.Presentation;

public sealed class StubBalloonMenu : BalloonMenu
{
    public bool PointerInside { get; set; }
    public bool Animate { get; set; }
    public bool FocusInside { get; set; } = true;
    public List<UIElement> FocusRequests { get; } = [];
    public List<bool> PresentationRequests { get; } = [];

    public List<bool> PopupRequests { get; } = [];
    public StubBalloonMenu() { PresentPopup = PopupRequests.Add; Expanded += (_, _) => PresentationRequests.Add(true); Collapsed += (_, _) => PresentationRequests.Add(false); }
    protected override bool IsPointerWithinMenu() => PointerInside;
    protected override bool ClientAreaAnimationEnabled => Animate;
    protected override bool OwnsKeyboardFocus => FocusInside;
    protected override bool FocusMenuElement(UIElement target) { FocusRequests.Add(target); FocusInside = true; return true; }
    public void Enter() => OnMouseEnter(new MouseEventArgs(Mouse.PrimaryDevice, 0));
    public void Leave() => OnMouseLeave(new MouseEventArgs(Mouse.PrimaryDevice, 0));
    public void PressKey(Key key, UIElement? target = null)
    {
        using var source = new InMemoryPresentationSource();
        var element = target ?? this;
        element.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = KeyDownEvent });
        element.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = KeyUpEvent });
    }
    public void LoseFocus()
    {
        FocusInside = false;
        OnLostKeyboardFocus(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, this, null));
    }
}
