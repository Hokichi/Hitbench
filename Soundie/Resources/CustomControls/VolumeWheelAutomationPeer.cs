using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;

namespace Soundie.Resources.CustomControls;

public sealed class VolumeWheelAutomationPeer(VolumeWheel owner) : SliderAutomationPeer(owner), IRangeValueProvider
{
    bool IRangeValueProvider.IsReadOnly => !owner.IsActive || !owner.IsEnabled;
    double IRangeValueProvider.LargeChange => owner.LargeChange;
    double IRangeValueProvider.Maximum => owner.Maximum;
    double IRangeValueProvider.Minimum => owner.Minimum;
    double IRangeValueProvider.SmallChange => owner.SmallChange;
    double IRangeValueProvider.Value => owner.Value;

    void IRangeValueProvider.SetValue(double value)
    {
        if (owner.IsActive && owner.IsEnabled) owner.SetCurrentValue(System.Windows.Controls.Primitives.RangeBase.ValueProperty, value);
    }
}
