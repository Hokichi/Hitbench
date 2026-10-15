using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Newtonsoft.Json;
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

    [TestCase(900, 700, "Dark", FlowDirection.LeftToRight)]
    [TestCase(900, 700, "Light", FlowDirection.RightToLeft)]
    [TestCase(1920, 1080, "Dark", FlowDirection.RightToLeft)]
    [TestCase(1920, 1080, "Light", FlowDirection.LeftToRight)]
    public void CollapseAndRecyclingPreserveEveryRealizedRow(int width, int height, string theme, FlowDirection direction)
    {
        Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary
            { Source = new Uri($"/osuHitsounder;component/Resources/Themes/{theme}.xaml", UriKind.Relative) };
        var files = Enumerable.Range(0, 10).Select(i => $"sample{i}.wav").ToArray();
        var tracks = CreateTracks(files);
        var saves = new DelayedProjectFileService();
        saves.Release();
        var (page, vm, view) = Create(direction, new ProjectData { Tracks = tracks, Offsets = [0], BPMs = [120] }, files, width, height, saves);
        using (view)
        try
        {
            Assert.That(vm.SelectNode(tracks[0], tracks[0].Samples[0], false), Is.True);
            vm.Position = TimeSpan.FromMilliseconds(375);
            var snapshot = JsonConvert.SerializeObject(tracks);
            var nodes = tracks.Select(t => t.Samples[0]).ToArray();
            var undo = vm.CanUndo;
            var redo = vm.CanRedo;
            for (var cycle = 0; cycle < 6; cycle++)
            {
                vm.ToggleTracksCollapsedCommand.Execute(null);
                foreach (var target in new[] { 0, 6, 16, 40, 79, 30, 0 })
                {
                    ScrollTo(page, view, tracks[target]);
                    AssertRealizedTrackState(page, vm);
                }
            }
            UiTestPump.Run(vm.FlushSavesAsync());
            Assert.Multiple(() =>
            {
                Assert.That(JsonConvert.SerializeObject(tracks), Is.EqualTo(snapshot));
                for (var i = 0; i < tracks.Count; i++) Assert.That(tracks[i].Samples.Single(), Is.SameAs(nodes[i]));
                Assert.That(vm.SelectedTrack, Is.SameAs(tracks[0]));
                Assert.That(vm.SelectedSamples, Is.EqualTo(new[] { nodes[0] }));
                Assert.That(vm.Position, Is.EqualTo(TimeSpan.FromMilliseconds(375)));
                Assert.That(vm.SamplesRevision, Is.Zero);
                Assert.That(vm.HasPendingSaves, Is.False);
                Assert.That(saves.Writes, Is.Zero);
                Assert.That(vm.CanUndo, Is.EqualTo(undo));
                Assert.That(vm.CanRedo, Is.EqualTo(redo));
            });
        }
        finally { UiTestPump.Run(page.DeactivateAsync()); }
    }

    [Test]
    public void CollapsedHeightStaysStableAcrossRecyclingAndRestoresGrowth()
    {
        var tracks = CreateTracks(["kick.wav"]);
        var (page, vm, view) = Create(project: new ProjectData { Tracks = tracks, Offsets = [0], BPMs = [120] });
        using (view)
        try
        {
            Settle(page, view);
            vm.IncreaseTrackHeightCommand.Execute(null);
            vm.IncreaseTrackHeightCommand.Execute(null);
            var growth = vm.TrackHeight - vm.MinimumTrackHeight;
            Assert.That(growth, Is.GreaterThan(0));
            vm.ToggleTracksCollapsedCommand.Execute(null);
            Settle(page, view);
            var minimum = vm.MinimumTrackHeight;
            foreach (var target in new[] { 6, 16, 40, 79, 30, 0 })
            {
                ScrollTo(page, view, tracks[target]);
                Assert.That(vm.MinimumTrackHeight, Is.EqualTo(minimum));
                Assert.That(vm.TrackHeight, Is.EqualTo(minimum));
            }
            vm.ToggleTracksCollapsedCommand.Execute(null);
            Settle(page, view);
            Assert.That(vm.TrackHeight - vm.MinimumTrackHeight, Is.EqualTo(growth));
            Assert.That(vm.SamplesRevision, Is.Zero);
            Assert.That(vm.HasPendingSaves, Is.False);
        }
        finally { UiTestPump.Run(page.DeactivateAsync()); }
    }

    [Test]
    public void RecycledRowsPreserveChoicesAndRouteEditsToCurrentTrack()
    {
        string[] files = ["kick.wav", "snare.wav"];
        var tracks = CreateTracks(files);
        for (var i = 3; i < tracks.Count; i += 7) tracks[i] = new InstrumentalTrack { Name = $"Instrument {i}" };
        var missing = tracks[16];
        missing.SelectedSampleFileName = missing.Samples[0].FileName = "missing.wav";
        var saves = new DelayedProjectFileService();
        saves.Release();
        var (page, vm, view) = Create(project: new ProjectData { Tracks = tracks, Offsets = [0], BPMs = [120] }, files: files, saves: saves);
        using (view)
        try
        {
            vm.ToggleTracksCollapsedCommand.Execute(null);
            vm.AddSampleTrackCommand.Execute(null);
            vm.AddPianoTrackCommand.Execute(null);
            var addedSample = vm.Tracks[^2];
            var addedInstrument = vm.Tracks[^1];
            foreach (var target in new[] { missing, tracks[40], addedSample, addedInstrument, tracks[0], missing })
            {
                ScrollTo(page, view, target);
                AssertRealizedTrackState(page, vm);
            }
            var list = (ListBox)page.FindName("TrackList");
            var missingRow = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(missing);
            Assert.That(Find<ComboBox>(missingRow, "TrackSampleFileSelector").SelectedItem, Is.Null);
            Assert.That(Find<TextBlock>(missingRow, "MissingGridStatus").Text, Is.EqualTo("Sample not found."));
            Assert.That(missing.SelectedSampleFileName, Is.EqualTo("missing.wav"));
            Assert.That(missing.Samples[0].FileName, Is.EqualTo("missing.wav"));

            UiTestPump.Run(vm.FlushSavesAsync());
            var writes = saves.Writes;
            var edited = tracks[40];
            var others = JsonConvert.SerializeObject(vm.Tracks.Where(t => !ReferenceEquals(t, edited)));
            ScrollTo(page, view, edited);
            var editedRow = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(edited);
            Find<ComboBox>(editedRow, "TrackSampleFileSelector").SelectedItem = "snare.wav";
            Assert.That(edited.SelectedSampleFileName, Is.EqualTo("snare.wav"));
            Assert.That(edited.Samples[0].FileName, Is.EqualTo("snare.wav"));
            Assert.That(JsonConvert.SerializeObject(vm.Tracks.Where(t => !ReferenceEquals(t, edited))), Is.EqualTo(others));
            UiTestPump.Run(vm.FlushSavesAsync());
            Assert.That(saves.Writes, Is.GreaterThan(writes));
            writes = saves.Writes;
            var revision = vm.SamplesRevision;
            vm.ToggleTracksCollapsedCommand.Execute(null);
            ScrollTo(page, view, tracks[0]);
            ScrollTo(page, view, edited);
            AssertRealizedTrackState(page, vm);
            UiTestPump.Run(vm.FlushSavesAsync());
            Assert.That(saves.Writes, Is.EqualTo(writes));
            Assert.That(vm.SamplesRevision, Is.EqualTo(revision));

            var (otherPage, otherVm, otherView) = Create(files: ["other.wav"]);
            using (otherView)
            try
            {
                otherVm.AddSampleTrackCommand.Execute(null);
                Settle(otherPage, otherView);
                Assert.That(Find<ComboBox>(otherPage, "TrackSampleFileSelector").Items.Cast<string>(), Is.EqualTo(new[] { "other.wav" }));
                Assert.That(Find<ComboBox>(editedRow, "TrackSampleFileSelector").Items.Cast<string>(), Is.EqualTo(files));
            }
            finally { UiTestPump.Run(otherPage.DeactivateAsync()); }
        }
        finally { UiTestPump.Run(page.DeactivateAsync()); }
    }

    [Test]
    public void RefreshRestoresRecycledRowsAfterDelayedSampleList()
    {
        string[] files = ["kick.wav", "snare.wav"];
        var tracks = CreateTracks(files);
        var samples = new StubSampleFileService(files);
        var (page, vm, view) = Create(project: new ProjectData { Tracks = tracks, Offsets = [0], BPMs = [120] }, sampleFiles: samples);
        using (view)
        try
        {
            vm.ToggleTracksCollapsedCommand.Execute(null);
            ScrollTo(page, view, tracks[40]);
            var snapshot = JsonConvert.SerializeObject(tracks);
            var listing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var choices = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
            samples.List = (_, _) => { listing.TrySetResult(); return choices.Task; };
            var refresh = vm.RefreshProjectCommand.ExecuteAsync(null);
            UiTestPump.Run(listing.Task);
            Settle(page, view);
            Assert.That(vm.SampleFiles, Is.Empty);
            choices.SetResult(files);
            UiTestPump.Run(refresh);
            foreach (var target in new[] { 40, 79, 0 })
            {
                ScrollTo(page, view, tracks[target]);
                AssertRealizedTrackState(page, vm);
            }
            Assert.That(JsonConvert.SerializeObject(tracks), Is.EqualTo(snapshot));
            Assert.That(vm.SamplesRevision, Is.Zero);
            Assert.That(vm.HasPendingSaves, Is.False);
        }
        finally { UiTestPump.Run(page.DeactivateAsync()); }
    }

    private static List<Track> CreateTracks(IReadOnlyList<string> files) => Enumerable.Range(0, 80).Select(i => new Track
    {
        Name = $"Track {i}", SelectedSampleFileName = files[i % files.Count], Volume = 20 + i,
        Addition = Track.AdditionOptions[i % 4],
        IsTrackMuted = i % 5 == 0, IsTrackSoloed = i % 7 == 0,
        Samples = [new Sample { Timestamp = i * 100, FileName = files[i % files.Count] }]
    }).ToList();

    private static void ScrollTo(WorkstationPage page, InMemoryView view, Track track)
    {
        ((ListBox)page.FindName("TrackList")).ScrollIntoView(track);
        Settle(page, view);
    }

    private static void Settle(WorkstationPage page, InMemoryView view)
    {
        view.UpdateLayout();
        UiTestPump.Run(page.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task);
        view.UpdateLayout();
    }

    private static void AssertRealizedTrackState(WorkstationPage page, WorkstationPageVM vm)
    {
        var list = (ListBox)page.FindName("TrackList");
        var rows = Descendants<ListBoxItem>(list).ToArray();
        Assert.That(rows, Is.Not.Empty);
        Assert.That(rows.Length, Is.LessThan(vm.Tracks.Count));
        foreach (var row in rows)
        {
            var track = (Track)row.DataContext;
            var groups = Descendants<StackPanel>(row).Where(x => x.Name is "TrackLeftExpandedControls" or "TrackRightExpandedControls").ToArray();
            Assert.That(groups, Has.Length.EqualTo(track is InstrumentalTrack ? 1 : 2));
            Assert.That(groups.Select(x => x.Visibility), Is.All.EqualTo(vm.AreTracksCollapsed ? Visibility.Collapsed : Visibility.Visible), track.Name);
            if (track is InstrumentalTrack)
            {
                var actions = Descendants<BalloonButton>(row).ToArray();
                var arrangement = actions.Single(x => AutomationProperties.GetName(x) == "View Arrangement");
                var mapping = actions.Single(x => AutomationProperties.GetName(x) == "Keys Mapping");
                UiControlContract.AssertStyle<BalloonButton>(arrangement, "OHsWorkstationInstrumentActionStyle");
                UiControlContract.AssertStyle<BalloonButton>(mapping, "OHsWorkstationKeysMappingStyle");
                Assert.That(arrangement.Command, Is.SameAs(vm.ViewArrangementCommand));
                Assert.That(mapping.Command, Is.SameAs(vm.KeysMappingCommand));
                continue;
            }
            var selector = Find<ComboBox>(row, "TrackSampleFileSelector");
            UiControlContract.AssertStyle<ComboBox>(selector, "OHsWorkstationSampleFileSelectorStyle");
            Assert.That(selector.Items.Cast<string>(), Is.EqualTo(vm.SampleFiles), track.Name);
            var expected = vm.SampleFiles.FirstOrDefault(file => string.Equals(file, track.SelectedSampleFileName, StringComparison.OrdinalIgnoreCase));
            Assert.That(selector.SelectedItem, Is.EqualTo(expected), track.Name);
            var grid = Descendants<BeatGridView>(row).Single();
            Assert.That(grid.Bpms, Is.SameAs(vm.Project!.BPMs), track.Name);
            Assert.That(grid.Offsets, Is.SameAs(vm.Project.Offsets), track.Name);
            Assert.That(grid.Height, Is.EqualTo(vm.TrackHeight), track.Name);
        }
    }

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
            Assert.That(toggle.ToolTip, Is.EqualTo("Expand all tracks (F4)"));
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
            Addition = "Drum", IsTrackMuted = true, IsTrackSoloed = true, Samples = [node] };
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
                Find<ComboBox>(page, "TrackAdditionSelector"),
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
    [TestCase("TrackAdditionSelector")]
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
        FlowDirection direction = FlowDirection.LeftToRight, ProjectData? project = null,
        IReadOnlyList<string>? files = null, int width = 900, int height = 700, DelayedProjectFileService? saves = null,
        StubSampleFileService? sampleFiles = null)
    {
        var source = new StubWorkstationSourceService();
        if (project is not null) source.Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav", project));
        var vm = new WorkstationPageVM(source, new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay),
            new ImmediateUiScheduler(), sampleFiles ?? new StubSampleFileService(files ?? ["kick.wav"]),
            new StubSampleFileDialogService(Array.Empty<string>()), projectFiles: saves);
        var page = new WorkstationPage(vm) { FlowDirection = direction };
        page.SetProjectPath("project.json");
        var view = new InMemoryView(new Window { Content = page, Width = width, Height = height });
        UiTestPump.Run(page.LoadCompletion);
        return (page, vm, view);
    }
}
