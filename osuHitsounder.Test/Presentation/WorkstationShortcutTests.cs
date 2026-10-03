using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.Services;
using osuHitsounder.ViewModels.Shell;
using osuHitsounder.Views.Pages;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class WorkstationShortcutTests
{
    [Test]
    public void GlobalNavigationSynchronizesFocusedTrackInterval()
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);
        var grid = new BeatGridView { Offsets = [0], Bpms = [120], Subdivision = 4, Duration = TimeSpan.FromSeconds(10), CanPlaceSamples = true };
        Assert.That(page.HandlePlaybackKey(Key.Right, ModifierKeys.None, false, grid), Is.True);
        UiTestPump.Run(page.PlaybackInputCompletion);
        Assert.That(grid.ActiveInterval!.Value.StartMilliseconds, Is.EqualTo(125));
        double? placement = null;
        grid.SamplePlacementRequested += (_, e) => placement = e.LeftBoundaryMilliseconds;
        Assert.That(grid.RequestSamplePlacement(), Is.True);
        Assert.That(placement, Is.EqualTo(125));
    }
    [SetUp]
    public void Setup() => new VolumeWheelTests().SetUp();

    [TestCase("text")]
    [TestCase("password")]
    [TestCase("selector")]
    [TestCase("slider")]
    [TestCase("button")]
    [TestCase("track")]
    public void TransportAndNavigationWorkAcrossFocus(string kind)
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);
        DependencyObject origin = kind switch
        {
            "text" => new TextBox(), "password" => new PasswordBox(), "selector" => new ComboBox(),
            "slider" => new Slider(), "button" => new Button(), _ => new BeatGridView()
        };
        Assert.That(page.HandlePlaybackKey(Key.Right, ModifierKeys.None, false, origin), Is.True);
        UiTestPump.Run(page.PlaybackInputCompletion);
        Assert.That(vm.Position, Is.EqualTo(TimeSpan.FromMilliseconds(125)));
        Assert.That(page.HandlePlaybackKey(Key.Space, ModifierKeys.None, false, origin), Is.True);
        UiTestPump.Run(page.PlaybackInputCompletion);
        Assert.That(vm.IsPlaying, Is.True);
        Assert.That(page.HandlePlaybackKey(Key.Space, ModifierKeys.None, true, origin), Is.True);
        Assert.That(vm.IsPlaying, Is.True);
        Assert.That(page.HandlePlaybackKey(Key.Space, ModifierKeys.Shift, false, origin), Is.True);
        UiTestPump.Run(page.PlaybackInputCompletion);
        Assert.That(vm.IsPlaying, Is.False);
        Assert.That(vm.Position, Is.EqualTo(TimeSpan.Zero));
        Assert.That(page.HandlePlaybackKey(Key.End, ModifierKeys.Control, false, origin), Is.True);
        UiTestPump.Run(page.PlaybackInputCompletion);
        Assert.That(vm.Position, Is.EqualTo(vm.Waveform!.Duration));
        Assert.That(page.HandlePlaybackKey(Key.Home, ModifierKeys.Control, false, origin), Is.True);
        UiTestPump.Run(page.PlaybackInputCompletion);
        Assert.That(vm.Position, Is.EqualTo(TimeSpan.Zero));
        Assert.That(page.HandlePlaybackKey(Key.A, ModifierKeys.None, false, origin), Is.False);
    }
}
