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
    [TestCase(false)]
    [TestCase(true)]
    public void NameEditorCommitsCancelsAndRecycles(bool instrumental)
    {
        new VolumeWheelTests().SetUp();
        Track track = instrumental ? new InstrumentalTrack { Name = "Old" } : new Track { Name = "Old" };
        var loading = WorkstationSampleConfigurationTests.CreateAsync(track); UiTestPump.Run(loading);
        var vm = loading.GetAwaiter().GetResult();
        var row = new OHsWorkstationTrackRow { Track = track, ViewModel = vm };
        using var view = new InMemoryView(new Window { Content = row, Width = 900, Height = 300 });
        try
        {
            Assert.That(row.BeginNameEdit(), Is.True);
            var input = FindInput(row)!;
            UiControlContract.AssertStyle<TextBox>(input, "OHsWorkstationTrackNameEditorStyle");
            input.Text = "Kick";
            UiTestPump.Run(row.HandleTrackNameKeyAsync(System.Windows.Input.Key.Enter));
            Assert.That(track.Name, Is.EqualTo("Kick"));
            row.BeginNameEdit(); input.Text = "Snare";
            input.RaiseEvent(new System.Windows.Input.KeyboardFocusChangedEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, 0, input, null)
                { RoutedEvent = System.Windows.Input.Keyboard.LostKeyboardFocusEvent });
            Assert.That(track.Name, Is.EqualTo("Snare"));
            row.BeginNameEdit(); input.Text = "Discard";
            UiTestPump.Run(row.HandleTrackNameKeyAsync(System.Windows.Input.Key.Escape));
            input.RaiseEvent(new System.Windows.Input.KeyboardFocusChangedEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, 0, input, null)
                { RoutedEvent = System.Windows.Input.Keyboard.LostKeyboardFocusEvent });
            Assert.That(track.Name, Is.EqualTo("Snare"));
            row.BeginNameEdit(); input.Text = "Recycled"; row.Track = new Track { Name = "Other" };
            Assert.That(vm.RenamingTrack, Is.Null);
            Assert.That(track.Name, Is.EqualTo("Snare"));
            Assert.That(Find(row)!.Text, Is.EqualTo("Other"));
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); }
    }

    private static TextBox? FindInput(DependencyObject root)
    {
        if (root is TextBox { Name: "TrackNameInput" } input) return input;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindInput(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        return null;
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void TrackUsesStyledTextContent(bool instrumental, bool collapsed)
    {
        new VolumeWheelTests().SetUp(); Track track=instrumental ? new InstrumentalTrack() : new Track();
        track.Name="Display"; track.IsCollapsed=collapsed;
        var vm=new WorkstationPageVM(new StubWorkstationSourceService{Load=(_,_)=>Task.FromResult(new WorkstationSource("map","song",new ProjectData{Tracks=[track],Offsets=[0],BPMs=[120]}))},new MarkerPlaybackCoordinator(new StubWorkstationAudioService(),Task.Delay),new ImmediateUiScheduler());
        try{
            UiTestPump.Run(vm.LoadAsync("project.json",default));vm.AcknowledgePresentationReady(vm.LoadGeneration);
            var row=new OHsWorkstationTrackRow{Track=track,ViewModel=vm};UiTestPump.Layout(row,900,300);
            var text=Find(row);Assert.That(text,Is.Not.Null);
            Assert.That(text!.Style,Is.SameAs(row.FindResource("OHsWorkstationSampleTrackNameStyle")));
            Assert.That(text.Text,Is.EqualTo("Display"));
            track.Color="#123456";row.RefreshPresentation();
            Assert.That(((SolidColorBrush)row.TrackBrush!).Color,Is.EqualTo(Color.FromRgb(0x12,0x34,0x56)));
            track.Color=null;row.RefreshPresentation();
            Assert.That(row.TrackBrush,Is.SameAs(row.FindResource("Brush.Track.Default")));
            row.Track=new InstrumentalTrack{Color="#ABCDEF"};row.RefreshPresentation();
            Assert.That(row.TrackBrush,Is.Not.SameAs(row.FindResource("Brush.Track.Default")));
            row.Track.Color=null;row.RefreshPresentation();
            Assert.That(row.TrackBrush,Is.SameAs(row.FindResource("Brush.Track.Default")));
        }finally{UiTestPump.Run(vm.DeactivateAsync());}
    }
    private static TextBlock? Find(DependencyObject root)
    {
        if(root is TextBlock {Name:"SampleTrackName"} text)return text;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)if(Find(VisualTreeHelper.GetChild(root,i)) is {} found)return found;
        return null;
    }
}
