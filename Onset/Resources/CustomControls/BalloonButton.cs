using System.Windows;

namespace Onset.Resources.CustomControls;

public class BalloonButton : BalloonControl
{
    static BalloonButton()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(BalloonButton),
            new FrameworkPropertyMetadata(typeof(BalloonButton)));
    }
}


