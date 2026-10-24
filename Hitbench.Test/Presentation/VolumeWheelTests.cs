using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Hitbench.Resources.CustomControls;
using Hitbench.ViewModels.Shell;
using Hitbench.Views.Pages;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class VolumeWheelTests
{
    [TestCase(0d, 100d, 50d)]
    [TestCase(20d, 80d, 50d)]
    [TestCase(80d, 80d, 80d)]
    public void RangeProviderPreservesBoundsAndClampsValues(double minimum, double maximum, double value)
    {
        var wheel = new VolumeWheel { Minimum = minimum, Maximum = maximum, Value = value, IsActive = true };
        var peer = UIElementAutomationPeer.CreatePeerForElement(wheel);
        var provider = (IRangeValueProvider)peer.GetPattern(PatternInterface.RangeValue);
        Assert.That(provider.Minimum, Is.EqualTo(minimum));
        Assert.That(provider.Maximum, Is.EqualTo(maximum));
        Assert.That(provider.Value, Is.EqualTo(value));
        provider.SetValue(maximum + 10);
        Assert.That(wheel.Value, Is.EqualTo(maximum));
        provider.SetValue(minimum - 10);
        Assert.That(wheel.Value, Is.EqualTo(minimum));
        wheel.IsActive = false;
        Assert.That(provider.IsReadOnly, Is.True);
        provider.SetValue(maximum);
        Assert.That(wheel.Value, Is.EqualTo(minimum));
    }

    [TestCase(FlowDirection.LeftToRight)]
    [TestCase(FlowDirection.RightToLeft)]
    public void WheelUsesAssignedStyleAndBindsLabelAcrossStates(FlowDirection direction)
    {
        var wheel = new VolumeWheel
        {
            Label = "Application volume with a long localized name", Value = 50,
            FlowDirection = direction,
            Style = (Style)Application.Current.FindResource("BaseVolumeWheelStyle")
        };
        UiTestPump.Layout(wheel, 320, 168);
        UiControlContract.AssertStyle<VolumeWheel>(wheel, "BaseVolumeWheelStyle");
        UiControlContract.AssertTemplate(wheel, "OHsVolumeWheelTemplate");
        Assert.That(AutomationProperties.GetName(wheel), Is.EqualTo($"{wheel.Label} volume"));
        wheel.IsActive = true;
        wheel.IsEnabled = false;
        Assert.That(wheel.IsActive, Is.True);
        Assert.That(wheel.IsEnabled, Is.False);
        UiControlContract.AssertStyle<VolumeWheel>(wheel, "BaseVolumeWheelStyle");
    }

    [Test]
    public void IdleDismissalStartsAt100Milliseconds()
    {
        var (page, _, view) = CreateLoadedPage();
        using (view)
        {
            page.ShowVolumeOverlay(false, new Point());
            page.ProcessVolumeOverlayIdle(TimeSpan.FromMilliseconds(99), false);
            Assert.That(page.IsVolumeOverlayOpen, Is.True);
            page.ApplicationVolumeWheel.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });
            page.ActivateVolumeWheel(page.SongVolumeWheel);
            Assert.That(page.ActiveVolumeWheel, Is.SameAs(page.SongVolumeWheel));
            page.ProcessVolumeOverlayIdle(TimeSpan.FromMilliseconds(100), false);
            Assert.That(page.IsVolumeOverlayOpen, Is.False);
        }
    }

    [Test]
    public void ActivityCancelsFadeAndDismissalClearsActiveWheel()
    {
        var (page, _, view) = CreateLoadedPage();
        using (view)
        {
            page.ShowVolumeOverlay(false, new Point());
            page.ProcessVolumeOverlayIdle(TimeSpan.FromMilliseconds(100), true);
            var overlay = (Border)page.FindName("VolumeOverlay");
            page.RecordVolumeActivity();
            Assert.That(page.IsVolumeOverlayOpen, Is.True);
            PumpWithVolumeActivity(page, TimeSpan.FromMilliseconds(250));
            Assert.That(page.IsVolumeOverlayOpen, Is.True);
            page.ApplicationVolumeWheel.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });
            page.DismissVolumeOverlay();
            Assert.That(page.ActiveVolumeWheel, Is.Null);
            page.ShowVolumeOverlay(false, new Point());
            page.ProcessVolumeOverlayIdle(TimeSpan.Zero, false);
            Assert.That(page.IsVolumeOverlayOpen, Is.True);
        }
    }
    [Test]
    public void PointerActivityOnOverlayPaddingCancelsFade()
    {
        var (page, _, view) = CreateLoadedPage();
        using (view)
        {
            page.ShowVolumeOverlay(false, new Point());
            page.ProcessVolumeOverlayIdle(TimeSpan.FromMilliseconds(100), true);
            var overlay = (Border)page.FindName("VolumeOverlay");
            overlay.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.PreviewMouseMoveEvent });
            page.ProcessVolumeOverlayIdle(TimeSpan.FromMilliseconds(99), false);
            Assert.That(page.IsVolumeOverlayOpen, Is.True);
            page.ProcessVolumeOverlayIdle(TimeSpan.FromMilliseconds(100), false);
            Assert.That(page.IsVolumeOverlayOpen, Is.False);
        }
    }

    [Test]
    public void PointerPressMoveAndReleaseNeverChangesVolumeOrCapturesMouse()
    {
        var wheel = new VolumeWheel { Value = 50, IsActive = true,
            Style = (Style)Application.Current.FindResource("BaseVolumeWheelStyle") };
        var host = new Grid();
        host.Children.Add(wheel);
        UiTestPump.Layout(host, 320, 168);
        wheel.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });
        Assert.That(wheel.IsPressed, Is.True);
        wheel.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 1) { RoutedEvent = Mouse.MouseMoveEvent });
        wheel.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 2, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent });
        Assert.Multiple(() =>
        {
            Assert.That(wheel.IsPressed, Is.False);
            Assert.That(wheel.Value, Is.EqualTo(50));
            Assert.That(wheel.IsMouseCaptured, Is.False);
        });
    }

    [TestCase("leave")]
    [TestCase("disable")]
    [TestCase("unload")]
    public void InterruptedPointerPressClearsWithoutChangingVolume(string interruption)
    {
        var wheel = new VolumeWheel { Value = 50, IsActive = true };
        wheel.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });
        Assert.That(wheel.IsPressed, Is.True);
        switch (interruption)
        {
            case "leave": wheel.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 1) { RoutedEvent = Mouse.MouseLeaveEvent }); break;
            case "disable": wheel.IsEnabled = false; break;
            case "unload": wheel.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); break;
        }
        Assert.Multiple(() =>
        {
            Assert.That(wheel.IsPressed, Is.False);
            Assert.That(wheel.Value, Is.EqualTo(50));
            Assert.That(wheel.IsMouseCaptured, Is.False);
        });
    }
    [Test]
    public void OverlayKeepsAssignedStyleAcrossAnchorsAndResize()
    {
        var (page, _, view) = CreateLoadedPage();
        using (view)
        {
            var overlay = (Border)page.FindName("VolumeOverlay");
            foreach (var direction in new[] { FlowDirection.LeftToRight, FlowDirection.RightToLeft })
            foreach (var anchor in new[] { new Point(500, 400), new Point(30, 50) })
            {
                page.FlowDirection = direction;
                page.ShowVolumeOverlay(false, anchor);
                view.UpdateLayout();
                Assert.That(page.IsVolumeOverlayOpen, Is.True);
                UiControlContract.AssertStyle<Border>(overlay, "OHsWorkstationVolumeOverlayStyle");
                page.DismissVolumeOverlay();
            }
            page.ShowVolumeOverlay(false, new Point(500, 400));
            page.Width = 180;
            page.Height = 250;
            view.UpdateLayout();
            Assert.That(page.IsVolumeOverlayOpen, Is.True);
            UiControlContract.AssertStyle<Border>(overlay, "OHsWorkstationVolumeOverlayStyle");
        }
    }

    [Test]
    public void OpenOverlayKeepsScrollBehaviorAfterResize()
    {
        var (page, _, view) = CreateLoadedPage();
        using (view)
        {
            page.ShowVolumeOverlay(false, new Point(500, 400));
            view.UpdateLayout();
            page.Width = 180;
            page.Height = 250;
            view.UpdateLayout();
            var overlay = (Border)page.FindName("VolumeOverlay");
            UiControlContract.AssertStyle<Border>(overlay, "OHsWorkstationVolumeOverlayStyle");
            var scroll = (ScrollViewer)overlay.Child;
            scroll.Width = 100;
            scroll.Height = 100;
            var content = (FrameworkElement)scroll.Content;
            content.Width = 500;
            content.Height = 500;
            view.UpdateLayout();
            Assert.That(scroll.ScrollableWidth, Is.GreaterThan(0));
            Assert.That(scroll.ScrollableHeight, Is.GreaterThan(0));
            scroll.ScrollToRightEnd();
            scroll.ScrollToBottom();
            view.UpdateLayout();
            Assert.That(scroll.HorizontalOffset, Is.EqualTo(scroll.ScrollableWidth));
            Assert.That(scroll.VerticalOffset, Is.EqualTo(scroll.ScrollableHeight));
        }
    }

    [Test]
    public void ExplicitAutomationNameSurvivesLabelChanges()
    {
        var wheel = new VolumeWheel();
        AutomationProperties.SetName(wheel, "Song volume");
        wheel.Label = "Audio";
        Assert.That(AutomationProperties.GetName(wheel), Is.EqualTo("Song volume"));
    }

    [Test]
    public void MasterVolumesExposeIndependentNamedControls()
    {
        var (page, _, view) = CreateLoadedPage();
        using (view)
        {
            page.ShowVolumeOverlay(false, new Point(0, 0));
            view.UpdateLayout();
            var panel = (Panel)page.FindName("VolumeWheelPanel");
            Assert.That(panel.Children.OfType<VolumeWheel>().Select(AutomationProperties.GetName),
                Is.EquivalentTo(new[] { "Song volume", "Application volume", "Samples volume" }));
            Assert.Multiple(() =>
            {
                Assert.That(page.SongVolumeWheel.Label, Is.EqualTo("Audio"));
                Assert.That(page.ApplicationVolumeWheel.Label, Is.EqualTo("Master"));
                Assert.That(page.SamplesVolumeWheel.Label, Is.EqualTo("Samples"));
            });
            foreach (var wheel in panel.Children.OfType<VolumeWheel>())
                UiControlContract.AssertStyle<VolumeWheel>(wheel, "BaseVolumeWheelStyle");
        }
    }
    [SetUp]
    public void SetUp()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Clear();
        UiTestPump.EnsureFontResources(app);
        foreach (var resource in new[] { "Themes/Dark.xaml", "Themes/Tokens.xaml", "Converters/Converters.xaml", "Icons.xaml", "Styles/TextBlock.xaml",
                     "Styles/ItemsControl.xaml", "Styles/StackPanel.xaml", "Styles/ColumnDefinition.xaml", "Styles/Grid.xaml", "Styles/Border.xaml", "Styles/ContentPresenter.xaml",
                     "Styles/Button.xaml", "Styles/MenuItem.xaml", "Styles/ContextMenu.xaml", "Styles/BalloonComboBox.xaml", "Styles/ToolTip.xaml", "Styles/ScrollViewer.xaml", "Styles/TextBox.xaml",
                     "Styles/ComboBox.xaml", "Styles/ListBox.xaml", "Styles/Slider.xaml" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"/Hitbench;component/Resources/{resource}", UriKind.Relative)
            });
    }

    [Test]
    public void WheelExposesAccessibleRangeAndActiveState()
    {
        var wheel = new VolumeWheel { Label = "Application", Value = 50 };
        using var view = new InMemoryView(new Window { Content = wheel });
        view.UpdateLayout();

        Assert.Multiple(() =>
        {
            Assert.That(wheel.Minimum, Is.Zero);
            Assert.That(wheel.Maximum, Is.EqualTo(100));
            Assert.That(wheel.SmallChange, Is.EqualTo(5));
            Assert.That(wheel.IsActive, Is.False);
            Assert.That(wheel.FilledGeometry, Is.Not.Null);
            Assert.That(AutomationProperties.GetName(wheel), Is.EqualTo("Application volume"));
        });

        wheel.IsActive = true;
        Assert.That(wheel.IsActive, Is.True);
    }

    [Test]
    public void AutomationCanChangeOnlyTheActiveEnabledWheel()
    {
        var wheel = new VolumeWheel { Label = "Samples", Value = 50 };
        var peer = UIElementAutomationPeer.CreatePeerForElement(wheel) ??
                   UIElementAutomationPeer.CreatePeerForElement(wheel);
        var provider = (IRangeValueProvider)peer!.GetPattern(PatternInterface.RangeValue)!;

        Assert.That(provider.IsReadOnly, Is.True);
        provider.SetValue(25);
        Assert.That(wheel.Value, Is.EqualTo(50));
        wheel.IsActive = true;
        wheel.IsEnabled = false;
        provider.SetValue(25);
        Assert.That(wheel.Value, Is.EqualTo(50));
        wheel.IsEnabled = true;
        Assert.That(provider.IsReadOnly, Is.False);
        provider.SetValue(25);
        Assert.That(wheel.Value, Is.EqualTo(25));
    }

    [Test]
    public void FirstOutsideAltWheelRevealsWithoutAdjustmentThenChangesOnlyActiveWheel()
    {
        var (page, vm, view) = CreateLoadedPage();
        using (view)
        {
            Assert.That(page.HandleContextWheel(ModifierKeys.Alt, 120, page), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(page.IsVolumeOverlayOpen, Is.True);
                Assert.That(page.ActiveVolumeWheel, Is.SameAs(page.ApplicationVolumeWheel));
                Assert.That(vm.ApplicationVolume, Is.EqualTo(100));
                Assert.That(vm.SongVolume, Is.EqualTo(100));
                Assert.That(vm.SamplesVolume, Is.EqualTo(100));
            });

            Assert.That(page.HandleContextWheel(ModifierKeys.Alt, -120, page), Is.True);
            Assert.That(vm.ApplicationVolume, Is.EqualTo(95));

            page.ActivateVolumeWheel(page.SamplesVolumeWheel);
            Assert.That(page.HandleContextWheel(ModifierKeys.Alt, -120, page), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(vm.ApplicationVolume, Is.EqualTo(95));
                Assert.That(vm.SamplesVolume, Is.EqualTo(95));
                Assert.That(page.ApplicationVolumeWheel.IsActive, Is.False);
                Assert.That(page.SamplesVolumeWheel.IsActive, Is.True);
            });
        }
    }

    [Test]
    public void RevealAndSelectionChangesDiscardPartialWheelDeltas()
    {
        var (page, vm, view) = CreateLoadedPage();
        using (view)
        {
            page.HandleContextWheel(ModifierKeys.Alt, 60, page);
            page.HandleContextWheel(ModifierKeys.Alt, 60, page);
            Assert.That(vm.ApplicationVolume, Is.EqualTo(100));

            page.ActivateVolumeWheel(page.SongVolumeWheel);
            page.HandleContextWheel(ModifierKeys.Alt, -60, page);
            page.ActivateVolumeWheel(page.SamplesVolumeWheel);
            page.HandleContextWheel(ModifierKeys.Alt, -60, page);

            Assert.Multiple(() =>
            {
                Assert.That(vm.SongVolume, Is.EqualTo(100));
                Assert.That(vm.SamplesVolume, Is.EqualTo(100));
            });
        }
    }

    [Test]
    public void CardAndOverlayContextsDoNotSharePartialWheelDeltas()
    {
        var (page, vm, view) = CreateLoadedPage();
        using (view)
        {
            var card = (Border)((Hitbench.Views.Pages.OHsWorkstationAudioCard)page.FindName("AudioCard")).FindName("SongAudioCard");
            page.HandleContextWheel(ModifierKeys.Control | ModifierKeys.Alt, 60, card);
            page.HandleContextWheel(ModifierKeys.Alt, 120, page);
            page.HandleContextWheel(ModifierKeys.Control | ModifierKeys.Alt, 60, card);
            Assert.That(vm.SelectedSubdivisionIndex, Is.EqualTo(3));

            page.HandleContextWheel(ModifierKeys.Alt, -60, page);
            page.HandleContextWheel(ModifierKeys.Control | ModifierKeys.Alt, 60, card);
            page.HandleContextWheel(ModifierKeys.Alt, -60, page);
            Assert.That(vm.ApplicationVolume, Is.EqualTo(100));
        }
    }

    [Test]
    public void ControlAltOnSongCardChangesSubdivisionAndKeyboardOverlayRestoresFocusIntent()
    {
        var (page, vm, view) = CreateLoadedPage();
        using (view)
        {
            var card = (Border)((Hitbench.Views.Pages.OHsWorkstationAudioCard)page.FindName("AudioCard")).FindName("SongAudioCard");
            Assert.That(page.HandleContextWheel(ModifierKeys.Control | ModifierKeys.Alt, 120, card), Is.True);
            Assert.That(vm.SelectedSubdivisionIndex, Is.EqualTo(4));
            Assert.That(page.IsVolumeOverlayOpen, Is.False);

            Assert.That(page.HandleVolumeShortcut(Key.V, ModifierKeys.Alt, page), Is.True);
            Assert.That(page.IsVolumeOverlayOpen, Is.True);
            var active = page.ActiveVolumeWheel;
            Assert.That(page.HandleVolumeOverlayKey(Key.Tab, ModifierKeys.None), Is.True);
            Assert.That(page.ActiveVolumeWheel, Is.SameAs(active));
            Assert.That(page.HandleVolumeOverlayKey(Key.Down, ModifierKeys.None), Is.True);
            Assert.That(vm.ApplicationVolume, Is.EqualTo(95));
            Assert.That(vm.SamplesVolume, Is.EqualTo(100));
            Assert.That(page.HandleVolumeOverlayKey(Key.Escape, ModifierKeys.None), Is.True);
            Assert.That(page.IsVolumeOverlayOpen, Is.False);
        }
    }

    [Test]
    public void AltVOpensOverlayFromNativeEditors()
    {
        var (page, _, view) = CreateLoadedPage();
        using (view)
        {
            Assert.That(page.HandleVolumeShortcut(Key.V, ModifierKeys.Alt, new TextBox()), Is.True);
            Assert.That(page.IsVolumeOverlayOpen, Is.True);
        }
    }

    [Test]
    public void OverlayKeepsStyleAndRightToLeftFlowAtNarrowWidth()
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new Hitbench.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm) { FlowDirection = FlowDirection.RightToLeft };
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 180, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);
        page.ShowVolumeOverlay(false, new Point(500, 500));
        view.UpdateLayout();
        var overlay = (Border)page.FindName("VolumeOverlay");

        UiControlContract.AssertStyle<Border>(overlay, "OHsWorkstationVolumeOverlayStyle");
        Assert.Multiple(() =>
        {
            Assert.That(((StackPanel)page.FindName("VolumeWheelPanel")).FlowDirection,
                Is.EqualTo(FlowDirection.RightToLeft));
        });
    }

    internal static void PumpWithVolumeActivity(WorkstationPage page, TimeSpan duration)
    {
        page.RecordVolumeActivity();
        var activity = new DispatcherTimer(DispatcherPriority.Send, page.Dispatcher) { Interval = TimeSpan.FromMilliseconds(25) };
        activity.Tick += (_, _) => page.RecordVolumeActivity();
        activity.Start();
        try { UiTestPump.Run(Task.Delay(duration)); }
        finally { activity.Stop(); }
    }

    private static (WorkstationPage Page, WorkstationPageVM ViewModel, InMemoryView View) CreateLoadedPage(bool physicalCoordinates = false)
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new Hitbench.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        FrameworkElement content = page;
        if (physicalCoordinates)
        {
            var host = new Grid { FlowDirection = FlowDirection.LeftToRight };
            host.Children.Add(page);
            content = host;
        }
        var view = new InMemoryView(new Window { Content = content, Width = 900, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);
        view.UpdateLayout();
        return (page, vm, view);
    }
}
