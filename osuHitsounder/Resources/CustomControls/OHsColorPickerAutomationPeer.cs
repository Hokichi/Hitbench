using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using osuHitsounder.Core.Entities;
namespace osuHitsounder.Resources.CustomControls;
public sealed class OHsColorPickerAutomationPeer(OHsColorPicker owner) : FrameworkElementAutomationPeer(owner), IValueProvider
{
    protected override string GetClassNameCore()=>nameof(OHsColorPicker);
    protected override AutomationControlType GetAutomationControlTypeCore()=>AutomationControlType.Custom;
    public override object? GetPattern(PatternInterface pattern)=>pattern==PatternInterface.Value?this:base.GetPattern(pattern);
    public bool IsReadOnly=>!owner.IsEnabled;
    public string Value=>owner.SelectedColor.ToUpperInvariant();
    public void SetValue(string value)
    {
        if(!owner.IsEnabled)throw new ElementNotEnabledException();
        if(!RgbColor.TryParse(value,out var rgb))throw new ArgumentException("Expected #RRGGBB.",nameof(value));
        owner.SetCurrentValue(OHsColorPicker.SelectedColorProperty,rgb.ToHex());
    }
}
