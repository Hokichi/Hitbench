using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Automation;
using System.Windows.Input;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.ViewModels.Shell;
using osuHitsounder.Views.Pages;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class WorkstationPageIntegrationTests
{
    [SetUp]
    public void SetUp() => EnsureResources();

    [Test]
    public void LoadsWaveformSwitchesTransportIconAndFitsViewport()
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
        vm.ZoomInCommand.Execute(null);
        view.UpdateLayout();
        Assert.That(waveform.DesiredSize.Width, Is.GreaterThan(scroll.ViewportWidth));
        scroll.ScrollToHorizontalOffset(50);
        var fit = (BalloonButton)page.FindName("FitButton");
        fit.Command.Execute(null);
        fit.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.Multiple(() =>
        {
            Assert.That(vm.ZoomFactor, Is.EqualTo(1));
            Assert.That(scroll.HorizontalOffset, Is.Zero);
        });
    }

    [Test]
    public void ExposesDummyActionsFooterAndAccessibleViewport()
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new StubWorkstationAudioService(), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        var window = new Window { Content = page, Width = 640, Height = 480, FlowDirection = FlowDirection.RightToLeft };
        using var view = new InMemoryView(window);
        UiTestPump.Run(page.LoadCompletion);
        view.UpdateLayout();

        Assert.Multiple(() =>
        {
            Assert.That(((BalloonButton)page.FindName("SampleTrackButton")).ButtonText, Is.EqualTo("Sample Track"));
            Assert.That(((BalloonButton)page.FindName("PianoTrackButton")).ButtonText, Is.EqualTo("Piano Track"));
            Assert.That(((FrameworkElement)page.FindName("ZoomFooter")).HorizontalAlignment, Is.EqualTo(HorizontalAlignment.Right));
            Assert.That(((ScrollViewer)page.FindName("WaveformScrollViewer")).Focusable, Is.True);
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
    public void SubdivisionSelectorIsTransparentAccessibleAndHasNoPlaybackEffect()
    {
        var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), audio, new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        var window = new Window { Content = page, Width = 640, Height = 480, FlowDirection = FlowDirection.RightToLeft };
        using var view = new InMemoryView(window);
        UiTestPump.Run(page.LoadCompletion);
        view.UpdateLayout();

        var selector = (ComboBox)page.FindName("SubdivisionSelector");
        var before = audio.Snapshot;
        Assert.Multiple(() =>
        {
            Assert.That(selector.Items.Cast<ComboBoxItem>().Select(item => item.Content),
                Is.EqualTo(new[] { "1/1", "1/2", "1/3", "1/4", "1/5", "1/6", "1/8", "1/12", "1/16" }));
            Assert.That(selector.SelectedIndex, Is.EqualTo(3));
            Assert.That(selector.Background, Is.EqualTo(Brushes.Transparent));
            Assert.That(AutomationProperties.GetName(selector), Is.EqualTo("Beat subdivision"));
            Assert.That(selector.ActualHeight, Is.EqualTo(40));
        });
        selector.SelectedIndex = 8;
        Assert.That(audio.Snapshot, Is.EqualTo(before));
    }

    [Test]
    public void ZoomEditorCommitsCancelsValidatesAndSelectorSynchronizes()
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new StubWorkstationAudioService(), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        var window = new Window { Content = page, Width = 900, Height = 600 };
        using var view = new InMemoryView(window);
        using var source = new InMemoryPresentationSource();
        UiTestPump.Run(page.LoadCompletion);
        view.UpdateLayout();

        var editor = (TextBox)page.FindName("ZoomEditor");
        var validation = (TextBlock)page.FindName("ZoomValidationText");
        var selector = (ComboBox)page.FindName("SubdivisionSelector");

        editor.Text = "5";
        editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        editor.RaiseEvent(CreateKeyEvent(source, Key.Enter));
        view.UpdateLayout();
        Assert.Multiple(() =>
        {
            Assert.That(vm.ZoomFactor, Is.EqualTo(5));
            Assert.That(editor.Text, Is.EqualTo("5"));
            Assert.That(AutomationProperties.GetName(editor), Is.EqualTo("Waveform zoom level"));
            Assert.That(editor.IsEnabled, Is.True);
        });

        editor.Text = "bad";
        editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        editor.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount, editor, null)
        {
            RoutedEvent = Keyboard.LostKeyboardFocusEvent
        });
        view.UpdateLayout();
        Assert.Multiple(() =>
        {
            Assert.That(vm.ZoomFactor, Is.EqualTo(5));
            Assert.That(vm.HasZoomValidationError, Is.True);
            Assert.That(validation.Text, Is.EqualTo("Enter a whole number from 1 to 16."));
            Assert.That(validation.Visibility, Is.EqualTo(Visibility.Visible));
        });

        editor.RaiseEvent(CreateKeyEvent(source, Key.Escape));
        view.UpdateLayout();
        Assert.Multiple(() =>
        {
            Assert.That(editor.Text, Is.EqualTo("5"));
            Assert.That(vm.HasZoomValidationError, Is.False);
            Assert.That(validation.Visibility, Is.EqualTo(Visibility.Collapsed));
        });

        selector.SelectedIndex = 8;
        selector.GetBindingExpression(Selector.SelectedIndexProperty)!.UpdateSource();
        Assert.That(vm.SelectedSubdivisionIndex, Is.EqualTo(8));
        vm.SelectedSubdivisionIndex = 2;
        view.UpdateLayout();
        Assert.That(selector.SelectedIndex, Is.EqualTo(2));
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
        var before = ScreenX();

        vm.ZoomInCommand.Execute(null);
        view.UpdateLayout();

        Assert.Multiple(() =>
        {
            Assert.That(ScreenX(), Is.EqualTo(before).Within(1));
            Assert.That(audio.Snapshot.Position, Is.EqualTo(TimeSpan.FromSeconds(6)));
        });

        vm.ZoomDraft = "4";
        vm.CommitZoomCommand.Execute(null);
        view.UpdateLayout();
        Assert.That(ScreenX(), Is.EqualTo(before).Within(1));
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

        vm.ZoomDraft = "4";
        vm.CommitZoomCommand.Execute(null);
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

        vm.FitCommand.Execute(null);
        view.UpdateLayout();
        Assert.That(scroll.HorizontalOffset, Is.Zero.Within(1));

        vm.SeekCommand.Execute(TimeSpan.FromSeconds(6));
        var before = ScreenX();
        vm.ZoomInCommand.Execute(null);
        vm.ZoomInCommand.Execute(null);
        view.UpdateLayout();
        Assert.That(ScreenX(), Is.EqualTo(before).Within(1));
    }

    private static KeyEventArgs CreateKeyEvent(PresentationSource source, Key key) => new(
        Keyboard.PrimaryDevice,
        source,
        Environment.TickCount,
        key)
    {
        RoutedEvent = Keyboard.PreviewKeyDownEvent
    };

    private static void EnsureResources()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Clear();
        UiTestPump.EnsureFontResources(app);
        foreach (var resource in new[] { "Themes/Dark.xaml", "Converters/Converters.xaml", "Icons.xaml",
                     "Styles/TextBlock.xaml", "Styles/Grid.xaml", "Styles/Border.xaml", "Styles/ContentPresenter.xaml",
                     "Styles/Button.xaml", "Styles/ToolTip.xaml", "Styles/ScrollViewer.xaml", "Styles/TextBox.xaml", "Styles/ComboBox.xaml" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"/osuHitsounder;component/Resources/{resource}", UriKind.Relative)
            });
    }
}
