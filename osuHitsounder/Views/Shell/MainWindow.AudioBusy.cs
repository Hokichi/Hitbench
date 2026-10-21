using System.Windows;
using System.Windows.Input;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Resources.Messages;

namespace osuHitsounder;

public partial class MainWindow : IRecipient<OHsAudioBusyChangedMessage>
{
    private readonly Dictionary<Guid, string> audioOperations = [];
    private IInputElement? audioPriorFocus;
    private static readonly DependencyPropertyKey IsAudioBusyPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsAudioBusy), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));
    public static readonly DependencyProperty IsAudioBusyProperty = IsAudioBusyPropertyKey.DependencyProperty;
    public bool IsAudioBusy => (bool)GetValue(IsAudioBusyProperty);
    private static readonly DependencyPropertyKey AudioBusyTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(AudioBusyText), typeof(string), typeof(MainWindow), new PropertyMetadata(""));
    public static readonly DependencyProperty AudioBusyTextProperty = AudioBusyTextPropertyKey.DependencyProperty;
    public string AudioBusyText => (string)GetValue(AudioBusyTextProperty);

    public void Receive(OHsAudioBusyChangedMessage message)
    {
        if (closed) return;
        VerifyAccess(); // The operation coordinator awaits the injected UI scheduler.
        if (message.IsBusy)
        {
            if (!IsAudioBusy) { audioPriorFocus = Keyboard.FocusedElement; Keyboard.ClearFocus(); CancelBackgroundDrag(); }
            audioOperations[message.OperationId] = message.Phase;
        }
        else audioOperations.Remove(message.OperationId);
        SetValue(IsAudioBusyPropertyKey, audioOperations.Count > 0);
        SetValue(AudioBusyTextPropertyKey, audioOperations.Values.LastOrDefault() ?? "");
        if (IsAudioBusy)
            UIElementAutomationPeer.CreatePeerForElement((UIElement)AudioBusyToast.Child)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        if (!IsAudioBusy)
        {
            if (!IsPopupOverlayVisible && audioPriorFocus is FrameworkElement { IsVisible: true, IsEnabled: true } target && IsAncestorOf(target))
                target.Focus();
            audioPriorFocus = null;
        }
    }

    private void ClearAudioBusy()
    {
        audioOperations.Clear();
        audioPriorFocus = null;
        SetValue(IsAudioBusyPropertyKey, false);
        SetValue(AudioBusyTextPropertyKey, "");
    }
}
