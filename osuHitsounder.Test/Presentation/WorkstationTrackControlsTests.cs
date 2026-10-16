using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using osuHitsounder.Core.Entities;
using osuHitsounder.Services;
using osuHitsounder.ViewModels.Shell;
using osuHitsounder.Views.Pages;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class WorkstationTrackControlsTests
{
    [SetUp] public void Setup() => new VolumeWheelTests().SetUp();

    [TestCase(FlowDirection.LeftToRight, "Dark")]
    [TestCase(FlowDirection.LeftToRight, "Light")]
    [TestCase(FlowDirection.RightToLeft, "Dark")]
    [TestCase(FlowDirection.RightToLeft, "Light")]
    public void AggregateToggleClearsFlagsThroughAutomationAndFollowsBusyState(FlowDirection direction, string theme)
    {
        Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = new Uri($"/osuHitsounder;component/Resources/Themes/{theme}.xaml", UriKind.Relative) };
        var track = new osuHitsounder.Core.Entities.Track { IsTrackMuted = true, IsTrackSoloed = true };
        var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(
            new WorkstationSource("map.osu", "audio.wav", new ProjectData { Tracks = [track], Offsets = [0], BPMs = [120] })) };
        var vm = new WorkstationPageVM(source, new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm) { FlowDirection = direction }; page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 700 });
        UiTestPump.Run(page.LoadCompletion);
        var mute = (ToggleButton)((osuHitsounder.Views.Pages.OHsWorkstationAudioCard)page.FindName("AudioCard")).FindName("ClearTrackMutesToggle");
        var solo = (ToggleButton)((osuHitsounder.Views.Pages.OHsWorkstationAudioCard)page.FindName("AudioCard")).FindName("ClearTrackSolosToggle");
        UiControlContract.AssertStyle<ToggleButton>(mute, "OHsWorkstationTrackToggleStyle");
        Assert.That(mute.IsChecked, Is.True); Assert.That(mute.IsEnabled, Is.True);
        ((IToggleProvider)new ToggleButtonAutomationPeer(mute)).Toggle(); view.UpdateLayout();
        Assert.That(track.IsTrackMuted, Is.False); Assert.That(track.IsTrackSoloed, Is.True);
        Assert.That(mute.IsChecked, Is.False); Assert.That(mute.IsEnabled, Is.False);
        Assert.That(solo.IsChecked, Is.True);
        vm.IsReturning = true; view.UpdateLayout();
        Assert.That(solo.IsEnabled, Is.False); Assert.That(solo.IsChecked, Is.True);
        vm.IsReturning = false; view.UpdateLayout();
        Assert.That(solo.IsEnabled, Is.True);
        ((IToggleProvider)new ToggleButtonAutomationPeer(solo)).Toggle(); view.UpdateLayout();
        Assert.That(solo.IsChecked, Is.False); Assert.That(solo.IsEnabled, Is.False);
        UiTestPump.Run(page.DeactivateAsync());
    }
}


