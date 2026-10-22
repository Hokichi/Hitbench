using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Soundie.Resources.CustomControls;
using Soundie.Services;
using Soundie.ViewModels.Shell;
using Soundie.Views.Pages;

namespace Soundie.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class WorkstationShortcutTests
{
    [TestCase(Key.D1, false)]
    [TestCase(Key.NumPad1, false)]
    [TestCase(Key.D2, true)]
    [TestCase(Key.NumPad2, true)]
    public void ModeShortcutsSwitchModesAndSuppressRepeats(Key key, bool addition)
    {
        var (page, vm, view) = CreateShortcutPage(); using (view)
        {
            vm.IsAdditionMode = !addition;
            Assert.That(page.HandlePlaybackKey(key, ModifierKeys.None, false, page), Is.True);
            Assert.That(vm.IsAdditionMode, Is.EqualTo(addition));
            vm.IsAdditionMode = !addition;
            Assert.That(page.HandlePlaybackKey(key, ModifierKeys.None, true, page), Is.True);
            Assert.That(vm.IsAdditionMode, Is.EqualTo(!addition));
            UiTestPump.Run(page.DeactivateAsync());
        }
    }

    [TestCase(ModifierKeys.Control, false)]
    [TestCase(ModifierKeys.Control | ModifierKeys.Shift, true)]
    public void NewTrackShortcutsWorkFromNameEditorAndSuppressRepeats(ModifierKeys modifiers, bool instrument)
    {
        var (page, vm, view) = CreateShortcutPage(); using (view)
        {
            var before = vm.Tracks.Count;
            Assert.That(page.HandlePlaybackKey(Key.N, modifiers, false, new TextBox()), Is.True);
            Assert.That(vm.Tracks, Has.Count.EqualTo(before + 1));
            Assert.That(vm.Tracks.Last() is Soundie.Core.Entities.InstrumentalTrack, Is.EqualTo(instrument));
            Assert.That(page.HandlePlaybackKey(Key.N, modifiers, true, page), Is.True);
            Assert.That(vm.Tracks, Has.Count.EqualTo(before + 1));
            Assert.That(page.HandlePlaybackKey(Key.N, ModifierKeys.Control | ModifierKeys.Alt, false, page), Is.False);
            UiTestPump.Run(page.DeactivateAsync());
        }
    }

    [TestCase(Key.F3)]
    [TestCase(Key.F4)]
    public void ToolbarToggleShortcutsToggleOnlyOnce(Key key)
    {
        var (page, vm, view) = CreateShortcutPage(); using (view)
        {
            var before = key == Key.F3 ? vm.ScrollWithMarker : vm.AreTracksCollapsed;
            Assert.That(page.HandlePlaybackKey(key, ModifierKeys.None, false, new TextBox()), Is.True);
            Assert.That(key == Key.F3 ? vm.ScrollWithMarker : vm.AreTracksCollapsed, Is.EqualTo(!before));
            Assert.That(page.HandlePlaybackKey(key, ModifierKeys.None, true, page), Is.True);
            Assert.That(key == Key.F3 ? vm.ScrollWithMarker : vm.AreTracksCollapsed, Is.EqualTo(!before));
            Assert.That(page.HandlePlaybackKey(key, ModifierKeys.Control, false, page), Is.False);
            UiTestPump.Run(page.DeactivateAsync());
        }
    }

