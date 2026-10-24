using System.Windows;
namespace Onset.Views.Pages;
public partial class WorkstationPage
{
    public static readonly DependencyProperty IsAudioBusyProperty = DependencyProperty.Register(nameof(IsAudioBusy),typeof(bool),typeof(WorkstationPage),new PropertyMetadata(false,(d,_)=>((WorkstationPage)d).RefreshLoadingPresentation()));
    public bool IsAudioBusy {get=>(bool)GetValue(IsAudioBusyProperty);set=>SetValue(IsAudioBusyProperty,value);}
    public static readonly DependencyProperty AudioBusyTextProperty=DependencyProperty.Register(nameof(AudioBusyText),typeof(string),typeof(WorkstationPage),new PropertyMetadata("",(d,_)=>((WorkstationPage)d).RefreshLoadingPresentation()));
    public string AudioBusyText {get=>(string)GetValue(AudioBusyTextProperty);set=>SetValue(AudioBusyTextProperty,value);}
    private static readonly DependencyPropertyKey LoadingStatusTextPropertyKey=DependencyProperty.RegisterReadOnly(nameof(LoadingStatusText),typeof(string),typeof(WorkstationPage),new PropertyMetadata(""));
    public static readonly DependencyProperty LoadingStatusTextProperty=LoadingStatusTextPropertyKey.DependencyProperty;
    public string LoadingStatusText=>(string)GetValue(LoadingStatusTextProperty);
    public void AnnounceLoadingStatus()
    {
        LoadingStatus.GetBindingExpression(System.Windows.Controls.TextBlock.TextProperty)?.UpdateTarget();
        System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(LoadingStatus)?.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
    }
    private static readonly DependencyPropertyKey IsLoadingPresentationActivePropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsLoadingPresentationActive), typeof(bool), typeof(WorkstationPage), new PropertyMetadata(false));
    public static readonly DependencyProperty IsLoadingPresentationActiveProperty = IsLoadingPresentationActivePropertyKey.DependencyProperty;
    public bool IsLoadingPresentationActive => (bool)GetValue(IsLoadingPresentationActiveProperty);
    private void RefreshLoadingPresentation()
    {
        SetValue(IsLoadingPresentationActivePropertyKey, !deactivationStarted && (IsAudioBusy || viewModel.ShowStatusOverlay));
        SetValue(LoadingStatusTextPropertyKey,deactivationStarted?"":IsAudioBusy?AudioBusyText:viewModel.ShowStatusOverlay?viewModel.StatusText:"");
        RefreshBlurPresentation();
    }
    private void RefreshBlurPresentation()
    {
        System.Windows.Media.Effects.Effect? effect = null;
        if (!deactivationStarted)
            effect = IsLoadingPresentationActive
                ? SystemParameters.HighContrast ? null : (System.Windows.Media.Effects.Effect)FindResource("OHsWorkstationLoadingBlurEffect")
                : IsVolumeOverlayOpen && volumeBlurRadius > 0 ? volumeBlur : null;
        WorkstationContent.Effect = effect;
        SetValue(HeaderBlurEffectPropertyKey, effect);
    }
    private void OnLoadingSystemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SystemParameters.HighContrast) || deactivationStarted) return;
        if (Dispatcher.CheckAccess()) RefreshBlurPresentation();
        else Dispatcher.BeginInvoke(new Action(() => { if (!deactivationStarted) RefreshBlurPresentation(); }));
    }
}
