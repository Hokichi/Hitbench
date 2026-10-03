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
    [TestCase("text")]
    [TestCase("password")]
    [TestCase("selector")]
    [TestCase("slider")]
    [TestCase("button")]
    [TestCase("track")]
    public void GlobalHistoryWorksAcrossFocusAndSuppressesRepeats(string kind)
    {
        var first = new osuHitsounder.Core.Entities.Track { SelectedSampleFileName = "a.wav" };
        var second = new osuHitsounder.Core.Entities.Track { SelectedSampleFileName = "a.wav" };
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new osuHitsounder.Core.Entities.WorkstationSource("map.osu", "audio.wav",
                new osuHitsounder.Core.Entities.ProjectData { Offsets = [0], BPMs = [120], Tracks = [first, second] }))
        };
        var vm = new WorkstationPageVM(source, new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay),
            new ImmediateUiScheduler(), new StubSampleFileService(["a.wav"]));
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);
        DependencyObject origin = kind switch
        {
            "text" => new TextBox(), "password" => new PasswordBox(), "selector" => new ComboBox(),
            "slider" => new Slider(), "button" => new Button(), _ => new BeatGridView()
        };
        Assert.That(page.HandlePlaybackKey(Key.Z, ModifierKeys.Control, false, origin), Is.False);
        Assert.That(page.HandlePlaybackKey(Key.Y, ModifierKeys.Control, false, origin), Is.False);
        vm.PlaceSample(first, 125);
        vm.PlaceSample(second, 250);
        var node = second.Samples.Single();
        Assert.That(page.HandlePlaybackKey(Key.Z, ModifierKeys.None, false, origin), Is.False);
        Assert.That(page.HandlePlaybackKey(Key.Y, ModifierKeys.None, false, origin), Is.False);
        Assert.That(page.HandlePlaybackKey(Key.Z, ModifierKeys.Control | ModifierKeys.Shift, false, origin), Is.False);
        Assert.That(page.HandlePlaybackKey(Key.Y, ModifierKeys.Control | ModifierKeys.Alt, false, origin), Is.False);
        Assert.That(second.Samples.Single(), Is.SameAs(node));
        Assert.That(page.HandlePlaybackKey(Key.Z, ModifierKeys.Control, false, origin), Is.True);
        Assert.That(second.Samples, Is.Empty);
        Assert.That(page.HandlePlaybackKey(Key.Z, ModifierKeys.Control, true, origin), Is.True);
        Assert.That(first.Samples, Has.Count.EqualTo(1));
        Assert.That(page.HandlePlaybackKey(Key.Z, ModifierKeys.Control, false, origin), Is.True);
        Assert.That(first.Samples, Is.Empty);
        Assert.That(page.HandlePlaybackKey(Key.Z, ModifierKeys.Control, true, origin), Is.True);
        Assert.That(page.HandlePlaybackKey(Key.Z, ModifierKeys.Control, false, origin), Is.False);
        Assert.That(page.HandlePlaybackKey(Key.Y, ModifierKeys.Control, false, origin), Is.True);
        Assert.That(first.Samples, Has.Count.EqualTo(1));
        Assert.That(page.HandlePlaybackKey(Key.Y, ModifierKeys.Control, true, origin), Is.True);
        Assert.That(second.Samples, Is.Empty);
        Assert.That(page.HandlePlaybackKey(Key.Y, ModifierKeys.Control, false, origin), Is.True);
        Assert.That(second.Samples.Single(), Is.SameAs(node));
        Assert.That(page.HandlePlaybackKey(Key.Y, ModifierKeys.Control, true, origin), Is.True);
        Assert.That(page.HandlePlaybackKey(Key.Y, ModifierKeys.Control, false, origin), Is.False);
        UiTestPump.Run(page.DeactivateAsync());
        Assert.That(page.HandlePlaybackKey(Key.Z, ModifierKeys.Control, false, origin), Is.False);
        Assert.That(page.HandlePlaybackKey(Key.Y, ModifierKeys.Control, true, origin), Is.False);
    }

    [Test]
    public void HistoryShortcutsAreUnavailableWhileLoading()
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(),
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        Assert.That(page.HandlePlaybackKey(Key.Z, ModifierKeys.Control, false, null), Is.False);
        Assert.That(page.HandlePlaybackKey(Key.Y, ModifierKeys.Control, true, null), Is.False);
        UiTestPump.Run(page.DeactivateAsync());
    }

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
