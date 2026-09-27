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

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class WorkstationPageIntegrationTests
{
    [Test]
    public void SongVolumeSliderUpdatesSongOnlyAndExposesAccessibleBounds()
    {
        var track = new osuHitsounder.Core.Entities.Track { Volume = 70 };
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav",
                new ProjectData { Tracks = [track] }))
        };
        var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(source, audio, new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 640, Height = 480 });
        UiTestPump.Run(page.LoadCompletion);
        view.UpdateLayout();

        var slider = (ValueSlider)page.FindName("SongVolumeSlider");
        slider.Value = 50;
        slider.GetBindingExpression(RangeBase.ValueProperty)!.UpdateSource();

        Assert.Multiple(() =>
        {
            Assert.That(vm.SongVolume, Is.EqualTo(50));
            Assert.That(audio.SongVolume, Is.EqualTo(50));
            Assert.That(track.Volume, Is.EqualTo(70));
            Assert.That(slider.Minimum, Is.Zero);
            Assert.That(slider.Maximum, Is.EqualTo(100));
            Assert.That(slider.SmallChange, Is.EqualTo(1));
            Assert.That(slider.IsEnabled, Is.True);
            Assert.That(AutomationProperties.GetName(slider), Is.EqualTo("Song volume"));
        });
    }

    [Test]
    public void SongVolumeSliderIsDisabledWhileLoading()
    {
        var completion = new TaskCompletionSource<WorkstationSource>();
        var vm = new WorkstationPageVM(
            new StubWorkstationSourceService { Load = (_, _) => completion.Task },
            new StubWorkstationAudioService(), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 640, Height = 480 });
        view.UpdateLayout();

        var slider = (ValueSlider)page.FindName("SongVolumeSlider");
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
        var vm = new WorkstationPageVM(source, new StubWorkstationAudioService(),
            new ImmediateUiScheduler(), trackRemovalDialog: dialog);
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 640, Height = 480 });
        UiTestPump.Run(page.LoadCompletion);
        view.UpdateLayout();
        var button = FindVisualChildren<BalloonButton>(page)
            .Single(action => action.ButtonText == "Remove Track");
        Assert.That(button.CommandParameter, Is.SameAs(track));
        Assert.That(button.Style, Is.SameAs(page.FindResource("OHsBalloonDestructiveActionStyle")));
        Assert.That(AutomationProperties.GetName(button), Is.EqualTo("Remove track"));
        UiTestPump.Run(vm.RemoveTrackCommand.ExecuteAsync(button.CommandParameter));
        view.UpdateLayout();
        Assert.That(((TextBlock)page.FindName("EmptyTracksText")).Visibility, Is.EqualTo(Visibility.Visible));
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
        var vm = new WorkstationPageVM(source, new StubWorkstationAudioService(),
            new ImmediateUiScheduler(), trackRemovalDialog: new StubTrackRemovalDialogService());
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 640, Height = 480 });
        UiTestPump.Run(page.LoadCompletion);
        view.UpdateLayout();
        var button = FindVisualChildren<BalloonButton>(page)
            .Single(action => action.ButtonText == "Remove Track");
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
        var vm = new WorkstationPageVM(source, new StubWorkstationAudioService(),
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
    public void PlaybackMarkerIsWhite()
    {
        var page = new WorkstationPage(new WorkstationPageVM(new StubWorkstationSourceService(),
            new StubWorkstationAudioService(), new ImmediateUiScheduler()));
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600 });
        view.UpdateLayout();
        var waveform = (WaveformView)page.FindName("WaveformDisplay");
        Assert.That(((SolidColorBrush)waveform.MarkerBrush).Color, Is.EqualTo(Colors.White));
    }

    [Test]
    public void LoadsWaveformSwitchesTransportIconAndKeepsMinimumZoomScrollable()
    {
        var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), audio, new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        var window = new Window { Content = page, Width = 900, Height = 600, ShowInTaskbar = false };
        using var view = new InMemoryView(window);
        UiTestPump.Run(page.LoadCompletion);
        view.UpdateLayout();

        var play = (BalloonButton)page.FindName("PlayPauseButton");
        Assert.That(play.ButtonIcon, Is.SameAs(Application.Current.FindResource("MediaPlay")));
        vm.PlayPauseCommand.Execute(null);
        view.UpdateLayout();
        Assert.That(play.ButtonIcon, Is.SameAs(Application.Current.FindResource("MediaPause")));

        var scroll = (ScrollViewer)page.FindName("WaveformScrollViewer");
        var waveform = (WaveformView)page.FindName("WaveformDisplay");
        Assert.That(waveform.DesiredSize.Width, Is.GreaterThan(scroll.ViewportWidth));
        scroll.ScrollToHorizontalOffset(50);
        view.UpdateLayout();
        Assert.Multiple(() =>
        {
            Assert.That(vm.ZoomFactor, Is.EqualTo(16));
            Assert.That(scroll.HorizontalOffset, Is.EqualTo(50).Within(1));
        });
    }

    [Test]
    public void ExposesTrackListFooterAndAccessibleViewport()
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new StubWorkstationAudioService(),
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
            Assert.That(((FrameworkElement)page.FindName("ZoomFooter")).HorizontalAlignment, Is.EqualTo(HorizontalAlignment.Stretch));
            Assert.That(((ScrollViewer)page.FindName("WaveformScrollViewer")).Focusable, Is.True);
        });
    }

    [TestCase(FlowDirection.LeftToRight)]
    [TestCase(FlowDirection.RightToLeft)]
    public void AddedTrackCardUsesWaveformViewportAndOffset(FlowDirection direction)
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new StubWorkstationAudioService(),
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
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new StubWorkstationAudioService(),
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
            var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new StubWorkstationAudioService(),
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
            var selector = FindVisualChild<ComboBox>(otherCard)!;
            Assert.That(selector.SelectedItem, Is.EqualTo("a.wav"));
            UiTestPump.Run(vm.ImportSampleAsync());
            view.UpdateLayout();
            Assert.That(selector.SelectedItem, Is.EqualTo("a.wav"));
            UiTestPump.Run(vm.FlushSavesAsync());
        }
        finally { Directory.Delete(root.FullName, true); }
    }

    [Test]
    public void ManyTracksKeepOnlyViewportCardsRealized()
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new StubWorkstationAudioService(),
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
    public void MissingSavedSampleIsShownWithoutChangingItsFilename()
    {
        var track = new osuHitsounder.Core.Entities.Track { Samples = [new Sample { FileName = "missing.wav" }] };
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav", new ProjectData { Tracks = [track] }))
        };
        var vm = new WorkstationPageVM(source, new StubWorkstationAudioService(), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        var window = new Window { Content = page, Width = 880, Height = 600 };
        using var view = new InMemoryView(window);
        UiTestPump.Run(page.LoadCompletion);
        view.UpdateLayout();
        var list = (ItemsControl)page.FindName("TrackList");
        var card = (FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(0)!;
        var cue = FindVisualChildren<TextBlock>(card).Single(block => block.Name == "MissingSampleText");
        Assert.That(cue.Text, Does.Contain("missing.wav"));
        Assert.That(track.Samples.Single().FileName, Is.EqualTo("missing.wav"));
    }

    [Test]
    public void TrackGridFollowsWaveformZoomAndScroll()
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new StubWorkstationAudioService(),
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
        var vm = new WorkstationPageVM(source, new StubWorkstationAudioService(),
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
        Assert.That(grid.RequestSampleRemoval(10), Is.True);
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
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new StubWorkstationAudioService(), new ImmediateUiScheduler());
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
            Assert.That(bar.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(bar.IsEnabled, Is.True);
            Assert.That(bar.Value, Is.Zero.Within(1));
        });
    }

    [Test]
    public void WaveformSeekCommandUpdatesBoundMarkerPosition()
    {
        var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), audio, new ImmediateUiScheduler());
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
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), audio, new ImmediateUiScheduler());
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
            Assert.That(selector.DisplayText, Is.EqualTo("4"));
            Assert.That(AutomationProperties.GetName(selector), Is.EqualTo("Beat subdivision"));
            Assert.That(selector.ActualHeight, Is.GreaterThanOrEqualTo(28));
        });
        selector.Value = 8;
        Assert.That(audio.Snapshot, Is.EqualTo(before));
    }

    [Test]
    public void ZoomButtonsAndSelectorSynchronize()
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new StubWorkstationAudioService(), new ImmediateUiScheduler());
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
        Assert.That(buttons[0].IsEnabled, Is.False);
        buttons[1].Command.Execute(null);
        view.UpdateLayout();
        Assert.That(vm.ZoomFactor, Is.EqualTo(17));
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
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), audio, new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        var window = new Window { Content = page, Width = 900, Height = 600 };
        using var view = new InMemoryView(window);
        UiTestPump.Run(page.LoadCompletion);
        view.UpdateLayout();
        vm.SeekCommand.Execute(TimeSpan.FromSeconds(6));
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
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), audio, new ImmediateUiScheduler());
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
        vm.SeekCommand.Execute(TimeSpan.FromSeconds(8));
        view.UpdateLayout();
        Assert.That(ScreenX(), Is.GreaterThan(scroll.ViewportWidth));

        vm.ZoomInCommand.Execute(null);
        view.UpdateLayout();
        Assert.That(ScreenX(), Is.EqualTo(scroll.ViewportWidth / 2).Within(1));

        vm.SeekCommand.Execute(TimeSpan.Zero);
        vm.ZoomOutCommand.Execute(null);
        view.UpdateLayout();
        Assert.That(scroll.HorizontalOffset, Is.Zero.Within(1));

        vm.SeekCommand.Execute(TimeSpan.FromSeconds(10));
        vm.ZoomInCommand.Execute(null);
        view.UpdateLayout();
        Assert.That(scroll.HorizontalOffset, Is.EqualTo(scroll.ScrollableWidth).Within(1));

        for (var index = 0; index < 40; index++) vm.ZoomOutCommand.Execute(null);
        view.UpdateLayout();
        Assert.That(vm.ZoomFactor, Is.EqualTo(16));
        Assert.That(scroll.HorizontalOffset, Is.EqualTo(scroll.ScrollableWidth).Within(1));

        vm.SeekCommand.Execute(TimeSpan.FromSeconds(6));
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
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), audio, new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        var window = new Window { Content = page, Width = 900, Height = 600 };
        using var view = new InMemoryView(window);
        UiTestPump.Run(page.LoadCompletion);
        var before = audio.Snapshot;

        Assert.Multiple(() =>
        {
            Assert.That(page.HandleWaveformWheel(ModifierKeys.Control, 120), Is.True);
            Assert.That(vm.ZoomFactor, Is.EqualTo(17));
            Assert.That(page.HandleWaveformWheel(ModifierKeys.Control, 240), Is.True);
            Assert.That(vm.ZoomFactor, Is.EqualTo(19));
            Assert.That(page.HandleWaveformWheel(ModifierKeys.Control, -120), Is.True);
            Assert.That(vm.ZoomFactor, Is.EqualTo(18));
            Assert.That(page.HandleWaveformWheel(ModifierKeys.Alt, 120), Is.True);
            Assert.That(vm.SelectedSubdivisionIndex, Is.EqualTo(4));
            Assert.That(page.HandleWaveformWheel(ModifierKeys.Alt, -120), Is.True);
            Assert.That(vm.SelectedSubdivisionIndex, Is.EqualTo(3));
            Assert.That(audio.Snapshot, Is.EqualTo(before));
        });
    }

    [Test]
    public void SpaceTogglesPlaybackOncePerPressAndPreservesEditingInputs()
    {
        var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), audio, new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);

        Assert.That(page.HandlePlaybackKey(Key.Space, ModifierKeys.None, false, page), Is.True);
        Assert.That(vm.IsPlaying, Is.True);
        Assert.That(page.HandlePlaybackKey(Key.Space, ModifierKeys.None, true, page), Is.True);
        Assert.That(vm.IsPlaying, Is.True);
        Assert.That(page.HandlePlaybackKey(Key.Space, ModifierKeys.None, false, page), Is.True);
        Assert.That(vm.IsPlaying, Is.False);

        Assert.Multiple(() =>
        {
            Assert.That(page.HandlePlaybackKey(Key.Space, ModifierKeys.Control, false, page), Is.False);
            Assert.That(page.HandlePlaybackKey(Key.Enter, ModifierKeys.None, false, page), Is.False);
            Assert.That(page.HandlePlaybackKey(Key.Space, ModifierKeys.None, false, new TextBox()), Is.False);
            Assert.That(page.HandlePlaybackKey(Key.Space, ModifierKeys.None, false, new PasswordBox()), Is.False);
        });

        var selector = new ComboBox { IsDropDownOpen = true };
        Assert.That(page.HandlePlaybackKey(Key.Space, ModifierKeys.None, false, selector), Is.False);
    }

    [Test]
    public void ArrowsSeekSubdivisionLinesAndRespectNativeEditors()
    {
        var audio = new StubWorkstationAudioService();
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav",
                new ProjectData { Offsets = [0], BPMs = [120] }))
        };
        var vm = new WorkstationPageVM(source, audio, new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);
        Assert.That(page.HandlePlaybackKey(Key.Right, ModifierKeys.None, false, page), Is.True);
        Assert.That(audio.Snapshot.Position, Is.EqualTo(TimeSpan.FromMilliseconds(125)));
        Assert.That(page.HandlePlaybackKey(Key.Right, ModifierKeys.None, true, page), Is.True);
        Assert.That(audio.Snapshot.Position, Is.EqualTo(TimeSpan.FromMilliseconds(250)));
        Assert.That(page.HandlePlaybackKey(Key.Left, ModifierKeys.None, false, page), Is.True);
        Assert.That(audio.Snapshot.Position, Is.EqualTo(TimeSpan.FromMilliseconds(125)));
        Assert.Multiple(() =>
        {
            Assert.That(page.HandlePlaybackKey(Key.Left, ModifierKeys.None, false, new TextBox()), Is.False);
            Assert.That(page.HandlePlaybackKey(Key.Left, ModifierKeys.None, false, new ComboBox()), Is.False);
            Assert.That(page.HandlePlaybackKey(Key.Left, ModifierKeys.None, false, new Slider()), Is.False);
            Assert.That(page.HandlePlaybackKey(Key.Left, ModifierKeys.None, false, new ListBoxItem()), Is.False);
            Assert.That(page.HandlePlaybackKey(Key.Right, ModifierKeys.Alt, false, page), Is.False);
        });
        Assert.That(audio.Snapshot.Position, Is.EqualTo(TimeSpan.FromMilliseconds(125)));
    }

    [Test]
    public void ControlArrowsSeekWholeBeatsAndKeepEditorKeys()
    {
        var audio = new StubWorkstationAudioService();
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav",
                new ProjectData { Offsets = [0, 800], BPMs = [120, 60] }))
        };
        var vm = new WorkstationPageVM(source, audio, new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);

        Assert.That(page.HandlePlaybackKey(Key.Right, ModifierKeys.Control, false, page), Is.True);
        Assert.That(audio.Snapshot.Position, Is.EqualTo(TimeSpan.FromMilliseconds(500)));
        Assert.That(page.HandlePlaybackKey(Key.Right, ModifierKeys.Control, true, page), Is.True);
        Assert.That(audio.Snapshot.Position, Is.EqualTo(TimeSpan.FromMilliseconds(800)));
        Assert.That(page.HandlePlaybackKey(Key.Left, ModifierKeys.Control, false, page), Is.True);
        Assert.That(audio.Snapshot.Position, Is.EqualTo(TimeSpan.FromMilliseconds(500)));
        Assert.That(page.HandlePlaybackKey(Key.Right, ModifierKeys.Control, false, new TextBox()), Is.False);
        Assert.That(page.HandlePlaybackKey(Key.Right, ModifierKeys.Control, false, new Slider()), Is.False);
        Assert.That(audio.Snapshot.Position, Is.EqualTo(TimeSpan.FromMilliseconds(500)));
        Assert.That(page.HandlePlaybackKey(Key.Right, ModifierKeys.Control | ModifierKeys.Shift, false, page), Is.False);
        audio.Snapshot = new AudioPlaybackSnapshot(TimeSpan.Zero, true, null);
        Assert.That(page.HandlePlaybackKey(Key.Left, ModifierKeys.Control, false, page), Is.True);
        Assert.That(audio.Snapshot.Position, Is.EqualTo(TimeSpan.Zero));
        Assert.That(audio.Snapshot.IsPlaying, Is.True);
        audio.Snapshot = new AudioPlaybackSnapshot(TimeSpan.FromSeconds(10), true, null);
        Assert.That(page.HandlePlaybackKey(Key.Right, ModifierKeys.Control, false, page), Is.True);
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
        var vm = new WorkstationPageVM(source, audio, new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600,
            FlowDirection = direction });
        UiTestPump.Run(page.LoadCompletion);
        view.UpdateLayout();
        var scroll = (ScrollViewer)page.FindName("WaveformScrollViewer");
        scroll.ScrollToLeftEnd();
        view.UpdateLayout();
        Assert.That(page.HandlePlaybackKey(Key.Right, ModifierKeys.None, false, page), Is.True);
        Assert.That(audio.Snapshot.Position, Is.EqualTo(TimeSpan.FromMilliseconds(8125)));
        view.UpdateLayout();
        Assert.That(scroll.HorizontalOffset, Is.GreaterThan(0));
        var before = scroll.HorizontalOffset;
        Assert.That(page.HandlePlaybackKey(Key.Left, ModifierKeys.None, false, page), Is.True);
        view.UpdateLayout();
        Assert.That(scroll.HorizontalOffset, Is.EqualTo(before).Within(1));
    }

    [Test]
    public void SpaceIsRejectedUntilLoadedAndAfterDeactivation()
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new StubWorkstationAudioService(), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        Assert.That(page.HandlePlaybackKey(Key.Space, ModifierKeys.None, false, page), Is.False);

        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);
        UiTestPump.Run(page.DeactivateAsync());
        Assert.That(page.HandlePlaybackKey(Key.Space, ModifierKeys.None, false, page), Is.False);
    }

    [Test]
    public void ModifierWheelAccumulatesPerModeConsumesBoundsAndIgnoresOtherModifiers()
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new StubWorkstationAudioService(), new ImmediateUiScheduler());
        var unloadedPage = new WorkstationPage(vm);
        Assert.That(unloadedPage.HandleWaveformWheel(ModifierKeys.Control, 120), Is.False);

        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        var window = new Window { Content = page, Width = 900, Height = 600 };
        using var view = new InMemoryView(window);
        UiTestPump.Run(page.LoadCompletion);

        Assert.That(page.HandleWaveformWheel(ModifierKeys.Control, 60), Is.True);
        Assert.That(vm.ZoomFactor, Is.EqualTo(16));
        Assert.That(page.HandleWaveformWheel(ModifierKeys.Control, 60), Is.True);
        Assert.That(vm.ZoomFactor, Is.EqualTo(17));

        Assert.That(page.HandleWaveformWheel(ModifierKeys.Control, 60), Is.True);
        Assert.That(page.HandleWaveformWheel(ModifierKeys.Alt, 60), Is.True);
        Assert.That(page.HandleWaveformWheel(ModifierKeys.Alt, 60), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(vm.ZoomFactor, Is.EqualTo(17));
            Assert.That(vm.SelectedSubdivisionIndex, Is.EqualTo(4));
        });

        Assert.Multiple(() =>
        {
            Assert.That(page.HandleWaveformWheel(ModifierKeys.None, 120), Is.False);
            Assert.That(page.HandleWaveformWheel(ModifierKeys.Control | ModifierKeys.Alt, 120), Is.False);
        });

        for (var index = 0; index < 40; index++) vm.ZoomInCommand.Execute(null);
        Assert.That(page.HandleWaveformWheel(ModifierKeys.Control, 120), Is.True);
        Assert.That(vm.ZoomFactor, Is.EqualTo(32));
        vm.SelectedSubdivisionIndex = 8;
        Assert.That(page.HandleWaveformWheel(ModifierKeys.Alt, 120), Is.True);
        Assert.That(vm.SelectedSubdivisionIndex, Is.EqualTo(8));
    }

    [Test]
    public void ControlShiftWheelChangesOnlyTrackLaneHeight()
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new StubWorkstationAudioService(),
            new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);
        var zoom = vm.ZoomFactor;
        var waveformHeight = ((FrameworkElement)page.FindName("WaveformDisplay")).ActualHeight;

        Assert.That(page.HandleWaveformWheel(ModifierKeys.Control | ModifierKeys.Shift, 60), Is.True);
        Assert.That(vm.TrackHeight, Is.EqualTo(72));
        Assert.That(page.HandleWaveformWheel(ModifierKeys.Control | ModifierKeys.Shift, 60), Is.True);
        view.UpdateLayout();

        Assert.Multiple(() =>
        {
            Assert.That(vm.TrackHeight, Is.EqualTo(84));
            Assert.That(vm.ZoomFactor, Is.EqualTo(zoom));
            Assert.That(((FrameworkElement)page.FindName("WaveformDisplay")).ActualHeight, Is.EqualTo(waveformHeight));
        });
    }

    [Test]
    public void ShiftWheelPansWaveformWithoutSeekingAndConsumesBounds()
    {
        var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), audio, new ImmediateUiScheduler());
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
    public void ZoomButtonsStayReachableAcrossLayouts(
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
            var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new StubWorkstationAudioService(), new ImmediateUiScheduler());
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
            var barBounds = bar.TransformToAncestor(page).TransformBounds(new Rect(bar.RenderSize));
            var zoomBounds = zoomControls.TransformToAncestor(page).TransformBounds(new Rect(zoomControls.RenderSize));
            listener.Flush();
            Assert.Multiple(() =>
            {
                Assert.That(buttons.Length, Is.EqualTo(2));
                Assert.That(AutomationProperties.GetName(selector), Is.EqualTo("Beat subdivision"));
                foreach (var button in buttons)
                {
                    var bounds = button.TransformToAncestor(page).TransformBounds(new Rect(button.RenderSize));
                    Assert.That(bounds.Left, Is.GreaterThanOrEqualTo(0));
                    Assert.That(bounds.Right, Is.LessThanOrEqualTo(page.ActualWidth));
                    Assert.That(bounds.Top, Is.GreaterThanOrEqualTo(0));
                    Assert.That(bounds.Bottom, Is.LessThanOrEqualTo(page.ActualHeight));
                    Assert.That(AutomationProperties.GetName(button), Is.Not.Empty);
                }
                Assert.That(barBounds.Left, Is.GreaterThanOrEqualTo(0));
                Assert.That(Grid.GetColumn(bar), Is.EqualTo(0));
                Assert.That(Grid.GetColumn(zoomControls), Is.EqualTo(1));
                Assert.That(flowDirection == FlowDirection.RightToLeft || barBounds.Right <= zoomBounds.Left, Is.True);
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
        var vm = new WorkstationPageVM(source, new StubWorkstationAudioService(), new ImmediateUiScheduler());
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
        Assert.That(buttons[1].IsEnabled, Is.True);
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
                     "Styles/TextBlock.xaml", "Styles/ItemsControl.xaml", "Styles/Grid.xaml", "Styles/Border.xaml", "Styles/ContentPresenter.xaml",
                     "Styles/Button.xaml", "Styles/ToolTip.xaml", "Styles/ScrollViewer.xaml", "Styles/TextBox.xaml", "Styles/ComboBox.xaml", "Styles/ListBox.xaml", "Styles/Slider.xaml", "BeatGrid.xaml" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"/osuHitsounder;component/Resources/{resource}", UriKind.Relative)
            });
    }
}
