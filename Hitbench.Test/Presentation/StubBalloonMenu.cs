using Hitbench.Resources.CustomControls;
using System.Windows;
using System.Windows.Input;

namespace Hitbench.Test.Presentation;

public sealed class StubBalloonMenu : BalloonMenu
{
    public Rect Bounds { get; set; } = new(20, 30, 40, 40);
    public bool PointerInside { get; set; }
    public bool Animate { get; set; }
    public bool FocusInside { get; set; } = true;
    public List<UIElement> FocusRequests { get; } = [];
    public List<bool> PresentationRequests { get; } = [];

    public StubBalloonMenu() => PresentPopup = (_, open) => PresentationRequests.Add(open);
    protected override Rect CaptureLauncherBounds() => Bounds;
    protected override bool IsPointerWithinMenu() => PointerInside;
    protected override bool ClientAreaAnimationEnabled => Animate;
    protected override bool OwnsKeyboardFocus => FocusInside;
    protected override bool FocusMenuElement(UIElement target) { FocusRequests.Add(target); FocusInside = true; return true; }
    public void Enter() => OnMouseEnter(new MouseEventArgs(Mouse.PrimaryDevice, 0));
    public void Leave() => OnMouseLeave(new MouseEventArgs(Mouse.PrimaryDevice, 0));
    public void MoveOwner() => RefreshPlacement();
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
