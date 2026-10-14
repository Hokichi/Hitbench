using System.Windows.Input;
using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.Services;
using osuHitsounder.ViewModels.Popups;
using osuHitsounder.Views.Popups;
namespace osuHitsounder.Test.Presentation;
[Apartment(ApartmentState.STA),NonParallelizable]
public class InstrumentalArrangementShortcutTests
{
    [Test]
    public void WheelAccumulatesZoomAndDoesNotLeakModifierRemainder()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var ctx=new InstrumentalTrackContext("project.json",null,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid());
        var snap=new InstrumentalTrackSnapshot(ctx,"Piano",100,new Dictionary<string,string>(),[],null,0);
        var vm=new InstrumentalArrangementPopupVM(new(snap,"song",new(TimeSpan.FromSeconds(10),[.5f]),[0],[120],TimeSpan.Zero,4,32,100,100,100),new MarkerPlaybackCoordinator(new StubWorkstationAudioService(),Task.Delay),new WeakReferenceMessenger(),new ImmediateUiScheduler());
        var popup=new OHsInstrumentalArrangementPopup(vm);var roll=(PianoRollView)popup.FindName("PianoRoll");
        try{
            popup.HandleArrangementWheel(ModifierKeys.Control,30,roll);Assert.That(vm.ZoomFactor,Is.EqualTo(32));popup.HandleArrangementWheel(ModifierKeys.Control,90,roll);Assert.That(vm.ZoomFactor,Is.EqualTo(33));popup.HandleArrangementWheel(ModifierKeys.Alt,30,roll);popup.HandleArrangementWheel(ModifierKeys.Control,90,roll);Assert.That(vm.ZoomFactor,Is.EqualTo(33));
            roll.Measure(new System.Windows.Size(400,150));roll.Arrange(new System.Windows.Rect(0,0,400,150));roll.VerticalOffset=70.5*36;
            popup.HandleArrangementWheel(ModifierKeys.Control|ModifierKeys.Shift,120,roll);Assert.That(vm.LaneHeight,Is.EqualTo(42));Assert.That(roll.VerticalOffset/roll.LaneHeight,Is.EqualTo(70.5).Within(.0001));
            popup.HandleEditorShortcut(Key.NumPad8,ModifierKeys.Control,false,roll);Assert.That(vm.Subdivision,Is.EqualTo(8));
            Assert.That(popup.HandleEditorShortcut(Key.Z,ModifierKeys.Control,false,new System.Windows.Controls.TextBox()),Is.False);
            UiControlContract.AssertStyle<System.Windows.Controls.RadioButton>((System.Windows.Controls.RadioButton)popup.FindName("SelectionModeButton"),"OHsTimelineEditingModeButtonStyle");
        }
        finally{popup.Close();UiTestPump.Run(vm.DeactivateAsync());}
    }
}
