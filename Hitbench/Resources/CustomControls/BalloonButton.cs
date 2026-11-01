using System.Windows;
using System.Windows.Automation.Peers;

namespace Hitbench.Resources.CustomControls;

public class BalloonButton : BalloonControl
{
    protected override AutomationPeer OnCreateAutomationPeer() => new BalloonButtonAutomationPeer(this);

    internal void InvokeAction()
    {
        if (IsEnabled) OnClick();
    }

    static BalloonButton()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(BalloonButton),
            new FrameworkPropertyMetadata(typeof(BalloonButton)));
    }
}


