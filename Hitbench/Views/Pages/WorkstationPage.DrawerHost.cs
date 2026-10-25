using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace Hitbench.Views.Pages;

public partial class WorkstationPage
{
    private static readonly DependencyProperty BottomDrawerProgressProperty = DependencyProperty.Register(
        "BottomDrawerProgress", typeof(double), typeof(WorkstationPage), new PropertyMetadata(0d,
            (owner, _) => ((WorkstationPage)owner).UpdateBottomDrawerReveal()));
    private Grid? bottomDrawerHost;
    private bool bottomDrawerSizesToContent;
    private double bottomDrawerTargetHeight;

    internal void AttachBottomDrawerHost(Grid host, bool sizeToContent)
    {
        if (bottomDrawerHost is not null) throw new InvalidOperationException("A bottom drawer is already attached.");
        BottomDrawerSlot.Children.Add(host);
        bottomDrawerHost = host;
        bottomDrawerSizesToContent = sizeToContent;
        host.VerticalAlignment = VerticalAlignment.Top;
        BottomDrawerSlot.Visibility = Visibility.Visible;
        host.LayoutUpdated += OnBottomDrawerLayoutUpdated;
        SettleBottomDrawerHostAnimation(false);
        UpdateBottomDrawerHostSize();
    }

    internal void DetachBottomDrawerHost(Grid host)
    {
        if (!ReferenceEquals(bottomDrawerHost, host)) return;
        ClearBottomDrawerHostAnimation();
        host.LayoutUpdated -= OnBottomDrawerLayoutUpdated;
        BottomDrawerSlot.Children.Remove(host);
        host.ClearValue(HeightProperty);
        host.ClearValue(VerticalAlignmentProperty);
        bottomDrawerHost = null;
        bottomDrawerTargetHeight = 0;
        SetValue(BottomDrawerProgressProperty, 0d);
        BottomDrawerSlot.Visibility = Visibility.Collapsed;
        BottomDrawerSlot.ClearValue(HeightProperty);
    }

    private void OnTracksLayoutSizeChanged(object sender, SizeChangedEventArgs e) => UpdateBottomDrawerHostSize();
    private void OnBottomDrawerLayoutUpdated(object? sender, EventArgs e) => UpdateBottomDrawerHostSize();

    private void UpdateBottomDrawerHostSize()
    {
        if (bottomDrawerHost is not { } host || TracksLayout.ActualWidth <= 0) return;
        var height = Math.Max(0, TracksLayout.ActualHeight / 2);
        if (bottomDrawerSizesToContent)
        {
            var presenter = host.Children[0];
            presenter.Measure(new Size(TracksLayout.ActualWidth, height));
            height = Math.Min(height, presenter.DesiredSize.Height);
        }
        if (host.Height != height) host.Height = height;
        if (bottomDrawerTargetHeight == height) return;
        bottomDrawerTargetHeight = height;
        UpdateBottomDrawerReveal();
    }

    private void UpdateBottomDrawerReveal() =>
        BottomDrawerSlot.Height = bottomDrawerTargetHeight * Math.Clamp((double)GetValue(BottomDrawerProgressProperty), 0, 1);

    private bool IsBottomDrawerOrigin(DependencyObject? origin) =>
        bottomDrawerHost is not null && IsWithin(origin, bottomDrawerHost);

    internal void BeginBottomDrawerHostAnimation(bool opening, Duration duration, IEasingFunction easing)
    {
        if (bottomDrawerHost is null) return;
        if (opening) SettleBottomDrawerHostAnimation(false);
        BeginAnimation(BottomDrawerProgressProperty, new DoubleAnimation(
            (double)GetValue(BottomDrawerProgressProperty), opening ? 1 : 0, duration) { EasingFunction = easing });
    }

    internal void ClearBottomDrawerHostAnimation()
    {
        var progress = (double)GetValue(BottomDrawerProgressProperty);
        BeginAnimation(BottomDrawerProgressProperty, null);
        SetValue(BottomDrawerProgressProperty, progress);
    }

    internal void SettleBottomDrawerHostAnimation(bool opening)
    {
        BeginAnimation(BottomDrawerProgressProperty, null);
        SetValue(BottomDrawerProgressProperty, opening ? 1d : 0d);
    }
}
