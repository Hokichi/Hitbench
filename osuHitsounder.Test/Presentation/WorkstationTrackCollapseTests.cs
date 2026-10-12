using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.Services;
using osuHitsounder.ViewModels.Shell;
using osuHitsounder.Views.Pages;
using Track = osuHitsounder.Core.Entities.Track;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class WorkstationTrackCollapseTests
{
    [SetUp] public void Setup() => new VolumeWheelTests().SetUp();

    [TestCase(FlowDirection.LeftToRight, "Dark")]
    [TestCase(FlowDirection.LeftToRight, "Light")]
    [TestCase(FlowDirection.RightToLeft, "Dark")]
    [TestCase(FlowDirection.RightToLeft, "Light")]
    public void AudioCardActionsBindExistingCommandsAndFollowAvailability(FlowDirection direction, string theme)
    {
        Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary
            { Source = new Uri($"/osuHitsounder;component/Resources/Themes/{theme}.xaml", UriKind.Relative) };
        var (page, vm, view) = Create(direction);
        using (view)
        {
            var sample = (BalloonButton)page.FindName("AddSampleTrackButton");
            var instrument = (BalloonButton)page.FindName("AddInstrumentButton");
            var toggle = (BalloonButton)page.FindName("ToggleTracksCollapsedButton");
            Assert.That(sample, Is.Not.Null, "Audio card needs sample creation action");
            UiControlContract.AssertStyle<BalloonButton>(sample, "OHsWorkstationToolbarButtonStyle");
            UiControlContract.AssertStyle<BalloonButton>(instrument, "OHsWorkstationAddInstrumentStyle");
            UiControlContract.AssertStyle<BalloonButton>(toggle, "OHsWorkstationCollapseTracksStyle");
            Assert.Multiple(() =>
            {
                Assert.That(sample.Command, Is.SameAs(vm.AddSampleTrackCommand));
                Assert.That(instrument.Command, Is.SameAs(vm.AddPianoTrackCommand));
                Assert.That(toggle.Command, Is.SameAs(vm.ToggleTracksCollapsedCommand));
                Assert.That(AutomationProperties.GetName(sample), Is.EqualTo("Add New Sample Track"));
                Assert.That(AutomationProperties.GetName(instrument), Is.EqualTo("Add New Instrument"));
                Assert.That(AutomationProperties.GetHelpText(instrument), Does.Contain("instrumental arrangement"));
                Assert.That(AutomationProperties.GetName(toggle), Is.EqualTo("Collapse all tracks"));
            });
            foreach (var action in new[] { sample, instrument, toggle })
            {
                Assert.That(KeyboardNavigation.GetIsTabStop(action), Is.EqualTo(ReferenceEquals(action,instrument)));
                Assert.That(action.Focusable, Is.True);
                Assert.That(action.FocusVisualStyle, Is.SameAs(page.FindResource(ReferenceEquals(action,instrument)?"OHsSampleManagerKeyboardFocusStyle":"OHsKeyboardFocusVisualStyle")));
                Assert.That(ToolTipService.GetShowsToolTipOnKeyboardFocus(action), Is.True);
                Assert.That(action.IsEnabled, Is.True);
            }
            PressEnter(sample);
            Assert.That(vm.Tracks, Has.Count.EqualTo(1));
            PressEnter(instrument);
            Assert.That(vm.Tracks, Has.Count.EqualTo(2));
            PressEnter(toggle);
            view.UpdateLayout();
            Assert.That(vm.AreTracksCollapsed, Is.True);
            Assert.That(AutomationProperties.GetName(toggle), Is.EqualTo("Expand all tracks"));
            Assert.That(toggle.ToolTip, Is.EqualTo("Expand all tracks"));
            vm.IsReturning = true;
            view.UpdateLayout();
            Assert.That(sample.IsEnabled, Is.False);
            Assert.That(instrument.IsEnabled, Is.False);
            Assert.That(toggle.IsEnabled, Is.False);
            vm.IsReturning = false;
            view.UpdateLayout();
            Assert.That(sample.IsEnabled, Is.True);
            UiTestPump.Run(page.DeactivateAsync());
        }
    }

    [TestCase(FlowDirection.LeftToRight, "Dark")]
    [TestCase(FlowDirection.LeftToRight, "Light")]
    [TestCase(FlowDirection.RightToLeft, "Dark")]
    [TestCase(FlowDirection.RightToLeft, "Light")]
    public void CollapseHidesOnlySecondaryControlsAndPreservesTrackState(FlowDirection direction, string theme)
    {
        Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary
            { Source = new Uri($"/osuHitsounder;component/Resources/Themes/{theme}.xaml", UriKind.Relative) };
        var node = new Sample { Timestamp = 125, FileName = "kick.wav" };
        var track = new Track { Name = "Kick", SelectedSampleFileName = "kick.wav", Volume = 64,
            Sampleset = "Soft", Addition = "Drum", IsTrackMuted = true, IsTrackSoloed = true, Samples = [node] };
        var (page, vm, view) = Create(direction, new ProjectData { Offsets = [0], BPMs = [120], Tracks = [track] });
        using (view)
        {
            Assert.That(vm.SelectNode(track, node, false), Is.True);
            vm.Position = TimeSpan.FromMilliseconds(375);
            var name = Descendants<TextBox>(page).Single(control => AutomationProperties.GetName(control) == "Track name");
            var file = Find<ComboBox>(page, "TrackSampleFileSelector");
            var grid = Descendants<BeatGridView>(page).Single();
            var secondary = new FrameworkElement[] { Find<ValueSlider>(page, "VolumeSlider"),
                Find<ToggleButton>(page, "TrackMuteToggle"), Find<ToggleButton>(page, "TrackSoloToggle"),
                Find<ComboBox>(page, "TrackSamplesetSelector"), Find<ComboBox>(page, "TrackAdditionSelector"),
                Find<ComboBox>(page, "TrackSampleTypeSelector"),
                Descendants<BalloonButton>(page).Single(control => AutomationProperties.GetName(control) == "Remove track") };
            UiControlContract.AssertStyle<ValueSlider>(secondary[0], "OHsWorkstationVolumeStyle");
            for (var cycle = 0; cycle < 2; cycle++)
            {
                vm.ToggleTracksCollapsedCommand.Execute(null);
                view.UpdateLayout();
                Assert.That(name.IsVisible, Is.True);
                Assert.That(file.IsVisible, Is.True);
                Assert.That(secondary.All(control => !control.IsVisible), Is.True);
                Assert.That(grid.IsVisible, Is.True);
                Assert.That(vm.TrackHeight, Is.EqualTo(vm.MinimumTrackHeight));
                Assert.That(grid.AllowNodeCreation, Is.True);
                vm.IsAdditionMode = false;
                Assert.That(grid.AllowNodeCreation, Is.False);
                vm.IsAdditionMode = true;
                vm.ToggleTracksCollapsedCommand.Execute(null);
                view.UpdateLayout();
                Assert.That(secondary.All(control => control.IsVisible), Is.True);
            }
            Assert.Multiple(() =>
            {
                Assert.That(track.Name, Is.EqualTo("Kick"));
                Assert.That(track.SelectedSampleFileName, Is.EqualTo("kick.wav"));
                Assert.That(track.Volume, Is.EqualTo(64));
                Assert.That(track.Sampleset, Is.EqualTo("Soft"));
                Assert.That(track.Addition, Is.EqualTo("Drum"));
                Assert.That(track.IsTrackMuted && track.IsTrackSoloed, Is.True);
                Assert.That(track.Samples.Single(), Is.SameAs(node));
                Assert.That(vm.SelectedTrack, Is.SameAs(track));
                Assert.That(vm.SelectedSamples.Single(), Is.SameAs(node));
                Assert.That(vm.Position, Is.EqualTo(TimeSpan.FromMilliseconds(375)));
                Assert.That(vm.HasPendingSaves, Is.False);
            });
            UiTestPump.Run(page.DeactivateAsync());
        }
    }

    [Test]
    public void CollapsePointerKeepsTrackAndNodeSelection()
    {
        var node = new Sample { Timestamp = 125, FileName = "kick.wav" };
        var track = new Track { SelectedSampleFileName = "kick.wav", Samples = [node] };
        var (page, vm, view) = Create(project: new ProjectData { Offsets = [0], BPMs = [120], Tracks = [track] });
        using (view)
        {
            Assert.That(vm.SelectNode(track, node, false), Is.True);
            var toggle = (BalloonButton)page.FindName("ToggleTracksCollapsedButton");
            toggle.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = Mouse.PreviewMouseDownEvent });
            PressEnter(toggle);
            view.UpdateLayout();
            Assert.That(vm.AreTracksCollapsed, Is.True);
            Assert.That(vm.SelectedTrack, Is.SameAs(track));
            Assert.That(vm.SelectedSamples.Single(), Is.SameAs(node));
            UiTestPump.Run(page.DeactivateAsync());
        }
    }

    [Test]
    public void CollapsedStateAppliesToAddedAndRecycledTracks()
    {
        var tracks = Enumerable.Range(0, 80).Select(index => new Track
            { Name = $"Track {index}", SelectedSampleFileName = "kick.wav" }).ToList();
        var (page, vm, view) = Create(project: new ProjectData { Offsets = [0], BPMs = [120], Tracks = tracks });
        using (view)
        {
            var list = (ListBox)page.FindName("TrackList");
            vm.ToggleTracksCollapsedCommand.Execute(null);
            PressEnter((BalloonButton)page.FindName("AddSampleTrackButton"));
            list.ScrollIntoView(vm.Tracks.Last());
            view.UpdateLayout();
            Assert.That(vm.Tracks, Has.Count.EqualTo(81));
            var realized = Descendants<ValueSlider>(list).Where(control => control.Name == "VolumeSlider").ToArray();
            Assert.That(realized, Is.Not.Empty);
            Assert.That(realized.Length, Is.LessThan(81));
            Assert.That(realized.All(control => !control.IsVisible), Is.True);
            Assert.That(Descendants<ComboBox>(list).Any(control => ReferenceEquals(control.DataContext, vm.Tracks.Last())), Is.True);
            vm.ToggleTracksCollapsedCommand.Execute(null);
            list.ScrollIntoView(vm.Tracks[0]);
            view.UpdateLayout();
            Assert.That(Descendants<ValueSlider>(list).Where(control => control.Name == "VolumeSlider")
                .All(control => control.IsVisible), Is.True);
            Assert.That(Descendants<ComboBox>(list).Any(control => ReferenceEquals(control.DataContext, vm.Tracks[0])), Is.True);
            UiTestPump.Run(page.DeactivateAsync());
        }
    }

    [TestCase("VolumeSlider")]
    [TestCase("TrackSamplesetSelector")]
    public void CollapseRestoresLogicalFocusOnlyFromHiddenControls(string controlName)
    {
        var track = new Track { SelectedSampleFileName = "kick.wav" };
        var (page, vm, view) = Create(project: new ProjectData { Offsets = [0], BPMs = [120], Tracks = [track] });
        using (view)
        {
            var hidden = Find<FrameworkElement>(page, controlName);
            var toggle = (BalloonButton)page.FindName("ToggleTracksCollapsedButton");
            var scope = FocusManager.GetFocusScope(toggle);
            page.RestoreTrackFocusBeforeCollapse(hidden);
            Assert.That(FocusManager.GetFocusedElement(scope), Is.SameAs(toggle));
            FocusManager.SetFocusedElement(scope, hidden);
            vm.ToggleTracksCollapsedCommand.Execute(null);
            view.UpdateLayout();
            Assert.That(FocusManager.GetFocusedElement(scope), Is.SameAs(toggle));
            var name = Descendants<TextBox>(page).Single(control => AutomationProperties.GetName(control) == "Track name");
            FocusManager.SetFocusedElement(scope, name);
            page.RestoreTrackFocusBeforeCollapse(name);
            Assert.That(FocusManager.GetFocusedElement(scope), Is.SameAs(name));
            UiTestPump.Run(page.DeactivateAsync());
        }
    }

    private static void PressEnter(UIElement action)
    {
        using var source = new InMemoryPresentationSource();
        action.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Enter)
            { RoutedEvent = Keyboard.KeyDownEvent });
    }

    private static T Find<T>(DependencyObject root, string name) where T : FrameworkElement =>
        Descendants<T>(root).Single(control => control.Name == name);

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    private static (WorkstationPage Page, WorkstationPageVM Vm, InMemoryView View) Create(
        FlowDirection direction = FlowDirection.LeftToRight, ProjectData? project = null)
    {
        var source = new StubWorkstationSourceService();
        if (project is not null) source.Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav", project));
        var vm = new WorkstationPageVM(source, new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay),
            new ImmediateUiScheduler(), new StubSampleFileService(["kick.wav"]));
        var page = new WorkstationPage(vm) { FlowDirection = direction };
        page.SetProjectPath("project.json");
        var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 700 });
        UiTestPump.Run(page.LoadCompletion);
        return (page, vm, view);
    }
}
