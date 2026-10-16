using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;

namespace osuHitsounder.Resources.CustomControls;

public sealed class BalloonComboBoxAutomationPeer(BalloonComboBox owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider
{
    protected override string GetClassNameCore() => nameof(BalloonComboBox);
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
    protected override string GetNameCore()
    {
        var name = base.GetNameCore();
        return string.IsNullOrEmpty(name) ? owner.ButtonText ?? string.Empty : name;
    }

    public override object? GetPattern(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.Invoke ? this : base.GetPattern(patternInterface);

    public void Invoke()
    {
        if (!IsEnabled()) throw new ElementNotEnabledException();
        var version = owner.MenuOwnerVersion;
        owner.Dispatcher.BeginInvoke(() =>
        {
            if (owner.MenuOwnerVersion == version) owner.InvokeActionMenu();
        });
    }
}
