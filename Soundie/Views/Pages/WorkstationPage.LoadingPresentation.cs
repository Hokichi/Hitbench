using System.Windows;
namespace Soundie.Views.Pages;
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
    private void RefreshLoadingPresentation()=>SetValue(LoadingStatusTextPropertyKey,deactivationStarted?"":IsAudioBusy?AudioBusyText:viewModel.ShowStatusOverlay?viewModel.StatusText:"");
}
