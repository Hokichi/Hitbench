using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;

namespace osuHitsounder.Resources.CustomControls;

public sealed class VolumeWheelAutomationPeer(VolumeWheel owner) : SliderAutomationPeer(owner), IRangeValueProvider
{
    void IRangeValueProvider.SetValue(double value)
    {
        if (owner.IsActive && owner.IsEnabled) owner.SetCurrentValue(System.Windows.Controls.Primitives.RangeBase.ValueProperty, value);
    }
}
