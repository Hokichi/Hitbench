using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Diagnostics;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Controls;
using Hitbench.Resources.CustomControls;

namespace Hitbench.Views.Pages;

public partial class WorkstationPage
{
    private static readonly DependencyPropertyKey HeaderBlurEffectPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(HeaderBlurEffect), typeof(Effect), typeof(WorkstationPage), new PropertyMetadata(null));
    public static readonly DependencyProperty HeaderBlurEffectProperty = HeaderBlurEffectPropertyKey.DependencyProperty;
    public Effect? HeaderBlurEffect => (Effect?)GetValue(HeaderBlurEffectProperty);

    private int volumeWheelRemainder;
    private IInputElement? volumePriorFocus;
    private DispatcherTimer? volumeIdleTimer;
    private long volumeLastActivity;
    private int volumeOverlayVersion;
    private bool volumeFading;
    private bool volumeAnimate;
    private double volumeBlurRadius;
    private BlurEffect? volumeBlur;

    private static readonly DependencyPropertyKey IsVolumeOverlayOpenPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsVolumeOverlayOpen), typeof(bool), typeof(WorkstationPage), new PropertyMetadata(false));
    public static readonly DependencyProperty IsVolumeOverlayOpenProperty = IsVolumeOverlayOpenPropertyKey.DependencyProperty;
    public bool IsVolumeOverlayOpen => (bool)GetValue(IsVolumeOverlayOpenProperty);
    private static readonly DependencyPropertyKey VolumeBlurEffectPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(VolumeBlurEffect), typeof(Effect), typeof(WorkstationPage), new PropertyMetadata(null));
    public static readonly DependencyProperty VolumeBlurEffectProperty = VolumeBlurEffectPropertyKey.DependencyProperty;
    public Effect? VolumeBlurEffect => (Effect?)GetValue(VolumeBlurEffectProperty);
    private Panel? volumeOverlayHost;

    public void AttachVolumeOverlayHost(Panel host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (ReferenceEquals(volumeOverlayHost, host)) return;
        DetachVolumeOverlayHost();
        WorkstationRoot.Children.Remove(VolumeOverlay);
        volumeOverlayHost = host;
        host.Children.Add(VolumeOverlay);
        host.SizeChanged += OnVolumeOverlayHostSizeChanged;
        RefreshBlurPresentation();
        PositionVolumeOverlay();
    }

    public void DetachVolumeOverlayHost()
    {
        if (volumeOverlayHost is null) return;
        DismissVolumeOverlay();
        volumeOverlayHost.SizeChanged -= OnVolumeOverlayHostSizeChanged;
        volumeOverlayHost.Children.Remove(VolumeOverlay);
        volumeOverlayHost = null;
        WorkstationRoot.Children.Add(VolumeOverlay);
        RefreshBlurPresentation();
    }

    internal VolumeWheel? ActiveVolumeWheel { get; private set; }

    internal void ShowVolumeOverlay(bool keyboardInvoked, Point anchor) =>
        ShowVolumeOverlay(keyboardInvoked, anchor, SystemParameters.ClientAreaAnimation, SystemParameters.HighContrast);

    internal void ShowVolumeOverlay(bool keyboardInvoked, Point anchor, bool animate, bool highContrast)
    {
        if (deactivationStarted) return;
        CancelNodePointer();
        var wasOpen = IsVolumeOverlayOpen;
        var settingsChanged = volumeAnimate != animate || volumeBlurRadius != (highContrast ? 0 : 6);
        volumeAnimate = animate;
        volumeBlurRadius = highContrast ? 0 : 6;
        volumeWheelRemainder = 0;
        if (!wasOpen) volumePriorFocus = Keyboard.FocusedElement;
        VolumeOverlay.Visibility = Visibility.Visible;
        SetValue(IsVolumeOverlayOpenPropertyKey, true);
        TrackListComponent.IsVolumeOverlayOpen = true;
        if (!wasOpen)
        {
            VolumeOverlay.Opacity = 0;
            volumeBlur ??= ((BlurEffect)FindResource("OHsWorkstationVolumeBlurEffect")).Clone();
            volumeBlur.Radius = 0;
            RefreshBlurPresentation();
            AnimateVolumeOverlay(true, animate);
        }
        else
        {
            RefreshBlurPresentation();
            if (settingsChanged && !volumeFading) AnimateVolumeOverlay(true, animate);
        }
        ActivateVolumeWheel(ApplicationVolumeWheel);
        PositionVolumeOverlay();
        ApplicationVolumeWheel.Focus();
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
        SetValue(IsVolumeOverlayOpenPropertyKey, false);
        TrackListComponent.IsVolumeOverlayOpen = false;
        volumeBlur?.BeginAnimation(BlurEffect.RadiusProperty, null);
        if (volumeBlur is not null) volumeBlur.Radius = 0;
        RefreshBlurPresentation();
        var restore = volumePriorFocus as UIElement;
        volumePriorFocus = null;
        volumeWheelRemainder = 0;
        if (ActiveVolumeWheel is not null) ActiveVolumeWheel.IsActive = false;
        ActiveVolumeWheel = null;
        if (!deactivationStarted && restore is { IsVisible: true, IsEnabled: true })
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                if (!deactivationStarted && !IsVolumeOverlayOpen && restore is { IsVisible: true, IsEnabled: true }) restore.Focus();
            });
    }

    internal void ActivateVolumeWheel(VolumeWheel wheel)
    {
        if (ReferenceEquals(ActiveVolumeWheel, wheel)) return;
        if (ActiveVolumeWheel is not null) ActiveVolumeWheel.IsActive = false;
        ActiveVolumeWheel = wheel;
        wheel.IsActive = true;
        volumeWheelRemainder = 0;
    }

    internal bool HandleContextWheel(ModifierKeys modifiers, int delta, DependencyObject? origin)
    {
        if (IsMarkerModeTransitionActive) { Timeline.ResetWheel(); return true; }
        if (IsVolumeOverlayOpen && modifiers == ModifierKeys.None && IsVolumeOverlayElement(origin)) return false;
        if (!IsVolumeOverlayOpen && HandleNodeDragWheel(modifiers, delta)) return true;
        if (modifiers == ModifierKeys.None && !IsHorizontalWheelOrigin(origin)) { Timeline.ResetWheel(); return false; }
        if (modifiers != ModifierKeys.Alt)
        {
            volumeWheelRemainder = 0;
            return HandleWaveformWheel(modifiers, delta);
        }
        Timeline.ResetWheel();

        if (!IsVolumeOverlayOpen)
        {
            ShowVolumeOverlay(false, new Point());
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

    internal bool IsHorizontalWheelOrigin(DependencyObject? origin) => IsWithin(origin, AudioCard) || IsWithin(origin, WaveformScrollBar);

    internal bool HandleVolumeShortcut(Key key, ModifierKeys modifiers, DependencyObject? origin)
    {
        if (IsMarkerModeTransitionActive) return true;
        if (IsVolumeOverlayOpen && HandleVolumeOverlayKey(key, modifiers)) return true;
        if (key != Key.V || modifiers != ModifierKeys.Alt || deactivationStarted) return false;
        ShowVolumeOverlay(true, new Point());
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
            return true;
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

    private void OnVolumeOverlayMouseMove(object sender, MouseEventArgs e) => RecordVolumeActivity();

    private void OnVolumeWheelMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is VolumeWheel wheel) ActivateVolumeWheel(wheel);
        RecordVolumeActivity();
    }

    private void OnVolumeWheelValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => RecordVolumeActivity();

    internal void RecordVolumeActivity()
    {
        if (!IsVolumeOverlayOpen) return;
        volumeLastActivity = Stopwatch.GetTimestamp();
        if (volumeFading) AnimateVolumeOverlay(true, volumeAnimate);
        if (volumeIdleTimer is null)
        {
            volumeIdleTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(50) };
            volumeIdleTimer.Tick += (_, _) => ProcessVolumeOverlayIdle(Stopwatch.GetElapsedTime(volumeLastActivity), SystemParameters.ClientAreaAnimation);
        }
        volumeIdleTimer.Start();
    }

    internal void ProcessVolumeOverlayIdle(TimeSpan idleFor, bool animate)
    {
        if (!IsVolumeOverlayOpen || volumeFading || idleFor < TimeSpan.FromMilliseconds(100)) return;
        volumeIdleTimer?.Stop();
        if (!animate) { DismissVolumeOverlay(); return; }
        AnimateVolumeOverlay(false, true);
    }

    private void AnimateVolumeOverlay(bool opening, bool animate)
    {
        var version = ++volumeOverlayVersion;
        volumeFading = !opening;
        var opacity = VolumeOverlay.Opacity;
        var radius = volumeBlur?.Radius ?? 0;
        VolumeOverlay.BeginAnimation(OpacityProperty, null);
        volumeBlur?.BeginAnimation(BlurEffect.RadiusProperty, null);
        VolumeOverlay.Opacity = animate ? opacity : opening ? 1 : 0;
        if (volumeBlur is not null) volumeBlur.Radius = animate ? radius : opening ? volumeBlurRadius : 0;
        if (!animate)
        {
            if (!opening) DismissVolumeOverlay();
            return;
        }
        var duration = TimeSpan.FromMilliseconds(200);
        var easing = new QuadraticEase { EasingMode = opening ? EasingMode.EaseOut : EasingMode.EaseIn };
        var animation = new DoubleAnimation(opacity, opening ? 1 : 0, duration) { EasingFunction = easing };
        animation.Completed += (_, _) =>
        {
            if (version != volumeOverlayVersion || !IsVolumeOverlayOpen) return;
            if (!opening) DismissVolumeOverlay();
            else
            {
                VolumeOverlay.Opacity = 1;
                if (volumeBlur is not null) volumeBlur.Radius = volumeBlurRadius;
                VolumeOverlay.BeginAnimation(OpacityProperty, null);
                volumeBlur?.BeginAnimation(BlurEffect.RadiusProperty, null);
            }
        };
        VolumeOverlay.BeginAnimation(OpacityProperty, animation);
        volumeBlur?.BeginAnimation(BlurEffect.RadiusProperty,
            new DoubleAnimation(radius, opening ? volumeBlurRadius : 0, duration) { EasingFunction = easing });
    }

    private void OnVolumeOverlayHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (IsVolumeOverlayOpen) PositionVolumeOverlay();
    }

    private void PositionVolumeOverlay()
    {
        var host = volumeOverlayHost ?? WorkstationRoot;
        var availableWidth = Math.Max(0, host.ActualWidth - 16);
        VolumeOverlay.Width = Math.Min(320, availableWidth);
        VolumeOverlay.MaxHeight = Math.Max(0, host.ActualHeight - 16);
        VolumeOverlay.Margin = new Thickness(0);
    }

    private bool IsSongAudioCardElement(DependencyObject? origin) => AudioCard.ContainsInputOrigin(origin);
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
