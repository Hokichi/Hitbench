using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;

namespace osuHitsounder.Resources.CustomControls;

public sealed class BeatGridViewAutomationPeer(BeatGridView owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider
{
    protected override string GetClassNameCore() => nameof(BeatGridView);
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Custom;
    protected override bool IsEnabledCore() => base.IsEnabledCore() && ((BeatGridView)Owner).CanPlaceSamples;
    protected override string GetItemStatusCore()
    {
        var grid = (BeatGridView)Owner;
        return $"{grid.Samples?.Count(sample => grid.SelectedSamples?.Contains(sample) == true) ?? 0} samples selected";
    }

    public override object? GetPattern(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.Invoke ? this : base.GetPattern(patternInterface);

    public void Invoke()
    {
        if (!IsEnabled()) throw new ElementNotEnabledException();
        ((BeatGridView)Owner).Dispatcher.BeginInvoke(((BeatGridView)Owner).RequestSamplePlacement);
    }
}
