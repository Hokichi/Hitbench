using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hitbench.Resources.CustomControls;
using Hitbench.Core.Entities;
using Hitbench.Resources.DataModels;
using Hitbench.Views.Pages;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class OHsDefaultTrackIntegrationTests
{
    [Test]
    public void MissingDefaultStaysSelectedAndDisabledWhileConfigurationWorks()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var track = new Track { UsesDefaultSample = true };
        var project = new ProjectData { Offsets = [0], BPMs = [120], Tracks = [track] };
        var vm = WorkstationSkinDefaultsTests.Create(project, new());
        UiTestPump.Run(vm.LoadAsync("project.json", default)); vm.AcknowledgePresentationReady(vm.LoadGeneration);
        try
        {
            var row = new OHsWorkstationTrackRow { Track = track, ViewModel = vm, PresentSampleConfiguration = (_, _) => { } };
            UiTestPump.Layout(row, 900, 200);
            Assert.That(vm.CanEditTracks, Is.True);
            Assert.That(row.CanOperateTrack, Is.False);
            Assert.That(row.OpenSampleConfiguration(), Is.True);
            var selector = (ComboBox)row.FindName("TrackSampleFileSelector");
            Assert.That(((OHsTrackSampleChoice)selector.Items[0]).IsDefault, Is.True);
            Assert.That(selector.SelectedIndex, Is.Zero);
            Assert.That(row.IsDefaultUnavailable, Is.True);
            selector.SelectedItem = selector.Items.Cast<OHsTrackSampleChoice>().Single(c => c.FileName == "a.wav");
            Assert.That(track.UsesDefaultSample, Is.False);
            row.CloseSampleConfiguration(false);
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T match) yield return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
}
