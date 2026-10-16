using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Automation;
using System.Windows.Input;
using System.Diagnostics;
using System.Text;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.ViewModels.Shell;
using osuHitsounder.Views.Pages;
using osuHitsounder.Services.FileSystem;
using osuHitsounder.Services.ProjectFiles;
using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Services.Ui;
using Track = osuHitsounder.Core.Entities.Track;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class WorkstationPageIntegrationTests
{
    [TestCase(FlowDirection.LeftToRight, "Dark")]
    [TestCase(FlowDirection.RightToLeft, "Light")]
    public void EditingModeControlsUpdateRealizedGrid(FlowDirection direction, string theme)
    {
        EnsureResources(theme);
        var track = new Track { SelectedSampleFileName = "a.wav" };
        var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav",
            new ProjectData { Offsets = [0], BPMs = [120], Tracks = [track] })) };
        var vm = new WorkstationPageVM(source, new osuHitsounder.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), new StubSampleFileService(["a.wav"]));
        var page = new WorkstationPage(vm); page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 1100, Height = 800, FlowDirection = direction });
        UiTestPump.Run(page.LoadCompletion); view.UpdateLayout();
        var selection = (RadioButton)page.FindName("SelectionModeButton");
        var addition = (RadioButton)page.FindName("AdditionModeButton");
        var grid = FindVisualChild<BeatGridView>(page)!;
        foreach (var mode in new[] { selection, addition, selection })
        {
            mode.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            view.UpdateLayout();
            Assert.That(selection.IsChecked, Is.EqualTo(vm.IsSelectionMode));
            Assert.That(addition.IsChecked, Is.EqualTo(vm.IsAdditionMode));
            Assert.That(grid.AllowNodeCreation, Is.EqualTo(vm.IsAdditionMode));
            UiControlContract.AssertStyle<RadioButton>(mode, "OHsTimelineEditingModeButtonStyle");
        }
        Assert.That(AutomationProperties.GetName(selection), Is.EqualTo("Selection Mode"));
        Assert.That(AutomationProperties.GetName(addition), Is.EqualTo("Addition Mode"));
        vm.SelectedTrack = track;
        vm.IsAdditionMode = true; view.UpdateLayout();
        Assert.That(vm.SelectedTrack, Is.SameAs(track));
        UiControlContract.AssertStyle<ListBoxItem>((ListBoxItem)((ListBox)page.FindName("TrackList")).ItemContainerGenerator.ContainerFromIndex(0), "OHsWorkstationTrackItemStyle");
        vm.IsReturning = true; view.UpdateLayout();
        Assert.That(selection.IsEnabled, Is.False);
        Assert.That(addition.IsEnabled, Is.False);
        UiTestPump.Run(page.DeactivateAsync());
    }

    [TestCase("page", false)]
    [TestCase("SongAudioCard", false)]
    [TestCase("PlayPauseButton", false)]
    [TestCase("ZoomFooter", true)]
    [TestCase("track", true)]
    public void TracksCardBoundaryControlsPointerDeselection(string originName, bool inside)
    {
        var node = new Sample { Timestamp = 1000, FileName = "a.wav" };
        var track = new Track { SelectedSampleFileName = "a.wav", Samples = [node] };
        var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav",
            new ProjectData { Offsets = [0], BPMs = [120], Tracks = [track] })) };
        var vm = new WorkstationPageVM(source, new osuHitsounder.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), new StubSampleFileService(["a.wav"]));
        var page = new WorkstationPage(vm); page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 1100, Height = 800 });
        UiTestPump.Run(page.LoadCompletion); view.UpdateLayout();
        vm.SelectNode(track, node, false);
        var list = (ListBox)page.FindName("TrackList");
        var origin = originName switch
        {
            "page" => (UIElement)page,
            "track" => FindVisualChildren<TextBox>(page).Single(input => ReferenceEquals(input.DataContext, track)),
            _ => (UIElement)page.FindName(originName)
        };
        var click = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            { RoutedEvent = Mouse.PreviewMouseDownEvent };
        origin.RaiseEvent(click);
        Assert.That(vm.SelectedTrack, inside ? Is.SameAs(track) : Is.Null);
        Assert.That(list.SelectedItem, inside ? Is.SameAs(track) : Is.Null);
        Assert.That(vm.SelectedSamples, Is.Empty);
        Assert.That(click.Handled, Is.False);
        UiTestPump.Run(page.DeactivateAsync());
    }

    [TestCase(ModifierKeys.None)]
    [TestCase(ModifierKeys.Control)]
    [TestCase(ModifierKeys.Alt)]
    [TestCase(ModifierKeys.Shift)]
    [TestCase(ModifierKeys.Control | ModifierKeys.Shift)]
    [TestCase(ModifierKeys.Control | ModifierKeys.Alt)]
    public void OnlyShiftWheelDisablesMarkerFollow(ModifierKeys modifiers)
    {
        EnsureResources("Dark");
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new osuHitsounder.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm); page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 700 });
        UiTestPump.Run(page.LoadCompletion);
        foreach (var delta in new[] { 120, 60, -60, 0 })
        {
            vm.ScrollWithMarker = true;
            page.HandleWaveformWheel(modifiers, delta);
            Assert.That(vm.ScrollWithMarker, Is.EqualTo(!(modifiers == ModifierKeys.Shift && delta != 0)), $"Direct: {modifiers}, {delta}");
            vm.ScrollWithMarker = true;
            page.HandleContextWheel(modifiers, delta, page.SongAudioCard);
            Assert.That(vm.ScrollWithMarker, Is.EqualTo(!(modifiers == ModifierKeys.Shift && delta != 0)), $"Context: {modifiers}, {delta}");
        }
        if (modifiers == ModifierKeys.Alt)
        {
            vm.ScrollWithMarker = true;
            page.HandleContextWheel(modifiers, 120, page);
            page.HandleContextWheel(modifiers, 120, page);
            Assert.That(vm.ScrollWithMarker, Is.True);
        }
        UiTestPump.Run(page.DeactivateAsync());
    }

    [Test]
    public void UnreadyAndDeactivatedWheelPreservesMarkerFollow()
    {
        EnsureResources("Dark");
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new osuHitsounder.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        page.HandleContextWheel(ModifierKeys.Shift, 120, page);
        Assert.That(vm.ScrollWithMarker, Is.True);
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 700 });
        UiTestPump.Run(page.LoadCompletion);
        UiTestPump.Run(page.DeactivateAsync());
        vm.ScrollWithMarker = true;
        page.HandleContextWheel(ModifierKeys.Shift, 120, page);
        Assert.That(vm.ScrollWithMarker, Is.True);
    }

    [Test]
    public void EditingModesAndScrollDragKeepPublicContracts()
    {
        EnsureResources("Dark");
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new osuHitsounder.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm); page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 700 });
        UiTestPump.Run(page.LoadCompletion);
        var addition = (RadioButton)page.FindName("AdditionModeButton");
        var selection = (RadioButton)page.FindName("SelectionModeButton");
        Assert.That(addition.IsChecked, Is.True);
        UiControlContract.AssertStyle<RadioButton>(selection, "OHsTimelineEditingModeButtonStyle");
        selection.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, true);
        Assert.That(vm.IsSelectionMode, Is.True);
        Assert.That(addition.IsChecked, Is.False);
        var bar = (ScrollBar)page.FindName("WaveformScrollBar");
        vm.ScrollWithMarker = true;
        bar.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0, 0) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragStartedEvent });
        Assert.That(vm.ScrollWithMarker, Is.False);
        UiTestPump.Run(page.DeactivateAsync());
    }
    [Test]
    public void NarrowWorkstationLoadsAndRecoversTimelineOnResize()
    {
        var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(),
            new osuHitsounder.Services.MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm); page.SetProjectPath("project.json");
        var window = new Window { Content = page, Width = 180, Height = 600 };
        using var view = new InMemoryView(window);
        UiTestPump.Run(page.LoadCompletion);
        Assert.That(vm.CanPlay, Is.True);
        window.Width = 600; view.UpdateLayout();
        UiTestPump.Run(vm.SeekCommand.ExecuteAsync(TimeSpan.FromSeconds(1)));
        Assert.That(audio.Snapshot.Position, Is.EqualTo(TimeSpan.FromSeconds(1)));
        Assert.That(vm.CanPlay, Is.True);
        UiTestPump.Run(page.DeactivateAsync());
    }

    [Test]
    public void TrackCollectionRecyclesAuditionStateAndSharesTimelineInputs()
    {
        var tracks = Enumerable.Range(0, 100).Select(i => new Track { Name = i.ToString(), IsTrackMuted = i == 99,
            IsTrackSoloed = i == 99 }).ToList();
        var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(
            new WorkstationSource("map.osu", "audio.wav", new ProjectData { Tracks = tracks, Offsets = [0], BPMs = [120] })) };
        var saves = new DelayedProjectFileService(); saves.Release();
        var vm = new WorkstationPageVM(source, new osuHitsounder.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay),
            new ImmediateUiScheduler(), projectFiles: saves);
        var page = new WorkstationPage(vm); page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 600, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);
        var list = (ListBox)page.FindName("TrackList");
        Assert.That(list.ItemContainerGenerator.ContainerFromIndex(99), Is.Null);
        list.ScrollIntoView(tracks[99]); view.UpdateLayout();
        var container = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(99)!;
        Assert.That(container, Is.Not.Null);
        var mute = FindVisualChildren<ToggleButton>(container).Single(control => control.Name == "TrackMuteToggle");
        Assert.That(mute.IsChecked, Is.True);
        page.TimelineViewportWidth = 210; page.TimelineContentWidth = 1678; page.TimelineHorizontalOffset = 123.75;
        var grid = FindVisualChild<BeatGridView>(container)!;
        Assert.That(grid.ViewportWidth, Is.EqualTo(210));
        Assert.That(grid.ContentWidth, Is.EqualTo(1678));
        Assert.That(grid.HorizontalOffset, Is.EqualTo(123.75));
        Assert.That(saves.Writes, Is.Zero);
        UiTestPump.Run(page.DeactivateAsync());
    }

    [TestCase(480d, FlowDirection.RightToLeft, "Light")]
    [TestCase(1100d, FlowDirection.LeftToRight, "Dark")]
    public void TrackAuditionControlsUseExpectedResourcesAndRecycleWithoutMutation(double width, FlowDirection direction, string theme)
    {
        EnsureResources(theme);
        var a = new Track { Name = "กลอง " + new string('a', 100), IsTrackMuted = true, IsTrackSoloed = true };
        var b = new Track { Name = "Snare", IsTrackSoloed = true };
        var json = ProjectDataSerializer.Serialize(new ProjectData { Tracks = [a, b], Offsets = [0], BPMs = [120] });
        var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(
            new WorkstationSource("map.osu", "audio.wav", ProjectDataSerializer.Deserialize(json))) };
        var audio = new StubWorkstationAudioService(); var saves = new DelayedProjectFileService(); saves.Release();
        var vm = new WorkstationPageVM(source, new osuHitsounder.Services.MarkerPlaybackCoordinator(audio, Task.Delay),
            new ImmediateUiScheduler(), projectFiles: saves);
        var page = new WorkstationPage(vm); page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = width, Height = 900, FlowDirection = direction });
        UiTestPump.Run(page.LoadCompletion); view.UpdateLayout();
        a = vm.Tracks[0]; b = vm.Tracks[1];
        var mute = FindVisualChildren<ToggleButton>(page).Single(control => control.Name == "TrackMuteToggle" && ReferenceEquals(control.DataContext, a));
        var solo = FindVisualChildren<ToggleButton>(page).Single(control => control.Name == "TrackSoloToggle" && ReferenceEquals(control.DataContext, a));
        Assert.That(mute.GetType(), Is.EqualTo(typeof(ToggleButton)));
        Assert.That(mute.IsTabStop, Is.True);
        Assert.That(mute.Style, Is.SameAs(page.FindResource("OHsWorkstationTrackToggleStyle")));
        Assert.That(mute.Style.BasedOn, Is.SameAs(page.FindResource("BaseToggleButtonStyle")));
        Assert.That(mute.Template, Is.SameAs(page.FindResource("OHsWorkstationTrackToggleTemplate")));
        Assert.That(mute.IsChecked, Is.True); Assert.That(solo.IsChecked, Is.True);
        var remove = FindVisualChildren<BalloonButton>(page).Single(control => ReferenceEquals(control.DataContext, a) &&
            AutomationProperties.GetName(control) == "Remove track");
        Assert.That(remove.Style, Is.SameAs(page.FindResource("OHsWorkstationRemoveTrackStyle")));
        Assert.That(remove.Style.BasedOn, Is.SameAs(page.FindResource("OHsBalloonDestructiveActionStyle")));
        Assert.That(remove.CommandParameter, Is.SameAs(a));
        var updates = audio.SampleUpdates.Count;
        mute.DataContext = b; solo.DataContext = b;
        Assert.That(mute.IsChecked, Is.False); Assert.That(solo.IsChecked, Is.True);
        Assert.That(AutomationProperties.GetName(mute), Is.EqualTo("Mute track"));
        Assert.That(saves.Writes, Is.Zero); Assert.That(audio.SampleUpdates.Count, Is.EqualTo(updates));
        mute.IsChecked = true; UiTestPump.Run(vm.FlushSavesAsync());
        Assert.That(b.IsTrackMuted, Is.True);
        Assert.That(ProjectDataSerializer.Deserialize(saves.LatestJson!).Tracks[1].IsTrackMuted, Is.True);
        UiTestPump.Run(page.DeactivateAsync());
    }

    [Test]
    public void TrackToggleSpaceControlsTransport()
    {
        var track = new Track(); var audio = new StubWorkstationAudioService();
        var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(
            new WorkstationSource("map.osu", "audio.wav", new ProjectData { Tracks = [track], Offsets = [0], BPMs = [120] })) };
        var vm = new WorkstationPageVM(source, new osuHitsounder.Services.MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm); page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 700 });
        UiTestPump.Run(page.LoadCompletion);
        var toggle = new ToggleButton { DataContext = track };
        Assert.That(page.HandlePlaybackKey(Key.Space, ModifierKeys.None, false, toggle), Is.True);
        UiTestPump.Run(page.PlaybackInputCompletion);
        Assert.That(audio.Snapshot.IsPlaying, Is.True);
        var mute = FindVisualChildren<ToggleButton>(page).Single(control => control.Name == "TrackMuteToggle");
        ((System.Windows.Automation.Provider.IToggleProvider)new System.Windows.Automation.Peers.ToggleButtonAutomationPeer(mute)).Toggle();
        Assert.That(track.IsTrackMuted, Is.True);
        UiTestPump.Run(page.PlaybackInputCompletion);
        Assert.That(audio.Snapshot.IsPlaying, Is.True);
        UiTestPump.Run(page.DeactivateAsync());
    }

    [Test]
    public void FileRefreshPreservesMetadataOptionsAndSampleIdentity()
    {
        EnsureResources("Dark");
        var track = new Track { SelectedSampleFileName = "kick.wav", Addition = "Soft",
            Samples = [new Sample { FileName = "kick.wav" }] };
        IReadOnlyList<string> choices = ["kick.wav"];
        var files = new StubSampleFileService([]) { List = (_, _) => Task.FromResult(choices) };
        var vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(
            new WorkstationSource("map.osu", "audio.wav", new ProjectData { Tracks = [track] })) },
            new osuHitsounder.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay),
            new ImmediateUiScheduler(), files, new StubSampleFileDialogService("snare.wav"));
        var page = new WorkstationPage(vm); page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 1100, Height = 900 });
        UiTestPump.Run(page.LoadCompletion); view.UpdateLayout();
        choices = ["kick.wav", "snare.wav"];
        UiTestPump.Run(vm.ImportSamplesAsync());
        var selectors = FindVisualChildren<ComboBox>(page).Where(box => ReferenceEquals(box.DataContext, track)).ToArray();
        Assert.That(selectors.Single(box => box.Name == "TrackAdditionSelector").SelectedItem, Is.EqualTo("Soft"));
        Assert.That(selectors.Single(box => box.Name == "TrackAdditionSelector").Items.Cast<string>(),
            Is.EqualTo(new[] { "None", "Normal", "Soft", "Drum" }));
        Assert.That(track.SelectedSampleFileName, Is.EqualTo("kick.wav"));
        Assert.That(track.Samples.Single().FileName, Is.EqualTo("kick.wav"));
        UiTestPump.Run(page.DeactivateAsync());
    }

    [TestCase(480d, FlowDirection.LeftToRight, "Dark")]
    [TestCase(1100d, FlowDirection.RightToLeft, "Light")]
    public void TrackMetadataSelectorsInitializeEditAndRecycleIndependently(double width, FlowDirection direction, string theme)
    {
        EnsureResources(theme);
        var node = new Sample { FileName = "kick.wav", Timestamp = 125 };
        var first = new Track { SelectedSampleFileName = "kick.wav", Addition = "Soft", Samples = [node] };
        var second = new Track { Addition = "None" };
        var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(
            new WorkstationSource("map.osu", "audio.wav", new ProjectData { Tracks = [first, second] })) };
        var saves = new DelayedProjectFileService(); saves.Release();
        var vm = new WorkstationPageVM(source, new osuHitsounder.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay),
            new ImmediateUiScheduler(), new StubSampleFileService(["kick.wav"]), projectFiles: saves);
        var page = new WorkstationPage(vm); page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = width, Height = 900, FlowDirection = direction });
        UiTestPump.Run(page.LoadCompletion); view.UpdateLayout();
        var selectors = FindVisualChildren<ComboBox>(page).Where(box => ReferenceEquals(box.DataContext, first)).ToArray();
        Assert.That(selectors.Select(box => box.Name), Does.Not.Contain("TrackSamplesetSelector"));
        var addition = selectors.Single(box => box.Name == "TrackAdditionSelector");
        Assert.That(addition.Items.Cast<string>(), Is.EqualTo(new[] { "None", "Normal", "Soft", "Drum" }));
        Assert.That(addition.SelectedItem, Is.EqualTo("Soft"));
        Assert.That(addition.ItemTemplate, Is.Null, "Use the native full-string presentation for both selection and options.");
        Assert.That(addition.Style, Is.SameAs(page.FindResource("OHsWorkstationTrackMetadataComboBoxStyle")));
        Assert.That(addition.Style, Is.SameAs(page.FindResource("OHsWorkstationTrackMetadataComboBoxStyle")));
        Assert.That(addition.IsTabStop, Is.False);
        Assert.That(saves.Writes, Is.Zero);
        addition.SelectedItem = "None";
        UiTestPump.Run(vm.FlushSavesAsync());
        Assert.That(first.Addition, Is.EqualTo("None"));
        Assert.That(node.FileName, Is.EqualTo("kick.wav"));
        var writes = saves.Writes;
        addition.DataContext = second;
        Assert.That(saves.Writes, Is.EqualTo(writes));
        page.ShowVolumeOverlay(false, new Point(), false, false);
        Assert.That(addition.IsEnabled, Is.False);
        page.DismissVolumeOverlay();
        Assert.That(addition.IsEnabled, Is.True);
        UiTestPump.Run(page.DeactivateAsync());
    }

    [TestCase(480d, FlowDirection.LeftToRight, "Dark")]
    [TestCase(1100d, FlowDirection.RightToLeft, "Light")]
    public void TrackNameFieldInitializesEditsSavesAndRebindsWithoutSpuriousWrites(double width, FlowDirection direction, string theme)
    {
        EnsureResources(theme);
        var first = new osuHitsounder.Core.Entities.Track { Name = "Kick " + new string('a', 256) };
        var second = new osuHitsounder.Core.Entities.Track { Name = "Snare" };
        var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(
            new WorkstationSource("map.osu", "audio.wav", new ProjectData { Tracks = [first, second] })) };
        var saves = new DelayedProjectFileService();
        saves.Release();
        var vm = new WorkstationPageVM(source, new osuHitsounder.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay),
            new ImmediateUiScheduler(), projectFiles: saves);
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = width, Height = 720, FlowDirection = direction });
        UiTestPump.Run(page.LoadCompletion);
        view.UpdateLayout();
        var input = FindVisualChildren<TextBox>(page).Single(box =>
            AutomationProperties.GetName(box) == "Track name" && ReferenceEquals(box.DataContext, first));
        Assert.That(input.Text, Is.EqualTo(first.Name));
        Assert.That(input.IsEnabled, Is.True);
        Assert.That(input.IsTabStop, Is.True);
        Assert.That(input.Focusable, Is.True);
        Assert.That(input.Style, Is.SameAs(page.FindResource("OHsWorkstationTrackNameStyle")));
        Assert.That(input.Style.BasedOn, Is.SameAs(page.FindResource("RoundedTextInputStyle")));
        Assert.That(saves.Writes, Is.Zero);

        input.Text = "กลอง";
        UiTestPump.Run(vm.FlushSavesAsync());
        Assert.That(first.Name, Is.EqualTo("กลอง"));
        Assert.That(ProjectDataSerializer.Deserialize(saves.LatestJson!).Tracks[0].Name, Is.EqualTo("กลอง"));
        var writes = saves.Writes;
        input.DataContext = second;
        UiTestPump.Run(vm.FlushSavesAsync());
        Assert.That(input.Text, Is.EqualTo("Snare"));
        Assert.That(first.Name, Is.EqualTo("กลอง"));
        Assert.That(second.Name, Is.EqualTo("Snare"));
        Assert.That(saves.Writes, Is.EqualTo(writes));
        input.Text = "";
        UiTestPump.Run(vm.FlushSavesAsync());
        Assert.That(second.Name, Is.Empty);
        Assert.That(ProjectDataSerializer.Deserialize(saves.LatestJson!).Tracks[1].Name, Is.Empty);

        UiTestPump.Run(vm.DeactivateAsync());
        view.UpdateLayout();
        Assert.That(input.IsEnabled, Is.False);
        input.Text = "Cannot edit";
        Assert.That(second.Name, Is.Empty);
    }

    [Test]
    public void SongVolumeWheelUpdatesSongOnlyAndExposesAccessibleBounds()
    {
        var track = new osuHitsounder.Core.Entities.Track { Volume = 70 };
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav",
                new ProjectData { Tracks = [track] }))
        };
        var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(source, new osuHitsounder.Services.MarkerPlaybackCoordinator(audio, (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 640, Height = 480 });
        UiTestPump.Run(page.LoadCompletion);
        view.UpdateLayout();

        var slider = (VolumeWheel)page.FindName("SongVolumeWheel");
        slider.Value = 50;
        slider.GetBindingExpression(RangeBase.ValueProperty)!.UpdateSource();

        Assert.Multiple(() =>
        {
            Assert.That(vm.SongVolume, Is.EqualTo(50));
            Assert.That(audio.SongVolume, Is.EqualTo(50));
            Assert.That(track.Volume, Is.EqualTo(70));
            Assert.That(slider.Minimum, Is.Zero);
            Assert.That(slider.Maximum, Is.EqualTo(100));
            Assert.That(slider.SmallChange, Is.EqualTo(5));
            Assert.That(slider.IsEnabled, Is.True);
            Assert.That(AutomationProperties.GetName(slider), Is.EqualTo("Song volume"));
        });
    }

    [Test]
    public void SongVolumeWheelIsDisabledWhileLoading()
    {
        var completion = new TaskCompletionSource<WorkstationSource>();
        var vm = new WorkstationPageVM(
            new StubWorkstationSourceService { Load = (_, _) => completion.Task }, new osuHitsounder.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 640, Height = 480 });
        view.UpdateLayout();

        var slider = (VolumeWheel)page.FindName("SongVolumeWheel");
        Assert.That(slider.IsEnabled, Is.False);

        completion.SetResult(new WorkstationSource("map.osu", "audio.wav", new ProjectData()));
        UiTestPump.Run(page.LoadCompletion);
    }

    [Test]
    public void RemovalActionUsesTrackParameterAndFinalRemovalExplainsEmptyState()
    {
        var track = new osuHitsounder.Core.Entities.Track();
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav",
                new ProjectData { Tracks = [track] }))
        };
        var dialog = new StubTrackRemovalDialogService { Confirm = _ => Task.FromResult(true) };
        var vm = new WorkstationPageVM(source, new osuHitsounder.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)),
            new ImmediateUiScheduler(), trackRemovalDialog: dialog);
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 640, Height = 480 });
        UiTestPump.Run(page.LoadCompletion);
        view.UpdateLayout();
        var button = FindVisualChildren<BalloonButton>(page)
            .Single(action => AutomationProperties.GetName(action) == "Remove track");
        Assert.That(button.CommandParameter, Is.SameAs(track));
        Assert.That(button.Style, Is.SameAs(page.FindResource("OHsWorkstationRemoveTrackStyle")));
        Assert.That(button.Style.BasedOn, Is.SameAs(page.FindResource("OHsBalloonDestructiveActionStyle")));
        Assert.That(AutomationProperties.GetName(button), Is.EqualTo("Remove track"));
        UiTestPump.Run(vm.RemoveTrackCommand.ExecuteAsync(button.CommandParameter));
        view.UpdateLayout();
        var list = (ListBox)page.FindName("TrackList");
        Assert.That(list.Items, Is.Empty);
        Assert.That(FindVisualChildren<TextBlock>(list).Any(text => text.Text.Contains("No tracks")), Is.False);
        Assert.That(vm.AddPianoTrackCommand.CanExecute(null), Is.True);
        UiTestPump.Run(page.DeactivateAsync());
    }

    [Test]
    public void RemovalActionDisablesAfterDeactivation()
    {
        var track = new osuHitsounder.Core.Entities.Track();
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav",
                new ProjectData { Tracks = [track] }))
        };
        var vm = new WorkstationPageVM(source, new osuHitsounder.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)),
            new ImmediateUiScheduler(), trackRemovalDialog: new StubTrackRemovalDialogService());
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 640, Height = 480 });
        UiTestPump.Run(page.LoadCompletion);
        view.UpdateLayout();
        var button = FindVisualChildren<BalloonButton>(page)
            .Single(action => AutomationProperties.GetName(action) == "Remove track");
        Assert.That(button.IsEnabled, Is.True);
        UiTestPump.Run(page.DeactivateAsync());
        view.UpdateLayout();
        Assert.That(button.IsEnabled, Is.False);
    }

    [Test]
    public void RemovalFocusFallsToNextTrackThenWaveform()
    {
        var first = new osuHitsounder.Core.Entities.Track();
        var second = new osuHitsounder.Core.Entities.Track();
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav",
                new ProjectData { Tracks = [first, second] }))
        };
        var dialog = new StubTrackRemovalDialogService { Confirm = _ => Task.FromResult(true) };
        var vm = new WorkstationPageVM(source, new osuHitsounder.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)),
            new ImmediateUiScheduler(), trackRemovalDialog: dialog);
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);
        UiTestPump.Run(vm.RemoveTrackCommand.ExecuteAsync(first));
        view.UpdateLayout();
        var next = page.FindRemovalFocusTarget(0);
        Assert.That(next, Is.TypeOf<BalloonButton>());
        Assert.That(((FrameworkElement)next).DataContext, Is.SameAs(second));
        UiTestPump.Run(vm.RemoveTrackCommand.ExecuteAsync(second));
        view.UpdateLayout();
        Assert.That(page.FindRemovalFocusTarget(0),
            Is.SameAs(page.FindName("WaveformScrollViewer")));
    }
    [SetUp]
    public void SetUp() => EnsureResources();

    [Test]
    public void WaveformUsesExpectedControlType()
    {
        var page = new WorkstationPage(new WorkstationPageVM(new StubWorkstationSourceService(), new osuHitsounder.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler()));
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600 });
        view.UpdateLayout();
        var waveform = (WaveformView)page.FindName("WaveformDisplay");
        Assert.That(waveform, Is.TypeOf<WaveformView>());
        UiControlContract.AssertStyle<ScrollViewer>((ScrollViewer)page.FindName("WaveformScrollViewer"), "OHsWorkstationWaveformScrollStyle");
    }

    [Test]
    public void LoadsWaveformBindsTransportStateAndKeepsMinimumZoomScrollable()
    {
        var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new osuHitsounder.Services.MarkerPlaybackCoordinator(audio, (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        var window = new Window { Content = page, Width = 900, Height = 600, ShowInTaskbar = false };
        using var view = new InMemoryView(window);
        UiTestPump.Run(page.LoadCompletion);
        view.UpdateLayout();

        var play = (BalloonButton)page.FindName("PlayPauseButton");
        UiControlContract.AssertStyle<BalloonButton>(play, "OHsWorkstationPlayPauseStyle");
        Assert.That(vm.IsPlaying, Is.False);
        osuHitsounder.Test.Presentation.UiTestPump.Run(vm.PlayPauseCommand.ExecuteAsync(null));
        view.UpdateLayout();
        Assert.That(vm.IsPlaying, Is.True);
        UiControlContract.AssertStyle<BalloonButton>(play, "OHsWorkstationPlayPauseStyle");

        var scroll = (ScrollViewer)page.FindName("WaveformScrollViewer");
        var waveform = (WaveformView)page.FindName("WaveformDisplay");
        Assert.That(waveform.DesiredSize.Width, Is.GreaterThan(scroll.ViewportWidth));
        scroll.ScrollToHorizontalOffset(50);
        view.UpdateLayout();
        Assert.Multiple(() =>
        {
            Assert.That(vm.ZoomFactor, Is.EqualTo(32));
            Assert.That(scroll.HorizontalOffset, Is.EqualTo(50).Within(1));
        });
    }

    [Test]
    public void ExposesTrackListFooterAndAccessibleViewport()
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new osuHitsounder.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)),
            new ImmediateUiScheduler(), new StubSampleFileService(["a.wav"]));
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        var window = new Window { Content = page, Width = 640, Height = 480, FlowDirection = FlowDirection.RightToLeft };
        using var view = new InMemoryView(window);
        UiTestPump.Run(page.LoadCompletion);
        view.UpdateLayout();

        Assert.Multiple(() =>
        {
            Assert.That(((ItemsControl)page.FindName("TrackList")).ItemsSource, Is.SameAs(vm.Tracks));
            Assert.That(((ScrollViewer)page.FindName("WaveformScrollViewer")).Focusable, Is.True);
        });
    }

    [TestCase(FlowDirection.LeftToRight)]
    [TestCase(FlowDirection.RightToLeft)]
    public void AddedTrackCardUsesWaveformViewportAndOffset(FlowDirection direction)
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new osuHitsounder.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)),
            new ImmediateUiScheduler(), new StubSampleFileService(["a.wav"]));
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        var window = new Window { Content = page, Width = 880, Height = 600, FlowDirection = direction };
        using var view = new InMemoryView(window);
        UiTestPump.Run(page.LoadCompletion);
        vm.AddSampleTrackCommand.Execute(null);
        view.UpdateLayout();
        var list = (ItemsControl)page.FindName("TrackList");
        Assert.That(list.Items, Has.Count.EqualTo(1));
        var card = (FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(0)!;
        var grid = FindBeatGrid(card)!;
        Assert.Multiple(() =>
        {
            Assert.That(grid.ContentWidth, Is.EqualTo(page.TimelineContentWidth));
            Assert.That(grid.ViewportWidth, Is.EqualTo(page.TimelineViewportWidth));
            Assert.That(grid.HorizontalOffset, Is.EqualTo(page.TimelineHorizontalOffset));
            Assert.That(grid.Subdivision, Is.EqualTo(4));
            Assert.That(grid.FlowDirection, Is.EqualTo(FlowDirection.LeftToRight));
        });
    }

    [Test]
    public void ReusedCardControlsReadCurrentTrackWithoutEditingPreviousTrack()
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new osuHitsounder.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)),
            new ImmediateUiScheduler(), new StubSampleFileService(["a.wav"]));
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        var window = new Window { Content = page, Width = 880, Height = 600 };
        using var view = new InMemoryView(window);
        UiTestPump.Run(page.LoadCompletion);
        vm.AddSampleTrackCommand.Execute(null);
        vm.AddSampleTrackCommand.Execute(null);
        var first = vm.Tracks[0];
        var second = vm.Tracks[1];
        vm.SetTrackVolume(first, 20);
        vm.SetTrackVolume(second, 80);
        view.UpdateLayout();
        var list = (ItemsControl)page.FindName("TrackList");
        var card = (FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(0)!;
        var slider = FindVisualChild<ValueSlider>(card)!;
        slider.DataContext = second;
        Assert.Multiple(() =>
        {
            Assert.That(slider.Value, Is.EqualTo(80));
            Assert.That(slider.DisplayText, Is.EqualTo("80"));
            Assert.That(first.Volume, Is.EqualTo(20));
            Assert.That(second.Volume, Is.EqualTo(80));
        });
    }

    [Test]
    public void ImportRefreshPreservesOtherCardSelection()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        try
        {
            var projectPath = Path.Combine(root.FullName, "project.json");
            File.WriteAllText(projectPath, "{}");
            var samples = Directory.CreateDirectory(Path.Combine(root.FullName, "Samples"));
            File.WriteAllBytes(Path.Combine(samples.FullName, "a.wav"), [1]);
            var sourcePath = Path.Combine(root.FullName, "b.mp3");
            File.WriteAllBytes(sourcePath, [2]);
            var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new osuHitsounder.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)),
                new UiScheduler(Dispatcher.CurrentDispatcher), new SampleFileService(), new StubSampleFileDialogService(sourcePath),
                new ProjectFileService(root.FullName), new WeakReferenceMessenger());
            var page = new WorkstationPage(vm);
            page.SetProjectPath(projectPath);
            var window = new Window { Content = page, Width = 880, Height = 700 };
            using var view = new InMemoryView(window);
            UiTestPump.Run(page.LoadCompletion);
            vm.AddSampleTrackCommand.Execute(null);
            vm.AddSampleTrackCommand.Execute(null);
            view.UpdateLayout();
            var list = (ItemsControl)page.FindName("TrackList");
            var otherCard = (FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(1)!;
            var selector = FindVisualChildren<ComboBox>(otherCard).Single(box => box.Name == "TrackSampleFileSelector");
            Assert.That(selector.SelectedItem, Is.EqualTo("a.wav"));
            UiTestPump.Run(vm.ImportSamplesAsync());
            view.UpdateLayout();
            Assert.That(selector.SelectedItem, Is.EqualTo("a.wav"));
            UiTestPump.Run(vm.FlushSavesAsync());
        }
        finally { Directory.Delete(root.FullName, true); }
    }

    [Test]
    public void ManyTracksKeepOnlyViewportCardsRealized()
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new osuHitsounder.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)),
            new ImmediateUiScheduler(), new StubSampleFileService(["a.wav"]));
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        var window = new Window { Content = page, Width = 880, Height = 600 };
        using var view = new InMemoryView(window);
        UiTestPump.Run(page.LoadCompletion);
        for (var index = 0; index < 80; index++) vm.AddSampleTrackCommand.Execute(null);
        view.UpdateLayout();
        var list = (ItemsControl)page.FindName("TrackList");
        Assert.That(list.Items, Has.Count.EqualTo(80));
        Assert.That(FindVisualChildren<ContentPresenter>(list).Count(), Is.LessThan(80));
    }

    [Test]
    public void ReloadRestoresTrackSelectionsAfterSampleFilesArrive()
    {
        var choices = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var listing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tracks = new[]
        {
            new osuHitsounder.Core.Entities.Track { SelectedSampleFileName = "kick.wav", Samples = [new Sample { FileName = "kick.wav" }] },
            new osuHitsounder.Core.Entities.Track { Samples = [new Sample { FileName = "Snare.wav" }] },
            new osuHitsounder.Core.Entities.Track { SelectedSampleFileName = "missing.wav", Samples = [new Sample { FileName = "missing.wav" }] }
        };
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav", new ProjectData { Tracks = [.. tracks] }))
        };
        var files = new StubSampleFileService([])
        {
            List = (_, _) => { listing.TrySetResult(); return choices.Task; }
        };
        var vm = new WorkstationPageVM(source, new osuHitsounder.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)),
            new UiScheduler(Dispatcher.CurrentDispatcher), files);
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 880, Height = 1200 });
        UiTestPump.Run(listing.Task);
        view.UpdateLayout();
        var list = (ItemsControl)page.FindName("TrackList");
        var cards = Enumerable.Range(0, 3).Select(index => (FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(index)!).ToArray();
        Assert.That(cards, Has.All.Not.Null);
        UiTestPump.Run(Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task);
        Assert.That(FindVisualChildren<ComboBox>(cards[0]).Single(box => box.Name == "TrackSampleFileSelector").SelectedItem, Is.Null);
        choices.SetResult(["Kick.wav", "Snare.wav"]);
        UiTestPump.Run(page.LoadCompletion);
        view.UpdateLayout();

        Assert.Multiple(() =>
        {
            Assert.That(FindVisualChildren<ComboBox>(cards[0]).Single(box => box.Name == "TrackSampleFileSelector").SelectedItem, Is.EqualTo("Kick.wav"));
            Assert.That(FindVisualChildren<ComboBox>(cards[1]).Single(box => box.Name == "TrackSampleFileSelector").SelectedItem, Is.EqualTo("Snare.wav"));
            foreach (var card in cards.Take(2))
                Assert.That(FindVisualChildren<TextBlock>(card).Single(block => block.Name == "MissingGridStatus").Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(FindVisualChildren<ComboBox>(cards[2]).Single(box => box.Name == "TrackSampleFileSelector").SelectedItem, Is.Null);
            Assert.That(FindVisualChildren<TextBlock>(cards[2]).Single(block => block.Name == "MissingGridStatus").Text, Is.EqualTo("Sample not found."));
            Assert.That(tracks[0].Samples.Single().FileName, Is.EqualTo("kick.wav"));
            Assert.That(tracks[1].Samples.Single().FileName, Is.EqualTo("Snare.wav"));
            Assert.That(tracks[2].Samples.Single().FileName, Is.EqualTo("missing.wav"));
            Assert.That(vm.SamplesRevision, Is.Zero);
        });
    }

    [Test]
    public void MissingSavedSampleIsShownWithoutChangingItsFilename()
    {
        var track = new osuHitsounder.Core.Entities.Track { Samples = [new Sample { FileName = "missing.wav" }] };
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav", new ProjectData { Tracks = [track] }))
        };
        var vm = new WorkstationPageVM(source, new osuHitsounder.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler(), new StubSampleFileService([]));
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        var window = new Window { Content = page, Width = 880, Height = 600 };
        using var view = new InMemoryView(window);
        UiTestPump.Run(page.LoadCompletion);
        view.UpdateLayout();
        var list = (ItemsControl)page.FindName("TrackList");
        var card = (FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(0)!;
        var cue = FindVisualChildren<TextBlock>(card).Single(block => block.Name == "MissingGridStatus");
        Assert.That(cue.Text, Is.EqualTo("Sample not found."));
        Assert.That(track.Samples.Single().FileName, Is.EqualTo("missing.wav"));
    }

    [Test]
    public void TrackGridFollowsWaveformZoomAndScroll()
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new osuHitsounder.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)),
            new ImmediateUiScheduler(), new StubSampleFileService(["a.wav"]));
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        var window = new Window { Content = page, Width = 880, Height = 600 };
        using var view = new InMemoryView(window);
        UiTestPump.Run(page.LoadCompletion);
        vm.AddSampleTrackCommand.Execute(null);
        view.UpdateLayout();
        var list = (ItemsControl)page.FindName("TrackList");
        var card = (FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(0)!;
        var grid = FindVisualChild<BeatGridView>(card)!;
        for (var index = 0; index < 3; index++) vm.ZoomInCommand.Execute(null);
        view.UpdateLayout();
        var waveform = (WaveformView)page.FindName("WaveformDisplay");
        Assert.That(grid.ContentWidth, Is.EqualTo(waveform.ActualWidth));
        var scroll = (ScrollViewer)page.FindName("WaveformScrollViewer");
        scroll.ScrollToHorizontalOffset(100);
        view.UpdateLayout();
        Assert.That(grid.HorizontalOffset, Is.EqualTo(scroll.HorizontalOffset));
    }

    [Test]
    public void TrackGridPlacementAddsSampleFromCurrentTrackControls()
    {
        var project = new ProjectData { Offsets = [0], BPMs = [60] };
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav", project))
        };
        var vm = new WorkstationPageVM(source, new osuHitsounder.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)),
            new ImmediateUiScheduler(), new StubSampleFileService(["kick.wav"]));
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 880, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);
        vm.AddSampleTrackCommand.Execute(null);
        view.UpdateLayout();
        var list = (ItemsControl)page.FindName("TrackList");
        var card = (FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(0)!;
        var grid = FindVisualChild<BeatGridView>(card)!;

        grid.UpdatePointerPosition(10);
        Assert.That(grid.RequestSamplePlacement(), Is.True);

        Assert.That(vm.Tracks.Single().Samples, Has.Count.EqualTo(1));
        Assert.That(vm.Tracks.Single().Samples.Single().FileName, Is.EqualTo("kick.wav"));
        Assert.That(vm.Tracks.Single().Samples.Single().Timestamp, Is.EqualTo(0));
        Assert.That(grid.RequestSampleRemoval(5), Is.True);
        Assert.That(vm.Tracks.Single().Samples, Is.Empty);
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in FindVisualChildren<T>(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    private static BeatGridView? FindBeatGrid(DependencyObject root) => FindVisualChild<BeatGridView>(root);

    private static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var found = FindVisualChild<T>(VisualTreeHelper.GetChild(root, index));
            if (found is not null) return found;
        }
        return null;
    }

    [Test]
    public void FooterScrollBarTracksAndControlsWaveformViewport()
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new osuHitsounder.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);
        view.UpdateLayout();

        Assert.That(page.FindName("WaveformScrollBar"), Is.TypeOf<ScrollBar>());
        var bar = (ScrollBar)page.FindName("WaveformScrollBar");
        var scroll = (ScrollViewer)page.FindName("WaveformScrollViewer");
        Assert.Multiple(() =>
        {
            Assert.That(bar.Maximum, Is.EqualTo(scroll.ScrollableWidth).Within(1));
            Assert.That(bar.ViewportSize, Is.EqualTo(scroll.ViewportWidth).Within(1));
            Assert.That(bar.IsEnabled, Is.True);
        });

        bar.ApplyTemplate();
        var track = (System.Windows.Controls.Primitives.Track)bar.Template.FindName("PART_Track", bar);
        Assert.That(track.IncreaseRepeatButton.IsHitTestVisible, Is.True);
        ((RoutedCommand)((RepeatButton)track.IncreaseRepeatButton).Command).Execute(null, bar);
        view.UpdateLayout();
        Assert.That(scroll.HorizontalOffset, Is.GreaterThan(0));

        bar.RaiseEvent(new ScrollEventArgs(ScrollEventType.ThumbTrack, 100)
            { RoutedEvent = ScrollBar.ScrollEvent });
        view.UpdateLayout();
        Assert.Multiple(() =>
        {
            Assert.That(scroll.HorizontalOffset, Is.EqualTo(100).Within(1));
            Assert.That(bar.Value, Is.EqualTo(scroll.HorizontalOffset).Within(1));
        });

        scroll.ScrollToHorizontalOffset(150);
        view.UpdateLayout();
        Assert.That(bar.Value, Is.EqualTo(150).Within(1));

        using (var source = new InMemoryPresentationSource())
        {
            var home = CreateKeyEvent(source, Key.Home);
            home.RoutedEvent = Keyboard.KeyDownEvent;
            bar.RaiseEvent(home);
        }
        view.UpdateLayout();
        Assert.That(scroll.HorizontalOffset, Is.Zero.Within(1));

        Assert.Multiple(() =>
        {
            UiControlContract.AssertStyle<ScrollBar>(bar, "OHsWorkstationWaveformScrollBarStyle");
                UiControlContract.AssertTemplate(bar, "OHsWorkstationWaveformScrollBarTemplate");
                Assert.That(bar.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(bar.IsEnabled, Is.True);
            Assert.That(bar.Value, Is.Zero.Within(1));
        });
    }

    [Test]
    public void WaveformSeekCommandUpdatesBoundMarkerPosition()
    {
        var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new osuHitsounder.Services.MarkerPlaybackCoordinator(audio, (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        var window = new Window { Content = page, Width = 900, Height = 600 };
        using var view = new InMemoryView(window);
        UiTestPump.Run(page.LoadCompletion);
        view.UpdateLayout();

        var waveform = (WaveformView)page.FindName("WaveformDisplay");
        Assert.That(waveform.SeekAt(waveform.ActualWidth * 0.6), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(audio.Snapshot.Position, Is.EqualTo(TimeSpan.FromSeconds(6)).Within(TimeSpan.FromMilliseconds(1)));
            Assert.That(waveform.Position, Is.EqualTo(audio.Snapshot.Position));
            Assert.That(((ScrollViewer)page.FindName("WaveformScrollViewer")).IsEnabled, Is.True);
        });
    }

    [Test]
    public void SubdivisionSliderIsAccessibleAndHasNoPlaybackEffect()
    {
        var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new osuHitsounder.Services.MarkerPlaybackCoordinator(audio, (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        var window = new Window { Content = page, Width = 640, Height = 480, FlowDirection = FlowDirection.RightToLeft };
        using var view = new InMemoryView(window);
        UiTestPump.Run(page.LoadCompletion);
        view.UpdateLayout();

        var selector = (ValueSlider)page.FindName("SubdivisionSelector");
        var before = audio.Snapshot;
        Assert.Multiple(() =>
        {
            Assert.That(selector.Minimum, Is.Zero);
            Assert.That(selector.Maximum, Is.EqualTo(8));
            Assert.That(selector.Value, Is.EqualTo(3));
            Assert.That(selector.DisplayText, Is.EqualTo("1/4"));
            Assert.That(AutomationProperties.GetName(selector), Is.EqualTo("Beat subdivision"));
        });
        selector.Value = 8;
        Assert.That(audio.Snapshot, Is.EqualTo(before));
    }

    [Test]
    public void ZoomButtonsAndSelectorSynchronize()
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new osuHitsounder.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        var window = new Window { Content = page, Width = 900, Height = 600 };
        using var view = new InMemoryView(window);
        UiTestPump.Run(page.LoadCompletion);
        view.UpdateLayout();

        var controls = (StackPanel)page.FindName("ZoomControls");
        var selector = (ValueSlider)page.FindName("SubdivisionSelector");
        var buttons = controls.Children.OfType<BalloonButton>().ToArray();
        Assert.That(buttons.Length, Is.EqualTo(2));
        Assert.That(AutomationProperties.GetName(buttons[0]), Is.EqualTo("Zoom waveform out"));
        Assert.That(AutomationProperties.GetName(buttons[1]), Is.EqualTo("Zoom waveform in"));
        Assert.That(buttons[0].IsEnabled, Is.True);
        buttons[1].Command.Execute(null);
        view.UpdateLayout();
        Assert.That(vm.ZoomFactor, Is.EqualTo(33));
        Assert.That(buttons[0].IsEnabled, Is.True);

        selector.Value = 8;
        selector.GetBindingExpression(RangeBase.ValueProperty)!.UpdateSource();
        Assert.That(vm.SelectedSubdivisionIndex, Is.EqualTo(8));
        vm.SelectedSubdivisionIndex = 2;
        view.UpdateLayout();
        Assert.That(selector.Value, Is.EqualTo(2));
    }

    [Test]
    public void ZoomKeepsVisiblePlaybackMarkerAtItsViewportPosition()
    {
        var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new osuHitsounder.Services.MarkerPlaybackCoordinator(audio, (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        var window = new Window { Content = page, Width = 900, Height = 600 };
        using var view = new InMemoryView(window);
        UiTestPump.Run(page.LoadCompletion);
        view.UpdateLayout();
        osuHitsounder.Test.Presentation.UiTestPump.Run(vm.SeekCommand.ExecuteAsync(TimeSpan.FromSeconds(6)));
        view.UpdateLayout();

        var scroll = (ScrollViewer)page.FindName("WaveformScrollViewer");
        var waveform = (WaveformView)page.FindName("WaveformDisplay");
        double ScreenX() => vm.Position.TotalSeconds / vm.Waveform!.Duration.TotalSeconds
            * waveform.ActualWidth - scroll.HorizontalOffset;
        scroll.ScrollToHorizontalOffset(vm.Position.TotalSeconds / vm.Waveform!.Duration.TotalSeconds
            * waveform.ActualWidth - scroll.ViewportWidth / 2);
        view.UpdateLayout();
        var before = ScreenX();

        vm.ZoomInCommand.Execute(null);
        view.UpdateLayout();

        Assert.Multiple(() =>
        {
            Assert.That(ScreenX(), Is.EqualTo(before).Within(1));
            Assert.That(audio.Snapshot.Position, Is.EqualTo(TimeSpan.FromSeconds(6)));
        });

    }

    [Test]
    public void ZoomCentersOffscreenMarkerClampsBoundariesAndCoalescesChanges()
    {
        var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new osuHitsounder.Services.MarkerPlaybackCoordinator(audio, (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        var window = new Window { Content = page, Width = 900, Height = 600 };
        using var view = new InMemoryView(window);
        UiTestPump.Run(page.LoadCompletion);
        view.UpdateLayout();

        var scroll = (ScrollViewer)page.FindName("WaveformScrollViewer");
        var waveform = (WaveformView)page.FindName("WaveformDisplay");
        double ScreenX() => vm.Position.TotalSeconds / vm.Waveform!.Duration.TotalSeconds
            * waveform.ActualWidth - scroll.HorizontalOffset;

        view.UpdateLayout();
        scroll.ScrollToLeftEnd();
        osuHitsounder.Test.Presentation.UiTestPump.Run(vm.SeekCommand.ExecuteAsync(TimeSpan.FromSeconds(8)));
        view.UpdateLayout();
        Assert.That(ScreenX(), Is.GreaterThan(scroll.ViewportWidth));

        vm.ZoomInCommand.Execute(null);
        view.UpdateLayout();
        Assert.That(ScreenX(), Is.EqualTo(scroll.ViewportWidth / 2).Within(1));

        osuHitsounder.Test.Presentation.UiTestPump.Run(vm.SeekCommand.ExecuteAsync(TimeSpan.Zero));
        vm.ZoomOutCommand.Execute(null);
        view.UpdateLayout();
        Assert.That(scroll.HorizontalOffset, Is.Zero.Within(1));

        osuHitsounder.Test.Presentation.UiTestPump.Run(vm.SeekCommand.ExecuteAsync(TimeSpan.FromSeconds(10)));
        vm.ZoomInCommand.Execute(null);
        view.UpdateLayout();
        Assert.That(scroll.HorizontalOffset, Is.EqualTo(scroll.ScrollableWidth).Within(1));

        for (var index = 0; index < 40; index++) vm.ZoomOutCommand.Execute(null);
        view.UpdateLayout();
        Assert.That(vm.ZoomFactor, Is.EqualTo(8));
        Assert.That(scroll.HorizontalOffset, Is.EqualTo(scroll.ScrollableWidth).Within(1));

        osuHitsounder.Test.Presentation.UiTestPump.Run(vm.SeekCommand.ExecuteAsync(TimeSpan.FromSeconds(6)));
        scroll.ScrollToHorizontalOffset(vm.Position.TotalSeconds / vm.Waveform!.Duration.TotalSeconds
            * waveform.ActualWidth - scroll.ViewportWidth / 2);
        view.UpdateLayout();
        var before = ScreenX();
        vm.ZoomInCommand.Execute(null);
        vm.ZoomInCommand.Execute(null);
        view.UpdateLayout();
        Assert.That(ScreenX(), Is.EqualTo(before).Within(1));
    }

    [Test]
    public void ModifierWheelStepsZoomAndSubdivisionWithoutSeeking()
    {
        var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new osuHitsounder.Services.MarkerPlaybackCoordinator(audio, (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        var window = new Window { Content = page, Width = 900, Height = 600 };
        using var view = new InMemoryView(window);
        UiTestPump.Run(page.LoadCompletion);
        var before = audio.Snapshot;

        Assert.Multiple(() =>
        {
            Assert.That(page.HandleWaveformWheel(ModifierKeys.Control, 120), Is.True);
            Assert.That(vm.ZoomFactor, Is.EqualTo(33));
            Assert.That(page.HandleWaveformWheel(ModifierKeys.Control, 240), Is.True);
            Assert.That(vm.ZoomFactor, Is.EqualTo(35));
            Assert.That(page.HandleWaveformWheel(ModifierKeys.Control, -120), Is.True);
            Assert.That(vm.ZoomFactor, Is.EqualTo(34));
            Assert.That(page.HandleWaveformWheel(ModifierKeys.Alt, 120), Is.True);
            Assert.That(vm.SelectedSubdivisionIndex, Is.EqualTo(4));
            Assert.That(page.HandleWaveformWheel(ModifierKeys.Alt, -120), Is.True);
            Assert.That(vm.SelectedSubdivisionIndex, Is.EqualTo(3));
            Assert.That(audio.Snapshot, Is.EqualTo(before));
        });
    }

    [Test]
    public void SpaceTogglesPlaybackOncePerPressExceptInsideTextInputs()
    {
        var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new osuHitsounder.Services.MarkerPlaybackCoordinator(audio, (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);

        Assert.That(page.HandlePlaybackKey(Key.Space, ModifierKeys.None, false, page), Is.True);
        UiTestPump.Run(page.PlaybackInputCompletion);
        Assert.That(vm.IsPlaying, Is.True);
        Assert.That(page.HandlePlaybackKey(Key.Space, ModifierKeys.None, true, page), Is.True);
        UiTestPump.Run(page.PlaybackInputCompletion);
        Assert.That(vm.IsPlaying, Is.True);
        Assert.That(page.HandlePlaybackKey(Key.Space, ModifierKeys.None, false, page), Is.True);
        UiTestPump.Run(page.PlaybackInputCompletion);
        Assert.That(vm.IsPlaying, Is.False);

        Assert.Multiple(() =>
        {
            Assert.That(page.HandlePlaybackKey(Key.Space, ModifierKeys.Control, false, page), Is.False);
        UiTestPump.Run(page.PlaybackInputCompletion);
            Assert.That(page.HandlePlaybackKey(Key.Enter, ModifierKeys.None, false, page), Is.False);
        UiTestPump.Run(page.PlaybackInputCompletion);
            Assert.That(page.HandlePlaybackKey(Key.Space, ModifierKeys.None, false, new TextBox()), Is.False);
        UiTestPump.Run(page.PlaybackInputCompletion);
            Assert.That(page.HandlePlaybackKey(Key.Space, ModifierKeys.None, false, new PasswordBox()), Is.False);
        UiTestPump.Run(page.PlaybackInputCompletion);
        });

        var selector = new ComboBox();
        Assert.That(page.HandlePlaybackKey(Key.Space, ModifierKeys.None, false, selector), Is.True);
        UiTestPump.Run(page.PlaybackInputCompletion);
    }

    [Test]
    public void ArrowsSeekSubdivisionLinesAcrossNativeEditors()
    {
        var audio = new StubWorkstationAudioService();
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav",
                new ProjectData { Offsets = [0], BPMs = [120] }))
        };
        var vm = new WorkstationPageVM(source, new osuHitsounder.Services.MarkerPlaybackCoordinator(audio, (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);
        Assert.That(page.HandlePlaybackKey(Key.Right, ModifierKeys.None, false, page), Is.True);
        UiTestPump.Run(page.PlaybackInputCompletion);
        Assert.That(audio.Snapshot.Position, Is.EqualTo(TimeSpan.FromMilliseconds(125)));
        Assert.That(page.HandlePlaybackKey(Key.Right, ModifierKeys.None, true, page), Is.True);
        UiTestPump.Run(page.PlaybackInputCompletion);
        Assert.That(audio.Snapshot.Position, Is.EqualTo(TimeSpan.FromMilliseconds(250)));
        Assert.That(page.HandlePlaybackKey(Key.Left, ModifierKeys.None, false, page), Is.True);
        UiTestPump.Run(page.PlaybackInputCompletion);
        Assert.That(audio.Snapshot.Position, Is.EqualTo(TimeSpan.FromMilliseconds(125)));
        Assert.Multiple(() =>
        {
            Assert.That(page.HandlePlaybackKey(Key.Left, ModifierKeys.None, false, new TextBox()), Is.True);
        UiTestPump.Run(page.PlaybackInputCompletion);
            Assert.That(page.HandlePlaybackKey(Key.Left, ModifierKeys.None, false, new ComboBox()), Is.True);
        UiTestPump.Run(page.PlaybackInputCompletion);
            Assert.That(page.HandlePlaybackKey(Key.Left, ModifierKeys.None, false, new Slider()), Is.True);
        UiTestPump.Run(page.PlaybackInputCompletion);
            Assert.That(page.HandlePlaybackKey(Key.Left, ModifierKeys.None, false, new ListBoxItem()), Is.True);
        UiTestPump.Run(page.PlaybackInputCompletion);
            Assert.That(page.HandlePlaybackKey(Key.Right, ModifierKeys.Alt, false, page), Is.False);
        UiTestPump.Run(page.PlaybackInputCompletion);
        });
        Assert.That(audio.Snapshot.Position, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void ControlArrowsSeekWholeBeatsAcrossEditorFocus()
    {
        var audio = new StubWorkstationAudioService();
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav",
                new ProjectData { Offsets = [0, 800], BPMs = [120, 60] }))
        };
        var vm = new WorkstationPageVM(source, new osuHitsounder.Services.MarkerPlaybackCoordinator(audio, (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);

        Assert.That(page.HandlePlaybackKey(Key.Right, ModifierKeys.Control, false, page), Is.True);
        UiTestPump.Run(page.PlaybackInputCompletion);
        Assert.That(audio.Snapshot.Position, Is.EqualTo(TimeSpan.FromMilliseconds(500)));
        Assert.That(page.HandlePlaybackKey(Key.Right, ModifierKeys.Control, true, page), Is.True);
        UiTestPump.Run(page.PlaybackInputCompletion);
        Assert.That(audio.Snapshot.Position, Is.EqualTo(TimeSpan.FromMilliseconds(800)));
        Assert.That(page.HandlePlaybackKey(Key.Left, ModifierKeys.Control, false, page), Is.True);
        UiTestPump.Run(page.PlaybackInputCompletion);
        Assert.That(audio.Snapshot.Position, Is.EqualTo(TimeSpan.FromMilliseconds(500)));
        Assert.That(page.HandlePlaybackKey(Key.Right, ModifierKeys.Control, false, new TextBox()), Is.True);
        UiTestPump.Run(page.PlaybackInputCompletion);
        Assert.That(page.HandlePlaybackKey(Key.Right, ModifierKeys.Control, false, new Slider()), Is.True);
        UiTestPump.Run(page.PlaybackInputCompletion);
        Assert.That(audio.Snapshot.Position, Is.EqualTo(TimeSpan.FromMilliseconds(1800)));
        Assert.That(page.HandlePlaybackKey(Key.Right, ModifierKeys.Control | ModifierKeys.Shift, false, page), Is.False);
        UiTestPump.Run(page.PlaybackInputCompletion);
        audio.Snapshot = new AudioPlaybackSnapshot(TimeSpan.Zero, true, null);
        Assert.That(page.HandlePlaybackKey(Key.Left, ModifierKeys.Control, false, page), Is.True);
        UiTestPump.Run(page.PlaybackInputCompletion);
        Assert.That(audio.Snapshot.Position, Is.EqualTo(TimeSpan.Zero));
        Assert.That(audio.Snapshot.IsPlaying, Is.True);
        audio.Snapshot = new AudioPlaybackSnapshot(TimeSpan.FromSeconds(10), true, null);
        Assert.That(page.HandlePlaybackKey(Key.Right, ModifierKeys.Control, false, page), Is.True);
        UiTestPump.Run(page.PlaybackInputCompletion);
        Assert.That(audio.Snapshot.Position, Is.EqualTo(TimeSpan.FromSeconds(10)));
        Assert.That(audio.Snapshot.IsPlaying, Is.True);
    }

    [TestCase(FlowDirection.LeftToRight)]
    [TestCase(FlowDirection.RightToLeft)]
    public void ArrowNavigationRevealsOffscreenPositionWithoutMovingVisibleView(FlowDirection direction)
    {
        var audio = new StubWorkstationAudioService
        {
            Snapshot = new AudioPlaybackSnapshot(TimeSpan.FromSeconds(8), false, null)
        };
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav",
                new ProjectData { Offsets = [0], BPMs = [120] }))
        };
        var vm = new WorkstationPageVM(source, new osuHitsounder.Services.MarkerPlaybackCoordinator(audio, (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600,
            FlowDirection = direction });
        UiTestPump.Run(page.LoadCompletion);
        view.UpdateLayout();
        UiTestPump.Run(vm.SeekCommand.ExecuteAsync(TimeSpan.FromSeconds(8)));
        var scroll = (ScrollViewer)page.FindName("WaveformScrollViewer");
        scroll.ScrollToLeftEnd();
        view.UpdateLayout();
        Assert.That(page.HandlePlaybackKey(Key.Right, ModifierKeys.None, false, page), Is.True);
        UiTestPump.Run(page.PlaybackInputCompletion);
        Assert.That(audio.Snapshot.Position, Is.EqualTo(TimeSpan.FromMilliseconds(8125)));
        view.UpdateLayout();
        Assert.That(scroll.HorizontalOffset, Is.GreaterThan(0));
        var before = scroll.HorizontalOffset;
        Assert.That(page.HandlePlaybackKey(Key.Left, ModifierKeys.None, false, page), Is.True);
        UiTestPump.Run(page.PlaybackInputCompletion);
        view.UpdateLayout();
        Assert.That(scroll.HorizontalOffset, Is.EqualTo(before).Within(1));
    }

    [Test]
    public void SpaceIsRejectedUntilLoadedAndAfterDeactivation()
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new osuHitsounder.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        Assert.That(page.HandlePlaybackKey(Key.Space, ModifierKeys.None, false, page), Is.False);
        UiTestPump.Run(page.PlaybackInputCompletion);

        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);
        UiTestPump.Run(page.DeactivateAsync());
        Assert.That(page.HandlePlaybackKey(Key.Space, ModifierKeys.None, false, page), Is.False);
        UiTestPump.Run(page.PlaybackInputCompletion);
    }

    [Test]
    public void ModifierWheelAccumulatesPerModeConsumesBoundsAndIgnoresOtherModifiers()
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new osuHitsounder.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        var unloadedPage = new WorkstationPage(vm);
        Assert.That(unloadedPage.HandleWaveformWheel(ModifierKeys.Control, 120), Is.False);

        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        var window = new Window { Content = page, Width = 900, Height = 600 };
        using var view = new InMemoryView(window);
        UiTestPump.Run(page.LoadCompletion);

        Assert.That(page.HandleWaveformWheel(ModifierKeys.Control, 60), Is.True);
        Assert.That(vm.ScrollWithMarker, Is.True);
        Assert.That(vm.ZoomFactor, Is.EqualTo(32));
        Assert.That(page.HandleWaveformWheel(ModifierKeys.Control, 60), Is.True);
        Assert.That(vm.ZoomFactor, Is.EqualTo(33));

        Assert.That(page.HandleWaveformWheel(ModifierKeys.Control, 60), Is.True);
        Assert.That(page.HandleWaveformWheel(ModifierKeys.Alt, 60), Is.True);
        Assert.That(page.HandleWaveformWheel(ModifierKeys.Alt, 60), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(vm.ZoomFactor, Is.EqualTo(33));
            Assert.That(vm.SelectedSubdivisionIndex, Is.EqualTo(4));
        });

        Assert.Multiple(() =>
        {
            Assert.That(page.HandleWaveformWheel(ModifierKeys.None, 120), Is.False);
            Assert.That(page.HandleWaveformWheel(ModifierKeys.Control | ModifierKeys.Alt, 120), Is.False);
        });

        for (var index = 0; index < 140; index++) vm.ZoomInCommand.Execute(null);
        Assert.That(page.HandleWaveformWheel(ModifierKeys.Control, 120), Is.True);
        Assert.That(vm.ZoomFactor, Is.EqualTo(128));
        vm.SelectedSubdivisionIndex = 8;
        Assert.That(page.HandleWaveformWheel(ModifierKeys.Alt, 120), Is.True);
        Assert.That(vm.SelectedSubdivisionIndex, Is.EqualTo(8));
    }

    [Test]
    public void ControlShiftWheelChangesOnlyTrackLaneHeight()
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new osuHitsounder.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)),
            new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);
        var zoom = vm.ZoomFactor;
        var position = vm.Position;
        vm.SetTrackHeightMinimum(200);

        Assert.That(page.HandleWaveformWheel(ModifierKeys.Control | ModifierKeys.Shift, 60), Is.True);
        Assert.That(vm.TrackHeight, Is.EqualTo(200));
        Assert.That(page.HandleWaveformWheel(ModifierKeys.Control | ModifierKeys.Shift, 60), Is.True);
        view.UpdateLayout();

        Assert.Multiple(() =>
        {
            Assert.That(vm.TrackHeight, Is.EqualTo(206));
            Assert.That(vm.ZoomFactor, Is.EqualTo(zoom));
            Assert.That(vm.Position, Is.EqualTo(position));
        });

        for (var index = 0; index < 100; index++) vm.DecreaseTrackHeightCommand.Execute(null);
        Assert.That(vm.TrackHeight, Is.EqualTo(200));
        Assert.That(vm.DecreaseTrackHeightCommand.CanExecute(null), Is.False);
        for (var index = 0; index < 100; index++) vm.IncreaseTrackHeightCommand.Execute(null);
        Assert.That(vm.TrackHeight, Is.EqualTo(308));
        Assert.That(vm.IncreaseTrackHeightCommand.CanExecute(null), Is.False);
    }

    [Test]
    public void ShiftWheelPansWaveformWithoutSeekingAndConsumesBounds()
    {
        var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new osuHitsounder.Services.MarkerPlaybackCoordinator(audio, (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);
        view.UpdateLayout();

        var scroll = (ScrollViewer)page.FindName("WaveformScrollViewer");
        scroll.ScrollToHorizontalOffset(100);
        view.UpdateLayout();
        var before = audio.Snapshot;

        Assert.That(page.HandleWaveformWheel(ModifierKeys.Shift, 60), Is.True);
        view.UpdateLayout();
        Assert.That(scroll.HorizontalOffset, Is.EqualTo(100).Within(1));
        Assert.That(page.HandleWaveformWheel(ModifierKeys.Shift, 60), Is.True);
        view.UpdateLayout();
        Assert.That(scroll.HorizontalOffset, Is.LessThan(100));

        var left = scroll.HorizontalOffset;
        Assert.That(page.HandleWaveformWheel(ModifierKeys.Shift, -120), Is.True);
        view.UpdateLayout();
        Assert.That(scroll.HorizontalOffset, Is.GreaterThan(left));
        Assert.That(audio.Snapshot, Is.EqualTo(before));

        scroll.ScrollToLeftEnd();
        Assert.That(page.HandleWaveformWheel(ModifierKeys.Shift, 120), Is.True);
        view.UpdateLayout();
        Assert.That(scroll.HorizontalOffset, Is.Zero);
        scroll.ScrollToRightEnd();
        Assert.That(page.HandleWaveformWheel(ModifierKeys.Shift, -120), Is.True);
        view.UpdateLayout();
        Assert.That(scroll.HorizontalOffset, Is.EqualTo(scroll.ScrollableWidth).Within(1));
    }

    [TestCase(640, 480, FlowDirection.LeftToRight, "Dark")]
    [TestCase(640, 480, FlowDirection.RightToLeft, "Dark")]
    [TestCase(900, 600, FlowDirection.LeftToRight, "Dark")]
    [TestCase(900, 600, FlowDirection.RightToLeft, "Dark")]
    [TestCase(640, 480, FlowDirection.LeftToRight, "Light")]
    [TestCase(640, 480, FlowDirection.RightToLeft, "Light")]
    [TestCase(900, 600, FlowDirection.LeftToRight, "Light")]
    [TestCase(900, 600, FlowDirection.RightToLeft, "Light")]
    public void ZoomButtonsKeepResourceAndAutomationContractsAcrossLayouts(
        double width, double height, FlowDirection flowDirection, string theme)
    {
        EnsureResources(theme);
        var trace = new StringBuilder();
        using var writer = new StringWriter(trace);
        using var listener = new TextWriterTraceListener(writer);
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        try
        {
            var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new osuHitsounder.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
            var page = new WorkstationPage(vm);
            page.SetProjectPath("project.json");
            var window = new Window { Content = page, Width = width, Height = height, FlowDirection = flowDirection };
            using var view = new InMemoryView(window);
            UiTestPump.Run(page.LoadCompletion);
            view.UpdateLayout();

            var selector = (ValueSlider)page.FindName("SubdivisionSelector");
            var bar = (ScrollBar)page.FindName("WaveformScrollBar");
            var zoomControls = (StackPanel)page.FindName("ZoomControls");
            var buttons = zoomControls.Children.OfType<BalloonButton>().ToArray();
            listener.Flush();
            Assert.Multiple(() =>
            {
                Assert.That(buttons.Length, Is.EqualTo(2));
                Assert.That(AutomationProperties.GetName(selector), Is.EqualTo("Beat subdivision"));
                foreach (var button in buttons)
                {
                    UiControlContract.AssertStyle<BalloonButton>(button, "OHsBalloonNeutralActionStyle");
                    Assert.That(AutomationProperties.GetName(button), Is.Not.Empty);
                }
                UiControlContract.AssertStyle<ScrollBar>(bar, "OHsWorkstationWaveformScrollBarStyle");
                UiControlContract.AssertTemplate(bar, "OHsWorkstationWaveformScrollBarTemplate");
                Assert.That(bar.Visibility, Is.EqualTo(Visibility.Visible));
                Assert.That(trace.ToString(), Is.Empty);
            });
        }
        finally
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
        }
    }

    [Test]
    public void ZoomButtonsAreDisabledWhileWaveformLoads()
    {
        var completion = new TaskCompletionSource<WorkstationSource>();
        var source = new StubWorkstationSourceService { Load = (_, _) => completion.Task };
        var vm = new WorkstationPageVM(source, new osuHitsounder.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        var window = new Window { Content = page, Width = 640, Height = 480 };
        using var view = new InMemoryView(window);
        view.UpdateLayout();

        var buttons = ((StackPanel)page.FindName("ZoomControls")).Children.OfType<BalloonButton>().ToArray();
        var scrollBar = (ScrollBar)page.FindName("WaveformScrollBar");
        Assert.Multiple(() =>
        {
            Assert.That(vm.IsLoading, Is.True);
            Assert.That(buttons.All(button => !button.IsEnabled), Is.True);
            Assert.That(scrollBar.IsEnabled, Is.False);
        });

        completion.SetResult(new WorkstationSource("map.osu", "audio.wav", new ProjectData()));
        UiTestPump.Run(page.LoadCompletion);
        view.UpdateLayout();
        Assert.That(buttons.Single(button => ReferenceEquals(button.Command, vm.ZoomInCommand)).IsEnabled, Is.True);
    }

    [TestCase(640, 480, FlowDirection.LeftToRight)]
    [TestCase(1100, 600, FlowDirection.RightToLeft)]
    [TestCase(480, 480, FlowDirection.LeftToRight)]
    public void SubdivisionKeepsResourceAndBindingAcrossResize(double width, double height, FlowDirection direction)
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new osuHitsounder.Services.MarkerPlaybackCoordinator(
            new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        var window = new Window { Content = page, Width = width, Height = height, FlowDirection = direction };
        using var view = new InMemoryView(window);
        UiTestPump.Run(page.LoadCompletion);
        var card = (Border)page.FindName("SongAudioCard");
        foreach (var cardHeight in new[] { 200d, 260d, 180d })
        {
            card.Height = cardHeight;
            window.Width = width + cardHeight - 200;
            window.Height = height + cardHeight - 200;
            view.UpdateLayout();
            var selector = (ValueSlider)page.FindName("SubdivisionSelector");
            UiControlContract.AssertStyle<ValueSlider>(selector, "BaseValueSliderStyle");
            Assert.That(selector.Value, Is.EqualTo(vm.SelectedSubdivisionIndex));
        }
    }

    [TestCase(480d, FlowDirection.LeftToRight, "Dark")]
    [TestCase(480d, FlowDirection.RightToLeft, "Light")]
    [TestCase(1100d, FlowDirection.LeftToRight, "Light")]
    [TestCase(1100d, FlowDirection.RightToLeft, "Dark")]
    public void ResnapHeaderActionBindsCommandAndAssignedStyle(double width, FlowDirection direction, string theme)
    {
        EnsureResources(theme);
        var sample = new Sample { Timestamp = 1188, FileName = "a.wav" };
        var track = new osuHitsounder.Core.Entities.Track { SelectedSampleFileName = "a.wav", Samples = [sample] };
        var completion = new TaskCompletionSource<WorkstationSource>();
        var source = new StubWorkstationSourceService { Load = (_, _) => completion.Task };
        var vm = new WorkstationPageVM(source, new osuHitsounder.Services.MarkerPlaybackCoordinator(
            new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), new StubSampleFileService(["a.wav"]));
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = width, Height = 720, FlowDirection = direction });
        try
        {
            var action = page.FindName("ResnapAllSamplesButton") as BalloonButton;
            Assert.That(action, Is.Not.Null, "The workstation header must expose the resnap action.");
            var button = action!;
            Assert.That(button.IsEnabled, Is.False);
            completion.SetResult(new WorkstationSource("map.osu", "audio.wav", new ProjectData
            {
                Offsets = [0], BPMs = [120], Tracks = [track]
            }));
            UiTestPump.Run(page.LoadCompletion);
            view.UpdateLayout();
            UiControlContract.AssertStyle<BalloonButton>(button, "OHsWorkstationTransportButtonStyle");
            Assert.That(button.Command, Is.SameAs(vm.ResnapAllSamplesCommand));
            Assert.That(button.IsEnabled, Is.True);
            Assert.That(button.ButtonText, Is.EqualTo("Resnap all samples"));
            Assert.That(button.Focusable, Is.True);
            Assert.That(button.IsTabStop, Is.False);
            Assert.That(button.FocusVisualStyle, Is.SameAs(page.FindResource("DefaultFocusVisualStyle")));
            Assert.That(button.ToolTip, Is.EqualTo("Snap every sample to the nearest beat in the current subdivision"));
            Assert.That(AutomationProperties.GetHelpText(button), Is.EqualTo(button.ToolTip));
            Assert.That(page.HandlePlaybackKey(Key.Enter, ModifierKeys.None, false, button), Is.False);
            using var inputSource = new InMemoryPresentationSource();
            var enter = CreateKeyEvent(inputSource, Key.Enter);
            enter.RoutedEvent = Keyboard.KeyDownEvent;
            button.RaiseEvent(enter);
            Assert.That(sample.Timestamp, Is.EqualTo(1250));
            vm.RemoveSample(track, sample);
            view.UpdateLayout();
            Assert.That(button.IsEnabled, Is.False);
            vm.UndoCommand.Execute(null);
            view.UpdateLayout();
            Assert.That(button.IsEnabled, Is.True);
            vm.IsReturning = true;
            view.UpdateLayout();
            Assert.That(button.IsEnabled, Is.False);
        }
        finally
        {
            completion.TrySetResult(new WorkstationSource("map.osu", "audio.wav", new ProjectData()));
            UiTestPump.Run(page.LoadCompletion);
            UiTestPump.Run(page.DeactivateAsync());
        }
    }

    private static KeyEventArgs CreateKeyEvent(PresentationSource source, Key key) => new(
        Keyboard.PrimaryDevice,
        source,
        Environment.TickCount,
        key)
    {
        RoutedEvent = Keyboard.PreviewKeyDownEvent
    };

    private static void EnsureResources(string theme = "Dark")
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Clear();
        UiTestPump.EnsureFontResources(app);
        foreach (var resource in new[] { $"Themes/{theme}.xaml", "Converters/Converters.xaml", "Icons.xaml",
                     "Styles/TextBlock.xaml", "Styles/ItemsControl.xaml", "Styles/StackPanel.xaml", "Styles/Grid.xaml", "Styles/Border.xaml", "Styles/ContentPresenter.xaml",
                     "Styles/Button.xaml", "Styles/ToolTip.xaml", "Styles/ScrollViewer.xaml", "Styles/TextBox.xaml", "Styles/ComboBox.xaml", "Styles/ListBox.xaml", "Styles/Slider.xaml", "BeatGrid.xaml" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"/osuHitsounder;component/Resources/{resource}", UriKind.Relative)
            });
    }
}
