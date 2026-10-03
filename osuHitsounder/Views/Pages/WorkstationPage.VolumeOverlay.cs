using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Diagnostics;
using System.Windows.Media.Animation;
using osuHitsounder.Resources.CustomControls;

namespace osuHitsounder.Views.Pages;

public partial class WorkstationPage
{
    private bool keyboardVolumeOverlay;
    private int volumeWheelRemainder;
    private IInputElement? volumePriorFocus;
    private DispatcherTimer? volumeIdleTimer;
    private long volumeLastActivity;
    private int volumeOverlayVersion;
    private bool volumeFading;

    internal bool IsVolumeOverlayOpen => VolumeOverlay.Visibility == Visibility.Visible;
    internal VolumeWheel? ActiveVolumeWheel { get; private set; }

    internal void ShowVolumeOverlay(bool keyboardInvoked, Point anchor)
    {
        keyboardVolumeOverlay = keyboardInvoked;
        volumeWheelRemainder = 0;
        if (keyboardInvoked && !IsVolumeOverlayOpen) volumePriorFocus = Keyboard.FocusedElement;
        VolumeOverlay.Visibility = Visibility.Visible;
        ActivateVolumeWheel(ApplicationVolumeWheel);
        PositionVolumeOverlay(anchor);
        if (keyboardInvoked) ApplicationVolumeWheel.Focus();
        RecordVolumeActivity();
    }

