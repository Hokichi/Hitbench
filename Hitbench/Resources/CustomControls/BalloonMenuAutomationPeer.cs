using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;

namespace Hitbench.Resources.CustomControls;

public sealed class BalloonMenuAutomationPeer(BalloonMenu owner) : FrameworkElementAutomationPeer(owner), IExpandCollapseProvider, IInvokeProvider
{
    protected override string GetClassNameCore() => nameof(BalloonMenu);
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
    protected override string GetNameCore() => string.IsNullOrEmpty(base.GetNameCore()) ? owner.ButtonText ?? string.Empty : base.GetNameCore();
    public ExpandCollapseState ExpandCollapseState => owner.IsExpanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
    public override object? GetPattern(PatternInterface patternInterface) =>
        patternInterface is PatternInterface.Invoke or PatternInterface.ExpandCollapse ? this : base.GetPattern(patternInterface);
    protected override List<AutomationPeer>? GetChildrenCore() => owner.IsExpanded ?
        owner.Options.Select(option => (AutomationPeer)UIElementAutomationPeer.CreatePeerForElement(option)).ToList() : null;

    public void Invoke() => Expand();
    public void Expand() => Queue(expand: true);
    public void Collapse() => Queue(expand: false);
    private void Queue(bool expand)
    {
        if (!IsEnabled()) throw new ElementNotEnabledException();
        var generation = owner.PresentationGeneration;
        owner.Dispatcher.BeginInvoke(() =>
        {
            if (generation != owner.PresentationGeneration || !owner.IsEnabled) return;
            if (expand) owner.OpenMenu(true);
            else owner.CloseMenu(true);
        });
    }

    internal void NotifyExpandedChanged(bool oldValue, bool newValue)
    {
        InvalidatePeer();
        if (ListenerExists(AutomationEvents.PropertyChanged))
            RaisePropertyChangedEvent(ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
                oldValue ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed,
                newValue ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed);
    }
}
