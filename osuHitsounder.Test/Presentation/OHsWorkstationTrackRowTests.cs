using System.Windows;
using System.Windows.Controls;
using osuHitsounder.Core.Entities;
using osuHitsounder.ViewModels.Shell;
using osuHitsounder.Views.Pages;
namespace osuHitsounder.Test.Presentation;
[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class OHsWorkstationTrackRowTests
{
    [Test]
    public void RebindingKeepsLiveTrackAndRestoresSampleChoice()
    {
        new VolumeWheelTests().SetUp();
        var vm = (WorkstationPageVM)WorkstationTestPage.Create().DataContext;
        vm.SampleFiles.Add("a.wav"); vm.SampleFiles.Add("b.wav");
        var a = new Track { Name = "A", SelectedSampleFileName = "a.wav" };
        var b = new Track { Name = "B", SelectedSampleFileName = "b.wav" };
        var row = new OHsWorkstationTrackRow { Track = a, ViewModel = vm };
        using var view = new InMemoryView(new Window { Content = row, Width = 900, Height = 300 });
        row.Track = b; view.UpdateLayout(); row.RefreshPresentation();
        Assert.That(row.Track, Is.SameAs(b));
        Assert.That(row.SampleTimeline!.DataContext, Is.SameAs(b));
        Assert.That(a.Name, Is.EqualTo("A"));
        row.Track = a; view.UpdateLayout(); row.RefreshPresentation();
        Assert.That(row.SampleTimeline!.DataContext, Is.SameAs(a));
        UiTestPump.Run(vm.DeactivateAsync());
    }
}
