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
    private Grid? drawerHost;
    private OHsWorkstationDrawerKind drawerKind;
    private double drawerTargetExtent;
    private Grid DrawerSlot => drawerKind == OHsWorkstationDrawerKind.Samples ? RightDrawerSlot : BottomDrawerSlot;

    internal void AttachDrawerHost(Grid host, OHsWorkstationDrawerKind kind)
    {
        if (drawerHost is not null) throw new InvalidOperationException("A drawer is already attached.");
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
        TracksLayout.UpdateLayout();
        TrackScrollBarGutter = TrackListComponent.TrackScrollBarGutter;
    }

    private void OnTracksLayoutSizeChanged(object sender, SizeChangedEventArgs e) => UpdateDrawerHostSize();
    private void OnDrawerLayoutUpdated(object? sender, EventArgs e) => UpdateDrawerHostSize();

    internal static double CalculateSideDrawerWidth(double availableWidth, double trackSideWidth) =>
        Math.Min(480, Math.Max(0, availableWidth - trackSideWidth) / 2);

    internal static double CalculateBottomDrawerHeight(double availableHeight, double desiredHeight, bool sizeToContent) =>
        sizeToContent ? Math.Min(availableHeight, desiredHeight) : availableHeight / 2;

    private void UpdateDrawerHostSize()
    {
        if (drawerHost is not { } host || TracksLayout.ActualWidth <= 0) return;
        var width = TracksLayout.ActualWidth;
        var height = Math.Max(0, TracksLayout.ActualHeight);
        var side = drawerKind == OHsWorkstationDrawerKind.Samples;
        if (side)
            width = CalculateSideDrawerWidth(width, TrackSideColumnWidth.Value + TracksCard.Padding.Left + TracksCard.Padding.Right + 24);
        else if (drawerKind == OHsWorkstationDrawerKind.KeysMapping)
        {
            var presenter = host.Children[0];
            presenter.Measure(new Size(width, double.PositiveInfinity));
            height = CalculateBottomDrawerHeight(height, presenter.DesiredSize.Height, true);
        }
        else height = CalculateBottomDrawerHeight(height, 0, false);
        if (host.Width != width) host.Width = width;
        if (host.Height != height) host.Height = height;
        var extent = side ? width : height;
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
