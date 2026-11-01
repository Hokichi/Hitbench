using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;

namespace Hitbench.Resources.CustomControls;

public sealed class BalloonMenuOptionAutomationPeer(BalloonMenuOption owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider
{
    protected override string GetClassNameCore() => nameof(BalloonMenuOption);
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
    protected override string GetNameCore() => string.IsNullOrEmpty(base.GetNameCore()) ? owner.ButtonText ?? string.Empty : base.GetNameCore();
    public override object? GetPattern(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.Invoke ? this : base.GetPattern(patternInterface);

    public void Invoke()
    {
        if (!IsEnabled()) throw new ElementNotEnabledException();
        var menu = owner.MenuOwner;
        if (menu is null || !menu.CanInvokeOption(owner)) return;
        var generation = menu.PresentationGeneration;
        owner.Dispatcher.BeginInvoke(() =>
        {
            if (ReferenceEquals(owner.MenuOwner, menu) && generation == menu.PresentationGeneration && menu.CanInvokeOption(owner))
                owner.InvokeAction();
        });
    }
}
