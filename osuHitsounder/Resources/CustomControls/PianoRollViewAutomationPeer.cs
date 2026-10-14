using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
namespace osuHitsounder.Resources.CustomControls;

public sealed class PianoRollViewAutomationPeer(PianoRollView owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider
{
    protected override string GetClassNameCore() => nameof(PianoRollView);
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Custom;
    protected override string GetItemStatusCore() => string.Format(System.Globalization.CultureInfo.CurrentCulture, "Active cell: {0}; notes selected: {1}; mode: {2}", ((PianoRollView)Owner).ActiveCell, ((PianoRollView)Owner).SelectedNoteIds.Count, ((PianoRollView)Owner).IsAdditionMode ? "Addition" : "Selection");
    protected override bool IsEnabledCore() => base.IsEnabledCore() && ((PianoRollView)Owner).CanEdit;
    public override object? GetPattern(PatternInterface p) => p == PatternInterface.Invoke ? this : base.GetPattern(p);
    public void Invoke() { if (!IsEnabled() || !((PianoRollView)Owner).CanEdit) throw new ElementNotEnabledException(); ((PianoRollView)Owner).Dispatcher.BeginInvoke(() => ((PianoRollView)Owner).HandleEditorKey(Key.Enter, ModifierKeys.None, false)); }
}
