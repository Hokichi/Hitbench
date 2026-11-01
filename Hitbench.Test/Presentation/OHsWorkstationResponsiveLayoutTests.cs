using System.Windows;
using System.Windows.Data;
using Hitbench.Core.Entities;
using Hitbench.ViewModels.Shell;
using Hitbench.Views.Pages;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class OHsWorkstationResponsiveLayoutTests
{
    [TestCase(700, 240)][TestCase(200, 240)][TestCase(700, 100)]
    public void SuppliedSideColumnDeterminesTimelineViewport(double width, double side)
    {
        new VolumeWheelTests().SetUp();
        var vm = (WorkstationPageVM)WorkstationTestPage.Create().DataContext;
        vm.Tracks.Add(new Track { Name = "Track" });
        var list = new OHsWorkstationTrackList { DataContext = vm, TrackSideColumnWidth = new GridLength(side) };
        using var view = new InMemoryView(new Window { Content = list, Width = width, Height = 300 });
        try
        {
            var scroll = Descendants(list).OfType<System.Windows.Controls.ScrollViewer>().First();
            Assert.That(list.TrackViewportWidth, Is.EqualTo(Math.Max(0, scroll.ViewportWidth - side - 12)));
            foreach (var row in Descendants(list).OfType<OHsWorkstationTrackRow>())
                Assert.That(row.TimelineViewportWidth, Is.EqualTo(list.TrackViewportWidth));
        }
        finally { list.Deactivate(); UiTestPump.Run(vm.DeactivateAsync()); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(System.Windows.Media.VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    [Test]
    public void ResizeChangesPresentationModeWithoutReplacingCommandsOrSelection()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var page = WorkstationTestPage.Create();
        var vm = (WorkstationPageVM)page.DataContext;
        var track = new Track { Name = "音楽 العربية ภาษาไทย" };
        vm.Tracks.Add(track);
        vm.SelectedTrack = track;
        var play = vm.PlayPauseCommand;
        var audio = (OHsWorkstationAudioCard)page.FindName("AudioCard");
        var list = (OHsWorkstationTrackList)page.FindName("TrackListComponent");
        try
        {
            foreach (var width in new[] { 1280d, 959d, 960d, 640d, 1280d })
            {
                UiTestPump.Layout(page, width, 700);
                Assert.That(page.IsCompactLayout, Is.EqualTo(width < 960));
                Assert.That(audio.IsCompactLayout, Is.EqualTo(page.IsCompactLayout));
                Assert.That(BindingOperations.IsDataBound(audio, OHsWorkstationAudioCard.TrackSideColumnWidthProperty), Is.True);
                Assert.That(BindingOperations.IsDataBound(list, OHsWorkstationTrackList.TrackSideColumnWidthProperty), Is.True);
                Assert.That(vm.SelectedTrack, Is.SameAs(track));
                Assert.That(vm.PlayPauseCommand, Is.SameAs(play));
            }
            UiTestPump.Layout(page, 640, 360);
            Assert.That(page.IsShortLayout, Is.True);
            Assert.That(audio.IsShortLayout, Is.True);
            UiTestPump.Layout(page, 1280, 720);
            Assert.That(page.IsShortLayout, Is.False);
        }
        finally { UiTestPump.Run(page.DeactivateAsync()); }
    }
}
