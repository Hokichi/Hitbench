using Hitbench.Test;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hitbench.Core.Entities;
using Hitbench.Services;
using Hitbench.ViewModels.Shell;
using Hitbench.Views.Pages;
namespace Hitbench.Test.Presentation;
[Apartment(ApartmentState.STA),NonParallelizable]
public class OHsSampleTrackNameTests
{
    [TestCase(1, false)][TestCase(2, true)]
    public void IdentityPointerActivationRequiresDoubleClick(int count, bool invokes)
    {
        var action = new Hitbench.Resources.CustomControls.OHsTrackIdentityButton();
        var calls = 0; action.Click += (_, _) => calls++;
        Assert.That(action.HandlePointerActivation(count), Is.EqualTo(invokes));
        Assert.That(calls, Is.EqualTo(invokes ? 1 : 0));
        action.IsEnabled = false;
        Assert.That(action.HandlePointerActivation(2), Is.False);
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void TrackUsesStyledTextContent(bool instrumental, bool collapsed)
    {
        new VolumeWheelTests().SetUp(); Track track=instrumental ? new InstrumentalTrack() : new Track();
        track.Name="Display"; track.IsCollapsed=collapsed;
        var vm=new WorkstationPageVM(new StubWorkstationSourceService{Load=(_,_)=>Task.FromResult(new WorkstationSource("map","song",new ProjectData{Tracks=[track],UninheritedPoints = TimingTestData.Points([0], [120])}))},new MarkerPlaybackCoordinator(new StubWorkstationAudioService(),Task.Delay),new ImmediateUiScheduler());
        try{
            UiTestPump.Run(vm.LoadAsync("project.json",default));vm.AcknowledgePresentationReady(vm.LoadGeneration);
            var row=new OHsWorkstationTrackRow{Track=track,ViewModel=vm};UiTestPump.Layout(row,900,300);
            var action = FindAction(row)!;
            Assert.That(System.Windows.Automation.AutomationProperties.GetName(action), Is.EqualTo("Change track name and color"));
            UiControlContract.AssertStyle<Hitbench.Resources.CustomControls.OHsTrackIdentityButton>(action, "OHsWorkstationTrackIdentityActionStyle");
            Assert.That(action.Style.BasedOn, Is.SameAs(action.FindResource("FolderActionButtonStyle")));
            var text=Find(row);Assert.That(text,Is.Not.Null);
            Assert.That(text!.Style,Is.SameAs(row.FindResource("OHsWorkstationSampleTrackNameStyle")));
            Assert.That(text.Text,Is.EqualTo("Display"));

        }finally{UiTestPump.Run(vm.DeactivateAsync());}
    }
    private static Hitbench.Resources.CustomControls.OHsTrackIdentityButton? FindAction(DependencyObject root)
    {
        if (root is Hitbench.Resources.CustomControls.OHsTrackIdentityButton action) return action;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindAction(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        return null;
    }
    private static TextBlock? Find(DependencyObject root)
    {
        if(root is TextBlock {Name:"SampleTrackName"} text)return text;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)if(Find(VisualTreeHelper.GetChild(root,i)) is {} found)return found;
        return null;
    }
}
