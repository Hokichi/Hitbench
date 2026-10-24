using System.Windows.Input;
using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.DataModels;
using Hitbench.Resources.CustomControls;
using Hitbench.Services;
using Hitbench.ViewModels.Popups;
using Hitbench.Views.Popups;
namespace Hitbench.Test.Presentation;
[Apartment(ApartmentState.STA),NonParallelizable]
public class InstrumentalArrangementShortcutTests
{
    [Test]
    public void WheelAccumulatesZoomAndDoesNotLeakModifierRemainder()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var ctx=new InstrumentalTrackContext("project.json",null,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid());
        var snap=new InstrumentalTrackSnapshot(ctx,"Piano",100,new Dictionary<string,string>(),[],null,0);
        var messenger=new WeakReferenceMessenger(); var requests=new List<Hitbench.Resources.Messages.OHsInstrumentalTimelineEditRequestedMessage>(); messenger.Register<Hitbench.Resources.Messages.OHsInstrumentalTimelineEditRequestedMessage>(requests,(_,m)=>requests.Add(m));
        var vm=new InstrumentalArrangementDrawerVM(InstrumentalArrangementTestContext.Create(snap,[0],[120],TimeSpan.Zero,4,32),messenger,new ImmediateUiScheduler());
        var popup=new OHsInstrumentalArrangementDrawer(vm);var roll=(PianoRollView)popup.FindName("PianoRoll");
        try{
            popup.HandleArrangementWheel(ModifierKeys.Control,30,roll);Assert.That(vm.ZoomFactor,Is.EqualTo(32));popup.HandleArrangementWheel(ModifierKeys.Control,90,roll);Assert.That(requests.Last().ZoomFactor,Is.EqualTo(33));popup.HandleArrangementWheel(ModifierKeys.Alt,30,roll);popup.HandleArrangementWheel(ModifierKeys.Control,90,roll);Assert.That(requests.Last().ZoomFactor,Is.EqualTo(33));
            roll.Measure(new System.Windows.Size(400,150));roll.Arrange(new System.Windows.Rect(0,0,400,150));roll.VerticalOffset=70.5*36;
            popup.HandleArrangementWheel(ModifierKeys.Control|ModifierKeys.Shift,120,roll);Assert.That(vm.LaneHeight,Is.EqualTo(42));Assert.That(roll.VerticalOffset/roll.LaneHeight,Is.EqualTo(70.5).Within(.0001));
            popup.HandleEditorShortcut(Key.NumPad8,ModifierKeys.Control,false,roll);Assert.That(requests.Last().SelectedSubdivisionIndex,Is.EqualTo(6));
            Assert.That(popup.HandleEditorShortcut(Key.Z,ModifierKeys.Control,false,new System.Windows.Controls.TextBox()),Is.False);
            UiControlContract.AssertStyle<PianoRollView>(roll,"BasePianoRollViewStyle");
        }
        finally{popup.Detach();UiTestPump.Run(vm.DeactivateAsync());}
    }
}
