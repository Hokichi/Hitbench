using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Hitbench.Resources.DataModels;

namespace Hitbench.Views.Pages;

public partial class WorkstationPage
{
    private static readonly DependencyProperty DrawerProgressProperty = DependencyProperty.Register(
        "DrawerProgress", typeof(double), typeof(WorkstationPage), new PropertyMetadata(0d,
            (owner, _) => ((WorkstationPage)owner).UpdateDrawerReveal()));
    private static readonly DependencyPropertyKey IsPianoRollExpandedPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsPianoRollExpanded), typeof(bool), typeof(WorkstationPage), new PropertyMetadata(false));
    public static readonly DependencyProperty IsPianoRollExpandedProperty = IsPianoRollExpandedPropertyKey.DependencyProperty;
    public bool IsPianoRollExpanded => (bool)GetValue(IsPianoRollExpandedProperty);
    private double savedTrackOffset;
    private int drawerAttachmentGeneration;
    internal void TogglePianoRollExpansion()
    {
        if (drawerHost is null || drawerKind != OHsWorkstationDrawerKind.Arrangement || deactivationStarted) return;
        SetPianoRollExpanded(!IsPianoRollExpanded);
    }
    private void SetPianoRollExpanded(bool expanded)
    {
        if (expanded == IsPianoRollExpanded) return;
        if (expanded)
        {
            savedTrackOffset = TrackListComponent.CaptureVerticalOffset();
            TrackListComponent.CancelNodePointer();
        }
        SetValue(IsPianoRollExpandedPropertyKey, expanded);
        TracksCard.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        BottomDrawerSlot.Margin = expanded ? new Thickness(0) : new Thickness(0, 12, 0, 0);
        UpdateDrawerHostSize();
        if (!expanded)
        {
            var generation = drawerAttachmentGeneration;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
            {
                if (!deactivationStarted && !IsPianoRollExpanded && drawerAttachmentGeneration == generation)
                    TrackListComponent.RestoreVerticalOffset(savedTrackOffset);
            });
        }
    }
    private Grid? drawerHost;
    private OHsWorkstationDrawerKind drawerKind;
    private double drawerTargetExtent;
    private Grid DrawerSlot => drawerKind == OHsWorkstationDrawerKind.Samples ? RightDrawerSlot : BottomDrawerSlot;

    internal void AttachDrawerHost(Grid host, OHsWorkstationDrawerKind kind)
    {
        if (drawerHost is not null) throw new InvalidOperationException("A drawer is already attached.");
        drawerAttachmentGeneration++;
        drawerKind = kind;
        drawerHost = host;
        DrawerSlot.Children.Add(host);
        host.VerticalAlignment = VerticalAlignment.Top;
        DrawerSlot.Visibility = Visibility.Visible;
        host.LayoutUpdated += OnDrawerLayoutUpdated;
        SettleDrawerHostAnimation(false);
        UpdateDrawerHostSize();
    }

    internal void DetachDrawerHost(Grid host)
    {
        if (!ReferenceEquals(drawerHost, host)) return;
        var restoreTrackOffset = IsPianoRollExpanded;
        SetPianoRollExpanded(false);
        ClearDrawerHostAnimation();
        host.LayoutUpdated -= OnDrawerLayoutUpdated;
        DrawerSlot.Children.Remove(host);
        host.ClearValue(HeightProperty);
        host.ClearValue(WidthProperty);
        host.ClearValue(VerticalAlignmentProperty);
        DrawerSlot.Visibility = Visibility.Collapsed;
        DrawerSlot.ClearValue(HeightProperty);
        DrawerSlot.ClearValue(WidthProperty);
        drawerHost = null;
        drawerTargetExtent = 0;
        SetValue(DrawerProgressProperty, 0d);
        WorkstationDockLayout.UpdateLayout();
        if (restoreTrackOffset) TrackListComponent.RestoreVerticalOffset(savedTrackOffset);
        TrackScrollBarGutter = TrackListComponent.TrackScrollBarGutter;
    }

    private void OnTracksLayoutSizeChanged(object sender, SizeChangedEventArgs e) => UpdateDrawerHostSize();
    private void OnDrawerLayoutUpdated(object? sender, EventArgs e) => UpdateDrawerHostSize();

    internal static double CalculateSideDrawerWidth(double availableWidth, double trackSideWidth) =>
        Math.Min(480, Math.Max(0, availableWidth - trackSideWidth) / 2);

    internal static double CalculateBottomDrawerHeight(double availableHeight, double desiredHeight, bool sizeToContent) =>
        sizeToContent ? Math.Min(availableHeight, desiredHeight) : availableHeight / 2;

    internal static Size CalculateDrawerHostSize(OHsWorkstationDrawerKind kind, Size dockSize,
        Size tracksSize, double fixedTrackSideWidth, double desiredContentHeight, bool arrangementExpanded = false) =>
        kind == OHsWorkstationDrawerKind.Samples
            ? new Size(CalculateSideDrawerWidth(dockSize.Width, fixedTrackSideWidth), dockSize.Height)
            : new Size(tracksSize.Width, arrangementExpanded && kind == OHsWorkstationDrawerKind.Arrangement ? tracksSize.Height : CalculateBottomDrawerHeight(tracksSize.Height, desiredContentHeight,
                kind == OHsWorkstationDrawerKind.KeysMapping));

    private void UpdateDrawerHostSize()
    {
        if (drawerHost is not { } host || TracksLayout.ActualWidth <= 0) return;
        var tracksSize = new Size(TracksLayout.ActualWidth, Math.Max(0, TracksLayout.ActualHeight));
        var dockSize = new Size(WorkstationDockLayout.ActualWidth, Math.Max(0, WorkstationDockLayout.ActualHeight));
        var side = drawerKind == OHsWorkstationDrawerKind.Samples;
        var desiredHeight = 0d;
        if (drawerKind == OHsWorkstationDrawerKind.KeysMapping)
        {
            var presenter = host.Children[0];
            presenter.Measure(new Size(tracksSize.Width, double.PositiveInfinity));
            desiredHeight = presenter.DesiredSize.Height;
        }
        var size = CalculateDrawerHostSize(drawerKind, dockSize, tracksSize,
            TrackSideColumnWidth.Value + TracksCard.Padding.Left + TracksCard.Padding.Right + 24, desiredHeight, IsPianoRollExpanded);
        if (host.Width != size.Width) host.Width = size.Width;
        if (host.Height != size.Height) host.Height = size.Height;
        var extent = side ? size.Width : size.Height;
        if (drawerTargetExtent == extent) return;
        drawerTargetExtent = extent;
        UpdateDrawerReveal();
    }

    private void UpdateDrawerReveal()
    {
        if (drawerHost is null) return;
        var extent = drawerTargetExtent * Math.Clamp((double)GetValue(DrawerProgressProperty), 0, 1);
        if (drawerKind == OHsWorkstationDrawerKind.Samples) RightDrawerSlot.Width = extent;
        else BottomDrawerSlot.Height = extent;
    }

    private bool IsDrawerOrigin(DependencyObject? origin) => drawerHost is not null && IsWithin(origin, drawerHost);

    internal void BeginDrawerHostAnimation(bool opening, Duration duration, IEasingFunction easing)
    {
        if (drawerHost is null) return;
        if (opening) SettleDrawerHostAnimation(false);
        BeginAnimation(DrawerProgressProperty, new DoubleAnimation(
            (double)GetValue(DrawerProgressProperty), opening ? 1 : 0, duration) { EasingFunction = easing });
    }

    internal void ClearDrawerHostAnimation()
    {
        var progress = (double)GetValue(DrawerProgressProperty);
        BeginAnimation(DrawerProgressProperty, null);
        SetValue(DrawerProgressProperty, progress);
    }

    internal void SettleDrawerHostAnimation(bool opening)
    {
        BeginAnimation(DrawerProgressProperty, null);
        SetValue(DrawerProgressProperty, opening ? 1d : 0d);
    }
}
