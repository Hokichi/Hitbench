using System.Windows;
using System.Windows.Data;
using Onset.Core.Entities;
using Onset.ViewModels.Shell;
using Onset.Views.Pages;

namespace Onset.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class OHsWorkstationResponsiveLayoutTests
{
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