    internal void DismissVolumeOverlay()
    {
        if (!IsVolumeOverlayOpen) return;
        volumeIdleTimer?.Stop();
        volumeOverlayVersion++;
        volumeFading = false;
        VolumeOverlay.BeginAnimation(OpacityProperty, null);
        VolumeOverlay.Opacity = 1;
        VolumeOverlay.Visibility = Visibility.Collapsed;
        SongVolumeWheel.EndDrag();
        ApplicationVolumeWheel.EndDrag();
        SamplesVolumeWheel.EndDrag();
        var restore = keyboardVolumeOverlay ? volumePriorFocus as UIElement : null;
        VolumeOverlay.Visibility = Visibility.Collapsed;
        keyboardVolumeOverlay = false;
        volumePriorFocus = null;
        volumeWheelRemainder = 0;
        if (ActiveVolumeWheel is not null) ActiveVolumeWheel.IsActive = false;
        ActiveVolumeWheel = null;
        if (restore is { IsVisible: true, IsEnabled: true })
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => restore.Focus());
    }

    internal void ActivateVolumeWheel(VolumeWheel wheel)
    {
        if (ActiveVolumeWheel?.IsDragging == true) return;
        if (ReferenceEquals(ActiveVolumeWheel, wheel)) return;
        if (ActiveVolumeWheel is not null) ActiveVolumeWheel.IsActive = false;
        ActiveVolumeWheel = wheel;
        wheel.IsActive = true;
        volumeWheelRemainder = 0;
    }

    internal bool HandleContextWheel(ModifierKeys modifiers, int delta, DependencyObject? origin)
    {
        if (modifiers != ModifierKeys.Alt)
        {
            volumeWheelRemainder = 0;
            return HandleWaveformWheel(modifiers, delta);
        }
        if (!IsVolumeOverlayElement(origin) && IsSongAudioCardElement(origin))
        {
            volumeWheelRemainder = 0;
            return HandleWaveformWheel(modifiers, delta);
        }

        wheelMode = ModifierKeys.None;
        wheelRemainder = 0;

        if (!IsVolumeOverlayOpen)
        {
            ShowVolumeOverlay(false, Mouse.GetPosition(this));
            return true;
        }

        RecordVolumeActivity();

        var hovered = FindAncestor<VolumeWheel>(origin);
        if (hovered is not null) ActivateVolumeWheel(hovered);
        volumeWheelRemainder += delta;
        var steps = volumeWheelRemainder / Mouse.MouseWheelDeltaForOneLine;
        volumeWheelRemainder %= Mouse.MouseWheelDeltaForOneLine;
        if (steps == 0 || ActiveVolumeWheel is not { IsEnabled: true } active) return true;
        active.SetCurrentValue(System.Windows.Controls.Primitives.RangeBase.ValueProperty,
            Math.Clamp(active.Value + Math.Sign(steps) * Math.Abs(steps) * 5, active.Minimum, active.Maximum));
        return true;
    }

    internal bool HandleVolumeShortcut(Key key, ModifierKeys modifiers, DependencyObject? origin)
    {
        if (IsVolumeOverlayOpen && HandleVolumeOverlayKey(key, modifiers)) return true;
        if (key != Key.V || modifiers != ModifierKeys.Alt || deactivationStarted) return false;
        ShowVolumeOverlay(true, new Point(Math.Max(0, ActualWidth / 2 - 64), 16));
        return true;
    }

    internal bool HandleVolumeOverlayKey(Key key, ModifierKeys modifiers)
    {
        if (!IsVolumeOverlayOpen) return false;
        RecordVolumeActivity();
        if (key == Key.Escape)
        {
            DismissVolumeOverlay();
            return true;
        }
        if (key == Key.Tab && modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            var wheels = new[] { SongVolumeWheel, ApplicationVolumeWheel, SamplesVolumeWheel };
            var current = Array.IndexOf(wheels, ActiveVolumeWheel);
            var next = modifiers == ModifierKeys.Shift ? (current + wheels.Length - 1) % wheels.Length : (current + 1) % wheels.Length;
            ActivateVolumeWheel(wheels[next]);
            wheels[next].Focus();
            wheels[next].BringIntoView();
            return true;
        }
        if (key is not (Key.Up or Key.Right or Key.Down or Key.Left) || modifiers != ModifierKeys.None ||
            ActiveVolumeWheel is not { IsEnabled: true } active) return false;
        var direction = key is Key.Up or Key.Right ? 1 : -1;
        active.SetCurrentValue(System.Windows.Controls.Primitives.RangeBase.ValueProperty,
            Math.Clamp(active.Value + direction * 5, active.Minimum, active.Maximum));
        return true;
    }

    private void OnVolumeWheelMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is VolumeWheel wheel) ActivateVolumeWheel(wheel);
        RecordVolumeActivity();
    }

    private void OnVolumeWheelMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is VolumeWheel wheel) ActivateVolumeWheel(wheel);
        RecordVolumeActivity();
    }

    private void OnVolumeWheelDragStateChanged(object? sender, EventArgs e) => RecordVolumeActivity();
    private void OnVolumeWheelValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => RecordVolumeActivity();

    internal void RecordVolumeActivity()
    {
        if (!IsVolumeOverlayOpen) return;
        volumeLastActivity = Stopwatch.GetTimestamp();
        volumeOverlayVersion++;
        volumeFading = false;
        VolumeOverlay.BeginAnimation(OpacityProperty, null);
        VolumeOverlay.Opacity = 1;
        if (volumeIdleTimer is null)
        {
            volumeIdleTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(50) };
            volumeIdleTimer.Tick += (_, _) => ProcessVolumeOverlayIdle(Stopwatch.GetElapsedTime(volumeLastActivity), SystemParameters.ClientAreaAnimation);
        }
        volumeIdleTimer.Start();
    }

    internal void ProcessVolumeOverlayIdle(TimeSpan idleFor, bool animate)
    {
        if (!IsVolumeOverlayOpen || volumeFading || idleFor < TimeSpan.FromSeconds(1) || ActiveVolumeWheel?.IsDragging == true) return;
        volumeIdleTimer?.Stop();
        if (!animate) { DismissVolumeOverlay(); return; }
        volumeFading = true;
        var version = volumeOverlayVersion;
        var animation = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        animation.Completed += (_, _) => { if (version == volumeOverlayVersion) DismissVolumeOverlay(); };
        VolumeOverlay.BeginAnimation(OpacityProperty, animation);
    }

    private void PositionVolumeOverlay(Point anchor)
    {
        var availableWidth = Math.Max(0, WorkstationRoot.ActualWidth - 16);
        VolumeOverlay.Width = Math.Min(128, availableWidth);
        VolumeOverlay.MaxHeight = Math.Max(0, WorkstationRoot.ActualHeight - 16);
        VolumeOverlay.Measure(new Size(VolumeOverlay.Width, double.PositiveInfinity));
        var left = Math.Clamp(anchor.X, 0, Math.Max(0, WorkstationRoot.ActualWidth - VolumeOverlay.Width));
        var top = Math.Clamp(anchor.Y, 0, Math.Max(0, WorkstationRoot.ActualHeight - VolumeOverlay.DesiredSize.Height));
        VolumeOverlay.Margin = new Thickness(left, top, 0, 0);
    }

    private bool IsSongAudioCardElement(DependencyObject? origin) => IsWithin(origin, SongAudioCard);
    private bool IsVolumeOverlayElement(DependencyObject? origin) => IsWithin(origin, VolumeOverlay);

    private static bool IsWithin(DependencyObject? origin, DependencyObject owner)
    {
        for (var current = origin; current is not null; current = GetInputParent(current))
            if (ReferenceEquals(current, owner)) return true;
        return false;
    }

    private static T? FindAncestor<T>(DependencyObject? origin) where T : DependencyObject
    {
        for (var current = origin; current is not null; current = GetInputParent(current))
            if (current is T match) return match;
        return null;
    }
}
