using System.Windows;
using System.Windows.Controls;
using osuHitsounder.Core.Entities;
using osuHitsounder.ViewModels.Shell;
using osuHitsounder.Views.Pages;
namespace osuHitsounder.Test.Presentation;
[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class OHsWorkstationTrackListTests
{
    [Test]
    public void GrowingCollectionStaysVirtualizedAndSelectionUsesLiveTrack()
    {
        new VolumeWheelTests().SetUp();
        var vm = (WorkstationPageVM)WorkstationTestPage.Create().DataContext;
        for (var i = 0; i < 100; i++) vm.Tracks.Add(new Track { Name = $"Track {i}" });
        var owner = new OHsWorkstationTrackList { DataContext = vm };
        using var view = new InMemoryView(new Window { Content = owner, Width = 900, Height = 300 });
        var list = (ListBox)owner.FindName("TrackList");
        list.SelectedItem = vm.Tracks[1];
        Assert.That(vm.SelectedTrack, Is.SameAs(vm.Tracks[1]));
        var realized = Enumerable.Range(0, vm.Tracks.Count).Count(i => list.ItemContainerGenerator.ContainerFromIndex(i) is not null);
        Assert.That(realized, Is.GreaterThan(0).And.LessThan(vm.Tracks.Count));
        owner.Deactivate();
        UiTestPump.Run(vm.DeactivateAsync());
    }
}
