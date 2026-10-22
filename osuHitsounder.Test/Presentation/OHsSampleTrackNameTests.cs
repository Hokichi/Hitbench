using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using osuHitsounder.Core.Entities;
using osuHitsounder.Services;
using osuHitsounder.ViewModels.Shell;
using osuHitsounder.Views.Pages;
namespace osuHitsounder.Test.Presentation;
[Apartment(ApartmentState.STA),NonParallelizable]
public class OHsSampleTrackNameTests
{
    [Test] public void RegularTrackUsesStyledTextContent()
    {
        new VolumeWheelTests().SetUp(); var track=new Track{Name="Display"};
        var vm=new WorkstationPageVM(new StubWorkstationSourceService{Load=(_,_)=>Task.FromResult(new WorkstationSource("map","song",new ProjectData{Tracks=[track],Offsets=[0],BPMs=[120]}))},new MarkerPlaybackCoordinator(new StubWorkstationAudioService(),Task.Delay),new ImmediateUiScheduler());
        try{
            UiTestPump.Run(vm.LoadAsync("project.json",default));vm.AcknowledgePresentationReady(vm.LoadGeneration);
            var row=new OHsWorkstationTrackRow{Track=track,ViewModel=vm};UiTestPump.Layout(row,900,300);
            var text=Find(row);Assert.That(text,Is.Not.Null);
            Assert.That(text!.Style,Is.SameAs(row.FindResource("OHsWorkstationSampleTrackNameStyle")));
            Assert.That(text.Text,Is.EqualTo("Display"));
        }finally{UiTestPump.Run(vm.DeactivateAsync());}
    }
    private static TextBlock? Find(DependencyObject root)
    {
        if(root is TextBlock {Name:"SampleTrackName"} text)return text;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)if(Find(VisualTreeHelper.GetChild(root,i)) is {} found)return found;
        return null;
    }
}
