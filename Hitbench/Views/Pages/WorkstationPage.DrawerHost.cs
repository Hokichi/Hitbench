using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Hitbench.Resources.DataModels;

namespace Hitbench.Views.Pages;

public partial class WorkstationPage
{
    private static readonly DependencyProperty SideDrawerProgressProperty = DependencyProperty.Register(
        "SideDrawerProgress", typeof(double), typeof(WorkstationPage), new PropertyMetadata(0d,
            (owner, _) => ((WorkstationPage)owner).UpdateDrawerReveal()));
    private static readonly DependencyProperty BottomDrawerProgressProperty = DependencyProperty.Register(
        "BottomDrawerProgress", typeof(double), typeof(WorkstationPage), new PropertyMetadata(0d,
            (owner, _) => ((WorkstationPage)owner).UpdateDrawerReveal()));
    private static readonly DependencyPropertyKey IsPianoRollExpandedPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsPianoRollExpanded), typeof(bool), typeof(WorkstationPage), new PropertyMetadata(false));
    public static readonly DependencyProperty IsPianoRollExpandedProperty = IsPianoRollExpandedPropertyKey.DependencyProperty;
    public bool IsPianoRollExpanded => (bool)GetValue(IsPianoRollExpandedProperty);
    private double savedTrackOffset;
    private bool hasSavedTrackOffset;
    private int drawerAttachmentGeneration;
    private static readonly DependencyProperty PianoRollExpansionProgressProperty = DependencyProperty.Register(
        nameof(PianoRollExpansionProgress), typeof(double), typeof(WorkstationPage),
        new PropertyMetadata(0d, (owner, _) => ((WorkstationPage)owner).UpdatePianoExpansionPresentation()));
    internal double PianoRollExpansionProgress => (double)GetValue(PianoRollExpansionProgressProperty);
    private AnimationClock? pianoExpansionClock;
    internal void TogglePianoRollExpansion() => TogglePianoRollExpansion(SystemParameters.ClientAreaAnimation);
    internal void TogglePianoRollExpansion(bool animationsEnabled)
    {
        if (bottomDrawerHost is null || bottomDrawerKind != OHsWorkstationDrawerKind.Arrangement || deactivationStarted) return;
        var expanded = !IsPianoRollExpanded;
        var progress = PianoRollExpansionProgress;
        if (expanded && !hasSavedTrackOffset)
        {
            savedTrackOffset = TrackListComponent.CaptureVerticalOffset();
            hasSavedTrackOffset = true;
            TrackListComponent.CancelNodePointer();
        }
        ClearPianoExpansionClock();
        SetValue(IsPianoRollExpandedPropertyKey, expanded);
        TracksCard.Visibility = Visibility.Visible;
        if (!animationsEnabled) { SettlePianoRollExpansion(); return; }
        var animation = new DoubleAnimation(progress, expanded ? 1 : 0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = expanded ? EasingMode.EaseOut : EasingMode.EaseIn }
        };
        pianoExpansionClock = (AnimationClock)animation.CreateClock(true);
        pianoExpansionClock.Completed += OnPianoExpansionCompleted;
        ApplyAnimationClock(PianoRollExpansionProgressProperty, pianoExpansionClock);
        pianoExpansionClock.Controller!.Begin();
    }
    internal void SeekPianoRollExpansion(TimeSpan position) =>
        pianoExpansionClock?.Controller?.SeekAlignedToLastTick(position, TimeSeekOrigin.BeginTime);
    private void OnPianoExpansionCompleted(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, pianoExpansionClock)) SettlePianoRollExpansion();
    }
    private void ClearPianoExpansionClock()
    {
        var progress = PianoRollExpansionProgress;
        if (pianoExpansionClock is { } clock)
        {
            pianoExpansionClock = null;
            clock.Completed -= OnPianoExpansionCompleted;
            ApplyAnimationClock(PianoRollExpansionProgressProperty, null);
            clock.Controller?.Remove();
        }
        SetValue(PianoRollExpansionProgressProperty, progress);
    }
    internal void SettlePianoRollExpansion()
    {
        ClearPianoExpansionClock();
        SetValue(PianoRollExpansionProgressProperty, IsPianoRollExpanded ? 1d : 0d);
        UpdatePianoExpansionPresentation();
        if (!IsPianoRollExpanded && hasSavedTrackOffset)
        {
            var generation = drawerAttachmentGeneration;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
            {
                if (!deactivationStarted && !IsPianoRollExpanded && hasSavedTrackOffset && drawerAttachmentGeneration == generation)
                {
                    TrackListComponent.RestoreVerticalOffset(savedTrackOffset);
                    hasSavedTrackOffset = false;
                }
            });
        }
    }
    private void ResetPianoRollExpansion()
    {
        ClearPianoExpansionClock();
        SetValue(IsPianoRollExpandedPropertyKey, false);
        SetValue(PianoRollExpansionProgressProperty, 0d);
        UpdatePianoExpansionPresentation();
    }
    private void UpdatePianoExpansionPresentation()
    {
        var progress = Math.Clamp(PianoRollExpansionProgress, 0, 1);
        TracksCard.Visibility = IsPianoRollExpanded && progress >= 1 ? Visibility.Collapsed : Visibility.Visible;
        BottomDrawerSlot.Margin = new Thickness(0, 12 * (1 - progress), 0, 0);
        UpdateDrawerHostSize();
    }
    private Grid? sideDrawerHost;
    private Grid? bottomDrawerHost;
    private OHsWorkstationDrawerKind bottomDrawerKind;
    private double sideDrawerTargetExtent;
    private double bottomDrawerTargetExtent;

    internal void AttachDrawerHost(Grid host, OHsWorkstationDrawerKind kind)
    {
        var side = kind == OHsWorkstationDrawerKind.Samples;
        if (deactivationStarted || (side ? sideDrawerHost : bottomDrawerHost) is not null)
            throw new InvalidOperationException("The drawer slot is unavailable.");
        if (side) sideDrawerHost = host;
        else { drawerAttachmentGeneration++; bottomDrawerKind = kind; bottomDrawerHost = host; }
        var slot = side ? RightDrawerSlot : BottomDrawerSlot;
        slot.Children.Add(host);
        host.VerticalAlignment = VerticalAlignment.Top;
        slot.Visibility = Visibility.Visible;
        host.LayoutUpdated += OnDrawerLayoutUpdated;
        SettleDrawerHostAnimation(host, false);
        UpdateDrawerHostSize();
    }

    internal void DetachDrawerHost(Grid host)
    {
        var side = ReferenceEquals(sideDrawerHost, host);
        if (!side && !ReferenceEquals(bottomDrawerHost, host)) return;
        var slot = side ? RightDrawerSlot : BottomDrawerSlot;
        var restoreTrackOffset = !side && hasSavedTrackOffset;
        if (!side) { drawerAttachmentGeneration++; ResetPianoRollExpansion(); }
        SettleDrawerHostAnimation(host, false);
        host.LayoutUpdated -= OnDrawerLayoutUpdated;
        slot.Children.Remove(host);
        host.ClearValue(HeightProperty);
        host.ClearValue(WidthProperty);
        host.ClearValue(VerticalAlignmentProperty);
        slot.Visibility = Visibility.Collapsed;
        slot.ClearValue(HeightProperty);
        slot.ClearValue(WidthProperty);
        if (side) { sideDrawerHost = null; sideDrawerTargetExtent = 0; }
        else { bottomDrawerHost = null; bottomDrawerTargetExtent = 0; }
        WorkstationDockLayout.UpdateLayout();
        if (restoreTrackOffset) TrackListComponent.RestoreVerticalOffset(savedTrackOffset);
        if (!side) { hasSavedTrackOffset = false; TrackScrollBarGutter = TrackListComponent.TrackScrollBarGutter; }
        UpdateDrawerHostSize();
    }

    private void OnTracksLayoutSizeChanged(object sender, SizeChangedEventArgs e) => UpdateDrawerHostSize();
    private void OnDrawerLayoutUpdated(object? sender, EventArgs e) => UpdateDrawerHostSize();

    internal static double CalculateSideDrawerWidth(double availableWidth, double trackSideWidth, double minimumTimeViewportWidth = 0)
    {
        static double Budget(double value) => double.IsFinite(value) ? Math.Max(0, value) : 0;
        var flexible = Math.Max(0, Budget(availableWidth) - Budget(trackSideWidth));
        return Math.Min(480, Math.Min(flexible / 2, Math.Max(0, flexible - Budget(minimumTimeViewportWidth))));
    }

    internal static double CalculateBottomDrawerHeight(double availableHeight, double desiredHeight, bool sizeToContent) =>
        sizeToContent ? Math.Min(availableHeight, desiredHeight) : availableHeight / 2;

    internal static Size CalculateDrawerHostSize(OHsWorkstationDrawerKind kind, Size dockSize,
        Size tracksSize, double fixedTrackSideWidth, double desiredContentHeight, double arrangementExpansionProgress = 0,
        double minimumTimeViewportWidth = 0) =>
        kind == OHsWorkstationDrawerKind.Samples
            ? new Size(CalculateSideDrawerWidth(dockSize.Width, fixedTrackSideWidth, minimumTimeViewportWidth), dockSize.Height)
            : new Size(tracksSize.Width, kind == OHsWorkstationDrawerKind.Arrangement ? tracksSize.Height * (0.5 + 0.5 * Math.Clamp(arrangementExpansionProgress, 0, 1)) : CalculateBottomDrawerHeight(tracksSize.Height, desiredContentHeight,
                kind == OHsWorkstationDrawerKind.KeysMapping));

    private void UpdateDrawerHostSize()
    {
        if (WorkstationDockLayout.ActualWidth <= 0) return;
        var tracksSize = new Size(TracksLayout.ActualWidth, Math.Max(0, TracksLayout.ActualHeight));
        var dockSize = new Size(WorkstationDockLayout.ActualWidth, Math.Max(0, WorkstationDockLayout.ActualHeight));
        if (sideDrawerHost is { } sideHost)
        {
            var fixedWidth = TrackSideColumnWidth.Value + TracksCard.Padding.Left + TracksCard.Padding.Right +
                TracksCard.BorderThickness.Left + TracksCard.BorderThickness.Right +
                ((GridLength)FindResource("OHsWorkstationColumnGap")).Value + TrackScrollBarGutter +
                RightDrawerSlot.Margin.Left + RightDrawerSlot.Margin.Right;
            var size = CalculateDrawerHostSize(OHsWorkstationDrawerKind.Samples, dockSize, tracksSize,
                fixedWidth, 0, minimumTimeViewportWidth: (double)FindResource("Size.Control.Primary"));
            if (sideHost.Width != size.Width) sideHost.Width = size.Width;
            if (sideHost.Height != size.Height) sideHost.Height = size.Height;
            sideDrawerTargetExtent = size.Width;
        }
        if (bottomDrawerHost is not { } host) { UpdateDrawerReveal(); return; }
        var desiredHeight = 0d;
        if (bottomDrawerKind == OHsWorkstationDrawerKind.KeysMapping && host.Children.Count > 0)
        {
            var presenter = host.Children[0];
            presenter.Measure(new Size(tracksSize.Width, double.PositiveInfinity));
            desiredHeight = presenter.DesiredSize.Height;
        }
        var bottomSize = CalculateDrawerHostSize(bottomDrawerKind, dockSize, tracksSize,
            TrackSideColumnWidth.Value, desiredHeight, PianoRollExpansionProgress);
        if (host.Width != bottomSize.Width) host.Width = bottomSize.Width;
        if (host.Height != bottomSize.Height) host.Height = bottomSize.Height;
        bottomDrawerTargetExtent = bottomSize.Height;
        UpdateDrawerReveal();
    }

    private void UpdateDrawerReveal()
    {
        if (sideDrawerHost is not null)
        {
            var width = sideDrawerTargetExtent * Math.Clamp((double)GetValue(SideDrawerProgressProperty), 0, 1);
            if (RightDrawerSlot.Width != width) RightDrawerSlot.Width = width;
        }
        if (bottomDrawerHost is not null)
        {
            var height = bottomDrawerTargetExtent * Math.Clamp((double)GetValue(BottomDrawerProgressProperty), 0, 1);
            if (BottomDrawerSlot.Height != height) BottomDrawerSlot.Height = height;
        }
    }

    private bool IsDrawerOrigin(DependencyObject? origin) =>
        sideDrawerHost is not null && IsWithin(origin, sideDrawerHost) || bottomDrawerHost is not null && IsWithin(origin, bottomDrawerHost);

    private DependencyProperty? DrawerProgressPropertyFor(Grid host) => ReferenceEquals(sideDrawerHost, host)
        ? SideDrawerProgressProperty : ReferenceEquals(bottomDrawerHost, host) ? BottomDrawerProgressProperty : null;

    internal void BeginDrawerHostAnimation(Grid host, bool opening, Duration duration, IEasingFunction easing)
    {
        if (DrawerProgressPropertyFor(host) is not { } property) return;
        if (opening) SettleDrawerHostAnimation(host, false);
        BeginAnimation(property, new DoubleAnimation(
            (double)GetValue(property), opening ? 1 : 0, duration) { EasingFunction = easing });
    }

    internal void ClearDrawerHostAnimation(Grid host)
    {
        if (DrawerProgressPropertyFor(host) is not { } property) return;
        var progress = (double)GetValue(property);
        BeginAnimation(property, null);
        SetValue(property, progress);
    }

    internal void SettleDrawerHostAnimation(Grid host, bool opening)
    {
        if (DrawerProgressPropertyFor(host) is not { } property) return;
        BeginAnimation(property, null);
        SetValue(property, opening ? 1d : 0d);
    }
}
