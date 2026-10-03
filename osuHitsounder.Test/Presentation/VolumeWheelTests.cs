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
    [TestCase(0d, false, false)]
    [TestCase(50d, true, false)]
    [TestCase(100d, true, true)]
    public void ProgressGeometryIsAnnular(double value, bool rightFilled, bool leftFilled)
    {
        var wheel = new VolumeWheel { Value = value };
        Assert.Multiple(() =>
        {
            Assert.That(wheel.FilledGeometry.FillContains(new Point(135, 80)), Is.EqualTo(rightFilled));
            Assert.That(wheel.FilledGeometry.FillContains(new Point(25, 80)), Is.EqualTo(leftFilled));
            Assert.That(wheel.FilledGeometry.FillContains(new Point(80, 80)), Is.False);
            Assert.That(wheel.FilledGeometry.FillContains(new Point(129, 80)), Is.False);
            Assert.That(wheel.FilledGeometry.FillContains(new Point(141, 80)), Is.False);
        });
        if (value == 0) Assert.That(wheel.FilledGeometry.Bounds.IsEmpty, Is.True);
    }

    [Test]
    public void ProgressNormalizesCustomRangeAndHandlesDegenerateRange()
    {
        var wheel = new VolumeWheel { Minimum = 20, Maximum = 80, Value = 50 };
        Assert.That(wheel.FilledGeometry.FillContains(new Point(135, 80)), Is.True);
        Assert.That(wheel.FilledGeometry.FillContains(new Point(25, 80)), Is.False);
        wheel.Minimum = 80;
        Assert.That(wheel.FilledGeometry.Bounds.IsEmpty, Is.True);
    }

    [TestCase(FlowDirection.LeftToRight)]
    [TestCase(FlowDirection.RightToLeft)]
    public void RingTemplateKeepsDimensionsFontsAndRightLabel(FlowDirection direction)
    {
        var wheel = new VolumeWheel
        {
            Label = "Application volume with a long localized name", Value = 50,
            FlowDirection = direction,
            Style = (Style)Application.Current.FindResource("OHsVolumeWheelStyle")
        };
        var host = new Grid { FlowDirection = FlowDirection.LeftToRight };
        host.Children.Add(wheel);
        UiTestPump.Layout(host, 320, 168);
        var graphic = (Grid)wheel.Template.FindName("PieGraphic", wheel);
        Assert.That(wheel.Template.FindName("WheelBackground", wheel), Is.TypeOf<System.Windows.Shapes.Path>());
        var ramp = (System.Windows.Shapes.Path)wheel.Template.FindName("WheelBackground", wheel);
        var value = (TextBlock)wheel.Template.FindName("WheelValue", wheel);
        var label = (TextBlock)wheel.Template.FindName("WheelLabel", wheel);
        var graphicOrigin = graphic.TranslatePoint(new Point(), host);
        var labelOrigin = label.TranslatePoint(new Point(), host);
        var labelOtherEdge = label.TranslatePoint(new Point(label.ActualWidth, 0), host);
        var valueCenter = value.TranslatePoint(new Point(value.ActualWidth / 2, value.ActualHeight / 2), host);
        Assert.Multiple(() =>
        {
            Assert.That(graphic.ActualWidth, Is.EqualTo(160));
            Assert.That(graphic.ActualHeight, Is.EqualTo(160));
            Assert.That(ramp.Data.Bounds, Is.EqualTo(new Rect(0, 0, 160, 160)));
            Assert.That(ramp.Data.FillContains(new Point(111, 80)), Is.True);
            Assert.That(ramp.Data.FillContains(new Point(109, 80)), Is.False);
            Assert.That(ramp.Data.FillContains(new Point(159, 80)), Is.True);
            Assert.That(ramp.Data.FillContains(new Point(161, 80)), Is.False);
            Assert.That(Math.Min(labelOrigin.X, labelOtherEdge.X), Is.GreaterThanOrEqualTo(graphicOrigin.X + 160));
            Assert.That(valueCenter.X, Is.EqualTo(graphicOrigin.X + 80).Within(0.01));
            Assert.That(valueCenter.Y, Is.EqualTo(graphicOrigin.Y + 80).Within(0.01));
            Assert.That(value.FontSize, Is.EqualTo(13));
            Assert.That(label.FontSize, Is.EqualTo(13));
            Assert.That(label.TextWrapping, Is.EqualTo(TextWrapping.Wrap));
            Assert.That(label.FlowDirection, Is.EqualTo(direction));
        });
        var size = wheel.DesiredSize;
        wheel.IsActive = true;
        UiTestPump.Layout(host, 320, 168);
        Assert.That(wheel.DesiredSize, Is.EqualTo(size));
        wheel.IsEnabled = false;
        Assert.That(wheel.Cursor, Is.EqualTo(Cursors.Arrow));
        Assert.That(wheel.Opacity, Is.EqualTo(0.4));
    }

    [Test]
    public void IdleDismissalWaitsOneSecondAfterPointerActivity()
    {
        var (page, _, view) = CreateLoadedPage();
        using (view)
        {
            page.ShowVolumeOverlay(false, new Point());
            page.ProcessVolumeOverlayIdle(TimeSpan.FromMilliseconds(999), false);
            Assert.That(page.IsVolumeOverlayOpen, Is.True);
            page.ApplicationVolumeWheel.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });
            page.ActivateVolumeWheel(page.SongVolumeWheel);
            Assert.That(page.ActiveVolumeWheel, Is.SameAs(page.SongVolumeWheel));
            page.ProcessVolumeOverlayIdle(TimeSpan.FromSeconds(1), false);
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
            page.ProcessVolumeOverlayIdle(TimeSpan.FromSeconds(1), true);
            var overlay = (Border)page.FindName("VolumeOverlay");
            Assert.That(overlay.HasAnimatedProperties, Is.True);
            page.RecordVolumeActivity();
            Assert.That(overlay.HasAnimatedProperties, Is.False);
            Assert.That(overlay.Opacity, Is.EqualTo(1));
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
            page.ProcessVolumeOverlayIdle(TimeSpan.FromSeconds(1), true);
            var overlay = (Border)page.FindName("VolumeOverlay");
            overlay.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.PreviewMouseMoveEvent });
            Assert.That(overlay.HasAnimatedProperties, Is.False);
            Assert.That(overlay.Opacity, Is.EqualTo(1));
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
    public void OverlayIgnoresInvocationAnchorAndStaysAtLeftAfterResize()
    {
        var (page, _, view) = CreateLoadedPage(physicalCoordinates: true);
        using (view)
        {
            var overlay = (Border)page.FindName("VolumeOverlay");
            var root = (Grid)page.FindName("WorkstationRoot");
            foreach (var direction in new[] { FlowDirection.LeftToRight, FlowDirection.RightToLeft })
            {
                page.FlowDirection = direction;
                foreach (var anchor in new[] { new Point(500, 400), new Point(30, 50) })
                {
                    page.ShowVolumeOverlay(false, anchor);
                    view.UpdateLayout();
                    Assert.Multiple(() =>
                    {
                        Assert.That(overlay.Margin, Is.EqualTo(new Thickness(0)));
                        Assert.That(overlay.VerticalAlignment, Is.EqualTo(VerticalAlignment.Center));
                        Assert.That(((SolidColorBrush)overlay.Background).Color.A, Is.Zero);
                        Assert.That(overlay.BorderThickness, Is.EqualTo(new Thickness(0)));
                        var center = overlay.TranslatePoint(new Point(overlay.ActualWidth / 2, overlay.ActualHeight / 2), root);
                        Assert.That(center.Y, Is.EqualTo(root.ActualHeight / 2).Within(0.01));
                    });
                    var physicalHost = view.Root;
                    var overlayLeft = overlay.TranslatePoint(new Point(), physicalHost).X;
                    var overlayRight = overlay.TranslatePoint(new Point(overlay.ActualWidth, 0), physicalHost).X;
                    var rootLeft = root.TranslatePoint(new Point(), physicalHost).X;
                    var rootRight = root.TranslatePoint(new Point(root.ActualWidth, 0), physicalHost).X;
                    Assert.That(Math.Min(overlayLeft, overlayRight), Is.EqualTo(Math.Min(rootLeft, rootRight)).Within(0.01));
                    page.DismissVolumeOverlay();
                }
                page.HandleVolumeShortcut(Key.V, ModifierKeys.Alt, page);
                view.UpdateLayout();
                Assert.That(overlay.Margin, Is.EqualTo(new Thickness(0)));
                page.DismissVolumeOverlay();
            }
            page.ShowVolumeOverlay(false, new Point(500, 400));
            page.Width = 180;
            page.Height = 250;
            view.UpdateLayout();
            Assert.That(overlay.Margin, Is.EqualTo(new Thickness(0)));
        }
    }

    [Test]
    public void OpenOverlayStaysInsideHostAfterResize()
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
            var root = (Grid)page.FindName("WorkstationRoot");
            Assert.Multiple(() =>
            {
                Assert.That(overlay.Margin.Left + overlay.ActualWidth, Is.LessThanOrEqualTo(root.ActualWidth + 0.01));
                Assert.That(overlay.Margin.Top + overlay.ActualHeight, Is.LessThanOrEqualTo(root.ActualHeight + 0.01));
            });
            var scroll = (ScrollViewer)overlay.Child;
            Assert.That(scroll.ScrollableWidth, Is.GreaterThan(0));
            Assert.That(scroll.ScrollableHeight, Is.GreaterThan(0));
            scroll.ScrollToRightEnd();
            scroll.ScrollToBottom();
            view.UpdateLayout();
            Assert.That(scroll.HorizontalOffset, Is.EqualTo(scroll.ScrollableWidth));
            Assert.That(scroll.VerticalOffset, Is.EqualTo(scroll.ScrollableHeight));
            var graphic = (Grid)page.SamplesVolumeWheel.Template.FindName("PieGraphic", page.SamplesVolumeWheel);
            Assert.That(graphic.ActualWidth, Is.EqualTo(160));
            Assert.That(graphic.ActualHeight, Is.EqualTo(160));
        }
    }

    [Test]
    public void MasterVolumesHaveSongApplicationSamplesOrder()
    {
        var (page, _, view) = CreateLoadedPage();
        using (view)
        {
            page.ShowVolumeOverlay(false, new Point(0, 0));
            view.UpdateLayout();
            var panel = (Panel)page.FindName("VolumeWheelPanel");
            Assert.That(panel.Children.OfType<VolumeWheel>().Select(w => w.Label), Is.EqualTo(new[] { "Song", "Application", "Samples" }));
            var centers = panel.Children.OfType<VolumeWheel>()
                .Select(w => w.TranslatePoint(new Point(w.ActualWidth / 2, w.ActualHeight / 2), panel)).ToArray();
            Assert.That(centers.Select(p => p.X).Distinct().Count(), Is.EqualTo(1));
            Assert.That(centers.Select(p => p.Y), Is.Ordered);
            Assert.That(page.FindName("SongVolumeSlider"), Is.Null);
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
    public void WheelExposesAccessibleRangeAndActiveStateWithoutDimmingItsText()
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
            Assert.That(wheel.PieOpacity, Is.EqualTo(0.75));
            Assert.That(wheel.FilledGeometry, Is.Not.Null);
            Assert.That(AutomationProperties.GetName(wheel), Is.EqualTo("Application volume"));
        });

        wheel.IsActive = true;
        Assert.That(wheel.PieOpacity, Is.EqualTo(1));
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
    public void OverlayStaysReachableAtNarrowWidthAndInheritsRightToLeftFlow()
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new osuHitsounder.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm) { FlowDirection = FlowDirection.RightToLeft };
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 180, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);
        page.ShowVolumeOverlay(false, new Point(500, 500));
        view.UpdateLayout();
        var overlay = (Border)page.FindName("VolumeOverlay");
        var root = (Grid)page.FindName("WorkstationRoot");

        Assert.Multiple(() =>
        {
            Assert.That(overlay.ActualWidth, Is.LessThanOrEqualTo(root.ActualWidth));
            Assert.That(overlay.Margin.Left + overlay.ActualWidth, Is.LessThanOrEqualTo(root.ActualWidth + 0.01));
            Assert.That(overlay.Margin.Top + overlay.ActualHeight, Is.LessThanOrEqualTo(root.ActualHeight + 0.01));
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
