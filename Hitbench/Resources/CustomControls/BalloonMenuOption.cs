using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Input;

namespace Hitbench.Resources.CustomControls;

public class BalloonMenuOption : BalloonButton
{
    private BalloonMenu? menuOwner;
    private bool spacePressed;
    internal BalloonMenu? MenuOwner
    {
        get => menuOwner;
        set { menuOwner = value; CoerceValue(IsHitTestVisibleProperty); CoerceValue(FocusableProperty); }
    }
    static BalloonMenuOption()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(BalloonMenuOption), new FrameworkPropertyMetadata(typeof(BalloonMenuOption)));
        ShouldShowTextProperty.OverrideMetadata(typeof(BalloonMenuOption), new FrameworkPropertyMetadata(true));
        FocusableProperty.OverrideMetadata(typeof(BalloonMenuOption), new FrameworkPropertyMetadata(true, null,
            (d, value) => (bool)value && ((BalloonMenuOption)d).MenuOwner?.IsOptionRevealed((BalloonMenuOption)d) == true));
        IsHitTestVisibleProperty.OverrideMetadata(typeof(BalloonMenuOption), new UIPropertyMetadata(true, null,
            (d, value) => (bool)value && ((BalloonMenuOption)d).MenuOwner?.IsOptionRevealed((BalloonMenuOption)d) == true));
    }

    public BalloonMenuOption() => Unloaded += (_, _) => { MenuOwner = null; spacePressed = false; };
    protected override AutomationPeer OnCreateAutomationPeer() => new BalloonMenuOptionAutomationPeer(this);

    protected override void OnClick()
    {
        if (MenuOwner is not { } owner || !owner.CanInvokeOption(this)) return;
        // Release native presentation before dispatch can enter a modal loop or throw.
        owner.PrepareOptionInvocation(this);
        base.OnClick();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (MenuOwner?.HandleOptionKey(this, e.Key) == true) e.Handled = true;
        else if (e.Key == Key.Enter)
        {
            if (!e.IsRepeat) OnClick();
            e.Handled = true;
        }
        else if (e.Key == Key.Space) { spacePressed = true; e.Handled = true; }
        else base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.Key != Key.Space) { base.OnKeyUp(e); return; }
        var invoke = spacePressed;
        spacePressed = false;
        if (invoke) OnClick();
        e.Handled = true;
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        spacePressed = false;
        base.OnLostKeyboardFocus(e);
    }
}
