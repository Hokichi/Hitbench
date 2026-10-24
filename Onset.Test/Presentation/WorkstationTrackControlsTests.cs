using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using Onset.Core.Entities;
using Onset.Services;
using Onset.ViewModels.Shell;
using Onset.Views.Pages;
using Track = Onset.Core.Entities.Track;

namespace Onset.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class WorkstationTrackControlsTests
{
    [SetUp] public void Setup() => new VolumeWheelTests().SetUp();

    [TestCase(true)]
    [TestCase(false)]
    public void MixedAndEmptyTrackStatesUseAggregateCommands(bool empty)
    {
        var tracks = empty ? new List<Track>() : new List<Track> { new() { IsTrackMuted = true }, new() };
        var vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(
            new WorkstationSource("map", "song", new ProjectData { Tracks = tracks, Offsets = [0], BPMs = [120] })) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler());
        try
        {
            UiTestPump.Run(vm.LoadAsync("project.json", default)); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            var card = new OHsWorkstationAudioCard { DataContext = vm };
            UiTestPump.Layout(card, 900, 300);
            var mute = (ToggleButton)card.FindName("ClearTrackMutesToggle");
            Assert.That(mute.IsChecked, Is.False);
            Assert.That(mute.IsEnabled, Is.EqualTo(!empty));
            if (empty) return;
            var revision = vm.TrackAuditionRevision;
            ((IToggleProvider)new ToggleButtonAutomationPeer(mute)).Toggle();
            Assert.That(tracks.All(track => track.IsTrackMuted), Is.True);
            Assert.That(vm.TrackAuditionRevision, Is.EqualTo(revision + 1));
            using var input = new InMemoryPresentationSource();
            mute.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, input, 0, System.Windows.Input.Key.Space)
                { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent });
            mute.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, input, 0, System.Windows.Input.Key.Space)
                { RoutedEvent = System.Windows.Input.Keyboard.KeyUpEvent });
            Assert.That(tracks.All(track => !track.IsTrackMuted), Is.True);
            Assert.That(vm.TrackAuditionRevision, Is.EqualTo(revision + 2));
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); }
    }

    [TestCase(FlowDirection.LeftToRight, "Dark")]
    [TestCase(FlowDirection.LeftToRight, "Light")]
    [TestCase(FlowDirection.RightToLeft, "Dark")]
    [TestCase(FlowDirection.RightToLeft, "Light")]
    public void AggregateActionsInvokeOnceAndFollowAuthoritativeState(FlowDirection direction, string theme)
    {
        Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = new Uri($"/Onset;component/Resources/Themes/{theme}.xaml", UriKind.Relative) };
        var track = new Onset.Core.Entities.Track { IsTrackMuted = true, IsTrackSoloed = true };
        var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(
            new WorkstationSource("map.osu", "audio.wav", new ProjectData { Tracks = [track], Offsets = [0], BPMs = [120] })) };
        var vm = new WorkstationPageVM(source, new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm) { FlowDirection = direction }; page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 700 });
        UiTestPump.Run(page.LoadCompletion);
        var mute = (ToggleButton)((Onset.Views.Pages.OHsWorkstationAudioCard)page.FindName("AudioCard")).FindName("ClearTrackMutesToggle");
        var solo = (ToggleButton)((Onset.Views.Pages.OHsWorkstationAudioCard)page.FindName("AudioCard")).FindName("ClearTrackSolosToggle");
        UiControlContract.AssertStyle<ToggleButton>(mute, "OHsWorkstationAggregateToggleStyle");
        Assert.That(mute.Tag, Is.SameAs(vm.ToggleTrackMutesCommand));
        Assert.That(solo.Tag, Is.SameAs(vm.ClearTrackSolosCommand));
        var revision = vm.TrackAuditionRevision;
        Assert.That(mute.IsChecked, Is.True); Assert.That(mute.IsEnabled, Is.True);
        ((IToggleProvider)new ToggleButtonAutomationPeer(mute)).Toggle(); view.UpdateLayout();
        Assert.That(track.IsTrackMuted, Is.False); Assert.That(track.IsTrackSoloed, Is.True);
        Assert.That(mute.IsChecked, Is.False); Assert.That(mute.IsEnabled, Is.True);
        Assert.That(vm.TrackAuditionRevision, Is.EqualTo(revision + 1));
        ((IToggleProvider)new ToggleButtonAutomationPeer(mute)).Toggle(); view.UpdateLayout();
        Assert.That(track.IsTrackMuted, Is.True);
        Assert.That(mute.IsChecked, Is.True);
        Assert.That(vm.TrackAuditionRevision, Is.EqualTo(revision + 2));
        vm.SetTrackMuted(track, false); view.UpdateLayout();
        Assert.That(mute.IsChecked, Is.False);
        Assert.That(vm.TrackAuditionRevision, Is.EqualTo(revision + 3));
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


