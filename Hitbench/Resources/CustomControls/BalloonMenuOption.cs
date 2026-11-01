using System.Windows;

namespace Hitbench.Resources.CustomControls;

public class BalloonMenuOption : BalloonButton
{
    static BalloonMenuOption()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(BalloonMenuOption), new FrameworkPropertyMetadata(typeof(BalloonMenuOption)));
        ShouldShowTextProperty.OverrideMetadata(typeof(BalloonMenuOption), new FrameworkPropertyMetadata(false, null, (_, _) => false));
    }
}
