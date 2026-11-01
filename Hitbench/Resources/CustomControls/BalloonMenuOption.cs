using System.Windows;

namespace Hitbench.Resources.CustomControls;

public class BalloonMenuOption : BalloonButton
{
    private BalloonMenu? menuOwner;
    internal BalloonMenu? MenuOwner
    {
        get => menuOwner;
        set { menuOwner = value; CoerceValue(IsHitTestVisibleProperty); }
    }
    static BalloonMenuOption()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(BalloonMenuOption), new FrameworkPropertyMetadata(typeof(BalloonMenuOption)));
        ShouldShowTextProperty.OverrideMetadata(typeof(BalloonMenuOption), new FrameworkPropertyMetadata(false, null, (_, _) => false));
        IsHitTestVisibleProperty.OverrideMetadata(typeof(BalloonMenuOption), new UIPropertyMetadata(true, null,
            (d, value) => (bool)value && ((BalloonMenuOption)d).MenuOwner?.IsOptionRevealed((BalloonMenuOption)d) == true));
    }
}
