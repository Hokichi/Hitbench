using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hitbench.Core.Entities;
using Hitbench.Services;
using Hitbench.ViewModels.Shell;
using Hitbench.Views.Pages;
using Hitbench.Resources.CustomControls;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA)] [NonParallelizable]
public sealed class WorkstationMissingSampleIntegrationTests
{
    [Test] public void MissingGridRetainsSelectorAndRecoversWhenReassigned()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var track = new Track { SelectedSampleFileName = "absent.wav", Samples = [new() { FileName = "absent.wav" }] };
        var vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav", new ProjectData { Tracks = [track] })) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), new StubSampleFileService(["hit.wav"]), new StubSampleFileDialogService(Array.Empty<string>()));
        var page = new WorkstationPage(vm); page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 1100, Height = 900 });
        try
        {
            UiTestPump.Run(page.LoadCompletion); view.UpdateLayout();
            var grid = Descendants(page).OfType<BeatGridView>().Single();
            var selector = Descendants(page).OfType<ComboBox>().Single(c => c.Name == "TrackSampleFileSelector");
            Assert.That(grid.IsEnabled, Is.False); Assert.That(selector.IsEnabled, Is.True);
            selector.SelectedItem = "hit.wav"; view.UpdateLayout();
            Assert.That(grid.IsEnabled, Is.True); Assert.That(track.Samples.Single().FileName, Is.EqualTo("hit.wav"));
        }
        finally { UiTestPump.Run(page.DeactivateAsync()); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { yield return root; for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) foreach(var child in Descendants(VisualTreeHelper.GetChild(root,i))) yield return child; }
}
