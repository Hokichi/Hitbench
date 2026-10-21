using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.Services;
using osuHitsounder.ViewModels.Shell;
using osuHitsounder.Views.Pages;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class WorkstationTrackVolumeInputTests
{
    [SetUp]
    public void Setup() => new VolumeWheelTests().SetUp();

    [TestCase(FlowDirection.LeftToRight)]
    [TestCase(FlowDirection.RightToLeft)]
    public void NativeTrackMappingImmediatelyUpdatesVolumeWithoutChangingSubdivision(FlowDirection direction)
    {
        var track = new osuHitsounder.Core.Entities.Track { Volume = 50 };
        var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(
            new WorkstationSource("map.osu", "audio.wav", new ProjectData { Offsets = [0], BPMs = [120], Tracks = [track] })) };
        var vm = new WorkstationPageVM(source,
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm) { FlowDirection = direction };
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 1200, Height = 700 });
        UiTestPump.Run(page.LoadCompletion);
        view.UpdateLayout();
        var slider = Sliders(page).Single(control => ReferenceEquals(control.DataContext, track));
        Assert.That(slider.IsMoveToPointEnabled, Is.True);
        Assert.That(Sliders(page).Where(control => !ReferenceEquals(control, slider))
            .All(control => !control.IsMoveToPointEnabled), Is.True);
        var nativeTrack = (System.Windows.Controls.Primitives.Track)slider.Template.FindName("PART_Track", slider);
        var usableWidth = nativeTrack.ActualWidth - nativeTrack.Thumb.ActualWidth;
        foreach (var fraction in new[] { 0d, 0.75d, 1d })
        {
            var mapped = nativeTrack.ValueFromPoint(new Point(nativeTrack.Thumb.ActualWidth / 2 + usableWidth * fraction,
                nativeTrack.ActualHeight / 2));
            Assert.That(mapped, Is.EqualTo(5 + 195 * fraction).Within(0.01));
            var snapped = Math.Round(mapped, MidpointRounding.AwayFromZero);
            slider.SetCurrentValue(RangeBase.ValueProperty, snapped);
            Assert.That(track.Volume, Is.EqualTo(snapped));
            Assert.That(slider.Value, Is.EqualTo(snapped));
            view.UpdateLayout();
        }
    }

    private static IEnumerable<ValueSlider> Sliders(DependencyObject parent)
    {
        if (parent is ValueSlider slider) yield return slider;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Sliders(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
}
