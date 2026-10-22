using System.Windows;

namespace Soundie.Resources.CustomControls;

public class BalloonButton : BalloonControl
{
    static BalloonButton()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(BalloonButton),
            new FrameworkPropertyMetadata(typeof(BalloonButton)));
    }
}


