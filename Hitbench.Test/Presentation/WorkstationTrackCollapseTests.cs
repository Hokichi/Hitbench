using System.Windows;
using System.Windows.Data;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Newtonsoft.Json;
using Hitbench.Core.Entities;
using Hitbench.Resources.CustomControls;
using Hitbench.Services;
using Hitbench.ViewModels.Shell;
using Hitbench.Views.Pages;
using Track = Hitbench.Core.Entities.Track;

namespace Hitbench.Test.Presentation;

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
            { Source = new Uri($"/Hitbench;component/Resources/Themes/{theme}.xaml", UriKind.Relative) };
        var files = Enumerable.Range(0, 10).Select(i => $"sample{i}.wav").ToArray();
        var tracks = CreateTracks(files);
        var saves = new DelayedProjectFileService();
        saves.Release();
        var (page, vm, view) = Create(direction, new ProjectData { Tracks = tracks, Offsets = [0], BPMs = [120], ControlPoints = [new()] }, files, width, height, saves);
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
                Assert.That(saves.Writes, Is.EqualTo(6));
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
            var list = (ListBox)((Hitbench.Views.Pages.OHsWorkstationTrackList)page.FindName("TrackListComponent")).FindName("TrackList");
            var missingRow = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(missing);
            Assert.That(Find<ComboBox>(missingRow, "TrackSampleFileSelector").SelectedItem, Is.Null);
            Assert.That(Find<Grid>(missingRow, "TrackGridContent").IsEnabled, Is.False);
            Assert.That(missing.SelectedSampleFileName, Is.EqualTo("missing.wav"));
            Assert.That(missing.Samples[0].FileName, Is.EqualTo("missing.wav"));

            UiTestPump.Run(vm.FlushSavesAsync());
            var writes = saves.Writes;
            var edited = tracks[40];
            var others = JsonConvert.SerializeObject(vm.Tracks.Where(t => !ReferenceEquals(t, edited)));
            ScrollTo(page, view, edited);
            var editedRow = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(edited);
            var editingRow = Descendants<OHsWorkstationTrackRow>(editedRow).Single();
            editingRow.PresentSampleConfiguration = (_, _) => { };
            editingRow.OpenSampleConfiguration();
            Find<ComboBox>(editedRow, "TrackSampleFileSelector").SelectedItem = "snare.wav";
            editingRow.CloseSampleConfiguration();
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
            Assert.That(saves.Writes, Is.EqualTo(writes + 1));
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
    public void ReloadSamplesRestoresRecycledRowsAfterDelayedSampleList()
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
            var revision = vm.SamplesRevision;
            var listing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var choices = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
            samples.List = (_, _) => { listing.TrySetResult(); return choices.Task; };
            var refresh = vm.ReloadSamplesCommand.ExecuteAsync(null);
            UiTestPump.Run(listing.Task);
            Settle(page, view);
            Assert.That(vm.SampleFiles, Is.EqualTo(files));
            choices.SetResult(files);
            UiTestPump.Run(refresh);
            foreach (var target in new[] { 40, 79, 0 })
            {
                ScrollTo(page, view, tracks[target]);
                AssertRealizedTrackState(page, vm);
            }
            Assert.That(JsonConvert.SerializeObject(tracks), Is.EqualTo(snapshot));
            Assert.That(vm.SamplesRevision, Is.EqualTo(revision + 1));
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
        ((ListBox)((Hitbench.Views.Pages.OHsWorkstationTrackList)page.FindName("TrackListComponent")).FindName("TrackList")).ScrollIntoView(track);
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
        var list = (ListBox)((Hitbench.Views.Pages.OHsWorkstationTrackList)page.FindName("TrackListComponent")).FindName("TrackList");
        var rows = Descendants<ListBoxItem>(list).ToArray();
        Assert.That(rows, Is.Not.Empty);
        Assert.That(rows.Length, Is.LessThan(vm.Tracks.Count));
        foreach (var row in rows)
        {
            var track = (Track)row.DataContext;
            var groups = Descendants<FrameworkElement>(row).Where(x => x.Name is "VolumeSlider" or "TrackMuteToggle" or "TrackSoloToggle").ToArray();
            Assert.That(groups, Is.Not.Empty);
            Assert.That(groups.Select(x => x.Visibility), Is.All.EqualTo(track.IsCollapsed ? Visibility.Collapsed : Visibility.Visible), track.Name);
            if (track is InstrumentalTrack)
            {
                var actions = Descendants<BalloonButton>(row).ToArray();
                var arrangement = actions.Single(x => AutomationProperties.GetName(x) == "View Arrangement");
                var mapping = Descendants<BalloonComboBox>(row).Single().ContextMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Keys Mapping"));
                UiControlContract.AssertStyle<BalloonButton>(arrangement, "OHsWorkstationInstrumentMutedActionStyle");
                UiControlContract.AssertStyle<MenuItem>(mapping, "OHsWorkstationKeysMappingMenuItemStyle");
                Assert.That(arrangement.Command, Is.SameAs(vm.ViewArrangementCommand));
                Assert.That(mapping.Command, Is.SameAs(vm.KeysMappingCommand));
                continue;
            }
            var selector = Find<ComboBox>(row, "TrackSampleFileSelector");
            UiControlContract.AssertStyle<ComboBox>(selector, "OHsWorkstationTrackMetadataComboBoxStyle");
            Assert.That(selector.Items.Cast<string>(), Is.EqualTo(vm.SampleFiles), track.Name);
            var expected = vm.SampleFiles.FirstOrDefault(file => string.Equals(file, track.SelectedSampleFileName, StringComparison.OrdinalIgnoreCase));
            Assert.That(selector.SelectedItem, Is.EqualTo(expected), track.Name);
            var grid = Descendants<BeatGridView>(row).Single();
            Assert.That(grid.Bpms, Is.SameAs(vm.Project!.BPMs), track.Name);
            Assert.That(grid.Offsets, Is.SameAs(vm.Project.Offsets), track.Name);
            Assert.That(BindingOperations.GetBindingExpression(grid, FrameworkElement.HeightProperty), Is.Not.Null, track.Name);
        }
    }

    [TestCase(FlowDirection.LeftToRight, "Dark")]
    [TestCase(FlowDirection.LeftToRight, "Light")]
    [TestCase(FlowDirection.RightToLeft, "Dark")]
    [TestCase(FlowDirection.RightToLeft, "Light")]
    public void AudioCardActionsBindExistingCommandsAndFollowAvailability(FlowDirection direction, string theme)
    {
        Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary
            { Source = new Uri($"/Hitbench;component/Resources/Themes/{theme}.xaml", UriKind.Relative) };
        var (page, vm, view) = Create(direction);
        using (view)
        {
            var trigger = (BalloonComboBox)page.Toolbar.FindName("AddMenuButton");
            UiTestPump.Layout(trigger.ContextMenu, 320, 200);
            UiTestPump.Run(trigger.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task);
            var sample = (MenuItem)trigger.ContextMenu.Items[0];
            var instrument = (MenuItem)trigger.ContextMenu.Items[1];
            var toggle = WorkstationActionMenus.Find(page.Toolbar, vm.ToggleTracksCollapsedCommand);
            var refresh = WorkstationActionMenus.Find(page.Toolbar, vm.ReloadSamplesCommand);
            var resnap = WorkstationActionMenus.Find(page.Toolbar, vm.ResnapAllSamplesCommand);
            Assert.That(refresh.Command, Is.SameAs(vm.ReloadSamplesCommand));
            Assert.That(resnap.Command, Is.SameAs(vm.ResnapAllSamplesCommand));
            Assert.That(AutomationProperties.GetName(refresh), Is.EqualTo("Refresh samples"));
            Assert.That(AutomationProperties.GetName(resnap), Is.EqualTo("Resnap all samples and control points"));
            foreach (var action in new[] { refresh, resnap })
            {
                UiControlContract.AssertStyle<MenuItem>(action, "OHsIconActionMenuItemStyle");
                Assert.That(action.IsEnabled, Is.EqualTo(action.Command!.CanExecute(null)));
                Assert.That(action.IsTabStop, Is.False);
                Assert.That(action.Focusable, Is.True);
            }
            Assert.That(sample, Is.Not.Null, "Audio card needs sample creation action");
            UiControlContract.AssertStyle<MenuItem>(sample, "OHsActionMenuItemStyle");
            UiControlContract.AssertStyle<MenuItem>(instrument, "OHsActionMenuItemStyle");
            UiControlContract.AssertStyle<MenuItem>(toggle, "OHsWorkstationCollapseMenuItemStyle");
            Assert.Multiple(() =>
            {
                Assert.That(sample.Command, Is.SameAs(vm.AddSampleTrackCommand));
                Assert.That(instrument.Command, Is.SameAs(vm.AddPianoTrackCommand));
                Assert.That(toggle.Command, Is.SameAs(vm.ToggleTracksCollapsedCommand));
                Assert.That(sample.Header, Is.EqualTo("Sample Track"));
                Assert.That(instrument.Header, Is.EqualTo("Instrumental Track"));
                Assert.That(AutomationProperties.GetName(toggle), Is.EqualTo("Collapse all tracks"));
            });
            foreach (var action in new Control[] { sample, instrument, toggle })
            {
                Assert.That(KeyboardNavigation.GetIsTabStop(action), Is.False);
                Assert.That(action.Focusable, Is.True);
                Assert.That(action.FocusVisualStyle, Is.SameAs(page.FindResource("OHsSampleManagerKeyboardFocusStyle")));
                Assert.That(ToolTipService.GetShowsToolTipOnKeyboardFocus(action), Is.True);
                Assert.That(action.IsEnabled, Is.EqualTo(!ReferenceEquals(action, toggle)));
            }
            InvokeMenuItem(sample);
            Assert.That(vm.Tracks, Has.Count.EqualTo(1));
            InvokeMenuItem(instrument);
            Assert.That(vm.Tracks, Has.Count.EqualTo(2));
            InvokeMenuItem(toggle);
            view.UpdateLayout();
            Assert.That(vm.AreTracksCollapsed, Is.True);
            Assert.That(AutomationProperties.GetName(toggle), Is.EqualTo("Expand all tracks"));
            Assert.That(toggle.ToolTip, Is.EqualTo("Expand all tracks (F4)"));
            vm.IsReturning = true;
            view.UpdateLayout();
            Assert.That(sample.IsEnabled, Is.False);
            Assert.That(instrument.IsEnabled, Is.False);
            Assert.That(toggle.IsEnabled, Is.False);
            Assert.That(refresh.IsEnabled, Is.False);
            Assert.That(resnap.IsEnabled, Is.False);
            vm.IsReturning = false;
            view.UpdateLayout();
            Assert.That(sample.IsEnabled, Is.True);
            Assert.That(refresh.IsEnabled, Is.EqualTo(vm.ReloadSamplesCommand.CanExecute(null)));
            Assert.That(resnap.IsEnabled, Is.EqualTo(vm.ResnapAllSamplesCommand.CanExecute(null)));
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
            { Source = new Uri($"/Hitbench;component/Resources/Themes/{theme}.xaml", UriKind.Relative) };
        var node = new Sample { Timestamp = 125, FileName = "kick.wav" };
        var track = new Track { Name = "Kick", SelectedSampleFileName = "kick.wav", Volume = 64,
            Addition = "Drum", IsTrackMuted = true, IsTrackSoloed = true, Samples = [node] };
        var (page, vm, view) = Create(direction, new ProjectData { Offsets = [0], BPMs = [120], Tracks = [track] });
        using (view)
        {
            Assert.That(vm.SelectNode(track, node, false), Is.True);
            vm.Position = TimeSpan.FromMilliseconds(375);
            var name = Descendants<TextBlock>(page).Single(control => control.Name == "SampleTrackName");
            var file = Find<ComboBox>(page, "TrackSampleFileSelector");
            var grid = Descendants<BeatGridView>(page).Single();
            var secondary = new FrameworkElement[] { Find<ValueSlider>(page, "VolumeSlider"),
                Find<ToggleButton>(page, "TrackMuteToggle"), Find<ToggleButton>(page, "TrackSoloToggle") };
            UiControlContract.AssertStyle<ValueSlider>(secondary[0], "OHsWorkstationExpandedVolumeStyle");
            Assert.That(secondary[0].Style.BasedOn, Is.SameAs(secondary[0].FindResource("OHsWorkstationCompactVolumeStyle")));
            Assert.That(secondary[0].Style.BasedOn.BasedOn, Is.SameAs(secondary[0].FindResource("BaseValueSliderStyle")));
            for (var cycle = 0; cycle < 2; cycle++)
            {
                vm.ToggleTracksCollapsedCommand.Execute(null);
                view.UpdateLayout();
                Assert.That(name.IsVisible, Is.True);
                var configRow = Descendants<OHsWorkstationTrackRow>(page).Single();
                configRow.PresentSampleConfiguration = (_, _) => { };
                Assert.That(configRow.OpenSampleConfiguration(), Is.True);
                Assert.That(file.IsEnabled, Is.True);
                configRow.CloseSampleConfiguration();
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
            var toggle = WorkstationActionMenus.Find(page.Toolbar, vm.ToggleTracksCollapsedCommand);
            toggle.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = Mouse.PreviewMouseDownEvent });
            InvokeMenuItem(toggle);
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
            var list = (ListBox)((Hitbench.Views.Pages.OHsWorkstationTrackList)page.FindName("TrackListComponent")).FindName("TrackList");
            vm.ToggleTracksCollapsedCommand.Execute(null);
            var menu = ((BalloonComboBox)page.Toolbar.FindName("AddMenuButton")).ContextMenu;
            UiTestPump.Layout(menu, 320, 200);
            InvokeMenuItem((MenuItem)menu.Items[0]);
            list.ScrollIntoView(vm.Tracks.Last());
            view.UpdateLayout();
            Assert.That(vm.Tracks, Has.Count.EqualTo(81));
            var realized = Descendants<ValueSlider>(list).Where(control => control.Name == "VolumeSlider").ToArray();
            Assert.That(realized, Is.Not.Empty);
            Assert.That(realized.Length, Is.LessThan(81));
            Assert.That(realized.All(control => control.IsVisible == !((Track)control.DataContext).IsCollapsed), Is.True);
            Assert.That(vm.Tracks.Last().IsCollapsed, Is.False);
            Assert.That(Descendants<OHsWorkstationTrackRow>(list).Any(row => ReferenceEquals(row.Track, vm.Tracks.Last())), Is.True);
            vm.ToggleTracksCollapsedCommand.Execute(null);
            vm.ToggleTracksCollapsedCommand.Execute(null);
            list.ScrollIntoView(vm.Tracks[0]);
            view.UpdateLayout();
            Assert.That(Descendants<ValueSlider>(list).Where(control => control.Name == "VolumeSlider")
                .All(control => control.IsVisible), Is.True);
            Assert.That(Descendants<OHsWorkstationTrackRow>(list).Any(row => ReferenceEquals(row.Track, vm.Tracks[0])), Is.True);
            UiTestPump.Run(page.DeactivateAsync());
        }
    }

    [TestCase("VolumeSlider", 640)]
    [TestCase("TrackMuteToggle", 640)]
    [TestCase("VolumeSlider", 1280)]
    [TestCase("TrackMuteToggle", 1280)]
    public void CollapseRestoresLogicalFocusOnlyFromHiddenControls(string controlName, int width)
    {
        var track = new Track { SelectedSampleFileName = "kick.wav" };
        var (page, vm, view) = Create(project: new ProjectData { Offsets = [0], BPMs = [120], Tracks = [track] }, width: width);
        using (view)
        {
            var hidden = Find<FrameworkElement>(page, controlName);
            var toggle = (BalloonButton)page.Toolbar.FindName("ProjectActionsButton");
            var scope = FocusManager.GetFocusScope(toggle);
            page.RestoreTrackFocusBeforeCollapse(hidden);
            Assert.That(FocusManager.GetFocusedElement(scope), Is.SameAs(toggle));
            FocusManager.SetFocusedElement(scope, hidden);
            vm.ToggleTracksCollapsedCommand.Execute(null);
            view.UpdateLayout();
            Assert.That(FocusManager.GetFocusedElement(scope), Is.SameAs(toggle));
            var name = Descendants<TextBlock>(page).Single(control => control.Name == "SampleTrackName");
            FocusManager.SetFocusedElement(scope, name);
            page.RestoreTrackFocusBeforeCollapse(name);
            Assert.That(FocusManager.GetFocusedElement(scope), Is.SameAs(name));
            UiTestPump.Run(page.DeactivateAsync());
        }
    }

    private static void InvokeMenuItem(MenuItem item)
    {
        var provider = (System.Windows.Automation.Provider.IInvokeProvider)new System.Windows.Automation.Peers.MenuItemAutomationPeer(item)
            .GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke);
        provider.Invoke();
        UiTestPump.Run(item.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task);
    }

    private static void PressEnter(UIElement action)
    {
        using var source = new InMemoryPresentationSource();
        action.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Enter)
            { RoutedEvent = Keyboard.KeyDownEvent });
    }

    private static T Find<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        var control = Descendants<T>(root).SingleOrDefault(control => control.Name == name);
        return control ?? Descendants<OHsWorkstationTrackRow>(root).Select(row => row.FindName(name)).OfType<T>().First();
    }

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
        vm.IsAdditionMode = true; view.UpdateLayout();
        return (page, vm, view);
    }
}