    [TestCase(Key.F2)]
    [TestCase(Key.F6)]
    public void WorkflowShortcutsInvokeOnceAndSuppressRepeats(Key key)
    {
        var opened = 0;
        var manager = new StubSampleManagementDialogService { Present = (_, _) => { opened++; return Task.CompletedTask; } };
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay),
            new ImmediateUiScheduler(), new StubSampleFileService(["hit.wav"]), new StubSampleFileDialogService([]),
            messenger: new CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger(), sampleManagementDialog: manager);
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.PendingControlPointAdd) && vm.PendingControlPointAdd is not null) opened++; };
        var page = new WorkstationPage(vm); page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);
        Assert.That(page.HandlePlaybackKey(key, ModifierKeys.None, false, new TextBox()), Is.True);
        UiTestPump.Run(page.PlaybackInputCompletion); Assert.That(opened, Is.EqualTo(1));
        Assert.That(page.HandlePlaybackKey(key, ModifierKeys.None, true, page), Is.True);
        Assert.That(opened, Is.EqualTo(1)); UiTestPump.Run(page.DeactivateAsync());
    }

    [TestCase(Key.D1, 1, 0)]
    [TestCase(Key.D2, 2, 1)]
    [TestCase(Key.D3, 3, 2)]
    [TestCase(Key.D4, 4, 3)]
    [TestCase(Key.D5, 5, 4)]
    [TestCase(Key.D6, 6, 5)]
    [TestCase(Key.D8, 8, 6)]
    [TestCase(Key.NumPad1, 1, 0)]
    [TestCase(Key.NumPad2, 2, 1)]
    [TestCase(Key.NumPad3, 3, 2)]
    [TestCase(Key.NumPad4, 4, 3)]
    [TestCase(Key.NumPad5, 5, 4)]
    [TestCase(Key.NumPad6, 6, 5)]
    [TestCase(Key.NumPad8, 8, 6)]
    public void ExistingDivisorShortcutSelectsMatchingValue(Key key, int divisor, int index)
    {
        var (page, vm, view) = CreateShortcutPage();
        using (view)
        {
            Assert.That(page.HandlePlaybackKey(key, ModifierKeys.Control, false, page), Is.True);
            Assert.That(vm.Subdivision, Is.EqualTo(divisor));
            Assert.That(vm.SelectedSubdivisionIndex, Is.EqualTo(index));
            Assert.That(((ValueSlider)((Soundie.Views.Pages.OHsWorkstationFooter)page.Footer).FindName("SubdivisionSelector")).Value, Is.EqualTo(index));
            foreach (var grid in Descendants(page).OfType<BeatGridView>())
                Assert.That(grid.Subdivision, Is.EqualTo(divisor));
            UiTestPump.Run(page.DeactivateAsync());
        }
    }

    [TestCase(Key.D0, ModifierKeys.Control, false, false)]
    [TestCase(Key.D7, ModifierKeys.Control, false, false)]
    [TestCase(Key.D9, ModifierKeys.Control, false, false)]
    [TestCase(Key.NumPad0, ModifierKeys.Control, false, false)]
    [TestCase(Key.NumPad7, ModifierKeys.Control, false, false)]
    [TestCase(Key.NumPad9, ModifierKeys.Control, false, false)]
    [TestCase(Key.D3, ModifierKeys.None, false, false)]
    [TestCase(Key.D3, ModifierKeys.Shift, false, false)]
    [TestCase(Key.D3, ModifierKeys.Control | ModifierKeys.Shift, false, false)]
    [TestCase(Key.D3, ModifierKeys.Control | ModifierKeys.Alt, false, false)]
    [TestCase(Key.D3, ModifierKeys.Control, true, true)]
    public void UnsupportedOrRepeatedDivisorShortcutPreservesSelection(Key key, ModifierKeys modifiers, bool repeat, bool handled)
    {
        var (page, vm, view) = CreateShortcutPage();
        using (view)
        {
            Assert.That(page.HandlePlaybackKey(key, modifiers, repeat, page), Is.EqualTo(handled));
            Assert.That(vm.Subdivision, Is.EqualTo(4));
            UiTestPump.Run(page.DeactivateAsync());
            Assert.That(page.HandlePlaybackKey(Key.D3, ModifierKeys.Control, false, page), Is.False);
            Assert.That(vm.Subdivision, Is.EqualTo(4));
        }
    }

    [Test]
    public void DivisorShortcutIsUnavailableWhileLoading()
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(),
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        Assert.That(page.HandlePlaybackKey(Key.D3, ModifierKeys.Control, false, page), Is.False);
        Assert.That(vm.Subdivision, Is.EqualTo(4));
        UiTestPump.Run(page.DeactivateAsync());
    }

    private static (WorkstationPage Page, WorkstationPageVM Vm, InMemoryView View) CreateShortcutPage()
    {
        var track = new Soundie.Core.Entities.Track { SelectedSampleFileName = "a.wav" };
        var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(
            new Soundie.Core.Entities.WorkstationSource("map.osu", "audio.wav",
                new Soundie.Core.Entities.ProjectData { Offsets = [0], BPMs = [120], Tracks = [track] })) };
        var vm = new WorkstationPageVM(source, new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay),
            new ImmediateUiScheduler(), new StubSampleFileService(["a.wav"]));
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);
        return (page, vm, view);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(System.Windows.Media.VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    [TestCase("text")]
    [TestCase("password")]
    [TestCase("selector")]
    [TestCase("slider")]
    [TestCase("button")]
    [TestCase("track")]
    public void GlobalHistoryWorksAcrossFocusAndSuppressesRepeats(string kind)
    {
        var first = new Soundie.Core.Entities.Track { SelectedSampleFileName = "a.wav" };
        var second = new Soundie.Core.Entities.Track { SelectedSampleFileName = "a.wav" };
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new Soundie.Core.Entities.WorkstationSource("map.osu", "audio.wav",
                new Soundie.Core.Entities.ProjectData { Offsets = [0], BPMs = [120], Tracks = [first, second] }))
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
        if (kind is "text" or "password")
        {
            Assert.That(page.HandlePlaybackKey(Key.Z, ModifierKeys.Control, false, origin), Is.False);
            Assert.That(page.HandlePlaybackKey(Key.Y, ModifierKeys.Control, false, origin), Is.False);
            Assert.That(second.Samples.Single(), Is.SameAs(node));
            UiTestPump.Run(page.DeactivateAsync());
            return;
        }
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

        if (kind is "text" or "password")
        {
            Assert.That(page.HandlePlaybackKey(Key.Space, ModifierKeys.None, false, origin), Is.False);
            Assert.That(vm.IsPlaying, Is.False);
            UiTestPump.Run(page.DeactivateAsync());
            return;
        }

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
