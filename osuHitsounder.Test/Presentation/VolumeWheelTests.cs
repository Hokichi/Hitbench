using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.ViewModels.Shell;
using osuHitsounder.Views.Pages;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class VolumeWheelTests
{
    [TestCase(0d, 0d)]
    [TestCase(50d, .5d)]
    [TestCase(100d, 1d)]
    public void ProgressDrawingReflectsValueFraction(double value, double fraction)
    {
        var full = new VolumeWheel { Minimum = 0, Maximum = 100, Value = 100 };
        var wheel = new VolumeWheel { Minimum = 0, Maximum = 100, Value = value };
        var area = full.FilledGeometry.GetArea();
        Assert.That(area, Is.GreaterThan(0));
        Assert.That(wheel.FilledGeometry.GetArea(), Is.EqualTo(area * fraction).Within(area * .001));
    }

    [Test]
    public void ProgressNormalizesCustomRangeAndHandlesDegenerateRange()
    {
        var full = new VolumeWheel { Minimum = 20, Maximum = 80, Value = 80 };
        var wheel = new VolumeWheel { Minimum = 20, Maximum = 80, Value = 50 };
        var area = full.FilledGeometry.GetArea();
        Assert.That(area, Is.GreaterThan(0));
        Assert.That(wheel.FilledGeometry.GetArea(), Is.EqualTo(area / 2).Within(area * .001));
        wheel.Minimum = 80;
        Assert.That(wheel.FilledGeometry.Bounds.IsEmpty, Is.True);
    }

    [TestCase(FlowDirection.LeftToRight)]
    [TestCase(FlowDirection.RightToLeft)]
    public void WheelUsesAssignedStyleAndBindsLabelAcrossStates(FlowDirection direction)
    {
        var wheel = new VolumeWheel
        {
            Label = "Application volume with a long localized name", Value = 50,
            FlowDirection = direction,
            Style = (Style)Application.Current.FindResource("OHsVolumeWheelStyle")
        };
        UiTestPump.Layout(wheel, 320, 168);
        UiControlContract.AssertStyle<VolumeWheel>(wheel, "OHsVolumeWheelStyle");
        UiControlContract.AssertTemplate(wheel, "OHsVolumeWheelTemplate");
        var label = (TextBlock)wheel.Template.FindName("WheelLabel", wheel);
        Assert.That(label.Text, Is.EqualTo(wheel.Label));
        Assert.That(label.FlowDirection, Is.EqualTo(direction));
        wheel.IsActive = true;
        wheel.IsEnabled = false;
        Assert.That(wheel.IsActive, Is.True);
        Assert.That(wheel.IsEnabled, Is.False);
        UiControlContract.AssertStyle<VolumeWheel>(wheel, "OHsVolumeWheelStyle");
    }

    [Test]
    public void IdleDismissalStartsAt500Milliseconds()
    {
        var (page, _, view) = CreateLoadedPage();
        using (view)
        {
            page.ShowVolumeOverlay(false, new Point());
            page.ProcessVolumeOverlayIdle(TimeSpan.FromMilliseconds(499), false);
            Assert.That(page.IsVolumeOverlayOpen, Is.True);
            page.ApplicationVolumeWheel.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });
            page.ActivateVolumeWheel(page.SongVolumeWheel);
            Assert.That(page.ActiveVolumeWheel, Is.SameAs(page.SongVolumeWheel));
            page.ProcessVolumeOverlayIdle(TimeSpan.FromMilliseconds(500), false);
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
            page.ProcessVolumeOverlayIdle(TimeSpan.FromMilliseconds(500), true);
            var overlay = (Border)page.FindName("VolumeOverlay");
            Assert.That(overlay.HasAnimatedProperties, Is.True);
            page.RecordVolumeActivity();
            Assert.That(overlay.HasAnimatedProperties, Is.EqualTo(SystemParameters.ClientAreaAnimation));
            UiTestPump.Run(Task.Delay(250));
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
            page.ProcessVolumeOverlayIdle(TimeSpan.FromMilliseconds(500), true);
            var overlay = (Border)page.FindName("VolumeOverlay");
            overlay.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.PreviewMouseMoveEvent });
            Assert.That(overlay.HasAnimatedProperties, Is.EqualTo(SystemParameters.ClientAreaAnimation));
            UiTestPump.Run(Task.Delay(250));
        }
    }

    [Test]
    public void PointerPressMoveAndReleaseNeverChangesVolumeOrCapturesMouse()
    {
        var wheel = new VolumeWheel { Value = 50, IsActive = true,
            Style = (Style)Application.Current.FindResource("OHsVolumeWheelStyle") };
        var host = new Grid();
        host.Children.Add(wheel);
        UiTestPump.Layout(host, 320, 168);
        wheel.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });
        wheel.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 1) { RoutedEvent = Mouse.MouseMoveEvent });
        wheel.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 2, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent });
        Assert.Multiple(() =>
        {
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
    public void MasterVolumesExposeIndependentNamedControls()
    {
        var (page, _, view) = CreateLoadedPage();
        using (view)
        {
            page.ShowVolumeOverlay(false, new Point(0, 0));
            view.UpdateLayout();
            var panel = (Panel)page.FindName("VolumeWheelPanel");
            Assert.That(panel.Children.OfType<VolumeWheel>().Select(w => w.Label), Is.EquivalentTo(new[] { "Song", "Application", "Samples" }));
            foreach (var wheel in panel.Children.OfType<VolumeWheel>())
                UiControlContract.AssertStyle<VolumeWheel>(wheel, "OHsVolumeWheelStyle");
        }
    }
    [SetUp]
    public void SetUp()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Clear();
        UiTestPump.EnsureFontResources(app);
        foreach (var resource in new[] { "Themes/Dark.xaml", "Converters/Converters.xaml", "Icons.xaml", "Styles/TextBlock.xaml",
                     "Styles/ItemsControl.xaml", "Styles/StackPanel.xaml", "Styles/Grid.xaml", "Styles/Border.xaml", "Styles/ContentPresenter.xaml",
                     "Styles/Button.xaml", "Styles/ToolTip.xaml", "Styles/ScrollViewer.xaml", "Styles/TextBox.xaml",
                     "Styles/ComboBox.xaml", "Styles/ListBox.xaml", "Styles/Slider.xaml", "BeatGrid.xaml" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"/osuHitsounder;component/Resources/{resource}", UriKind.Relative)
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
            var card = (Border)page.FindName("SongAudioCard");
            page.HandleContextWheel(ModifierKeys.Alt, 60, card);
            page.HandleContextWheel(ModifierKeys.Alt, 120, page);
            page.HandleContextWheel(ModifierKeys.Alt, 60, card);
            Assert.That(vm.SelectedSubdivisionIndex, Is.EqualTo(3));

            page.HandleContextWheel(ModifierKeys.Alt, -60, page);
            page.HandleContextWheel(ModifierKeys.Alt, 60, card);
            page.HandleContextWheel(ModifierKeys.Alt, -60, page);
            Assert.That(vm.ApplicationVolume, Is.EqualTo(100));
        }
    }

    [Test]
    public void SongCardKeepsSubdivisionPriorityAndKeyboardOverlayRestoresFocusIntent()
    {
        var (page, vm, view) = CreateLoadedPage();
        using (view)
        {
            var card = (Border)page.FindName("SongAudioCard");
            Assert.That(page.HandleContextWheel(ModifierKeys.Alt, 120, card), Is.True);
            Assert.That(vm.SelectedSubdivisionIndex, Is.EqualTo(4));
            Assert.That(page.IsVolumeOverlayOpen, Is.False);

            Assert.That(page.HandleVolumeShortcut(Key.V, ModifierKeys.Alt, page), Is.True);
            Assert.That(page.IsVolumeOverlayOpen, Is.True);
            Assert.That(page.HandleVolumeOverlayKey(Key.Tab, ModifierKeys.None), Is.True);
            Assert.That(page.ActiveVolumeWheel, Is.SameAs(page.SamplesVolumeWheel));
            Assert.That(page.HandleVolumeOverlayKey(Key.Down, ModifierKeys.None), Is.True);
            Assert.That(vm.SamplesVolume, Is.EqualTo(95));
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
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new osuHitsounder.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
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

    private static (WorkstationPage Page, WorkstationPageVM ViewModel, InMemoryView View) CreateLoadedPage(bool physicalCoordinates = false)
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new osuHitsounder.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
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
