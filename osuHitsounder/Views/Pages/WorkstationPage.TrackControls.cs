using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Track = osuHitsounder.Core.Entities.Track;

namespace osuHitsounder.Views.Pages;

public partial class WorkstationPage
{
    public static readonly DependencyProperty TrackScrollBarGutterProperty = DependencyProperty.Register(
        nameof(TrackScrollBarGutter), typeof(double), typeof(WorkstationPage));
    public double TrackScrollBarGutter
    {
        get => (double)GetValue(TrackScrollBarGutterProperty);
        set => SetValue(TrackScrollBarGutterProperty, value);
    }

    private void OnTrackAuditionLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { DataContext: Track track } toggle) return;
        toggle.Tag = true;
        toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, toggle.Name == "TrackMuteToggle"
            ? viewModel.IsTrackMuted(track) : viewModel.IsTrackSoloed(track));
        toggle.Tag = false;
    }

    private void OnTrackAuditionChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { IsLoaded: true, DataContext: Track track } toggle || toggle.Tag is true) return;
        if (toggle.Name == "TrackMuteToggle") viewModel.SetTrackMuted(track, toggle.IsChecked is true);
        else if (toggle.Name == "TrackSoloToggle") viewModel.SetTrackSoloed(track, toggle.IsChecked is true);
        OnTrackAuditionLoaded(toggle, new RoutedEventArgs());
    }

    private void RefreshTrackAuditionControls(DependencyObject root)
    {
        if (root is ToggleButton { Name: "TrackMuteToggle" or "TrackSoloToggle" } toggle)
            OnTrackAuditionLoaded(toggle, new RoutedEventArgs());
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            RefreshTrackAuditionControls(VisualTreeHelper.GetChild(root, i));
    }

    private void OnTrackControlStackLoaded(object sender, RoutedEventArgs e) => UpdateTrackHeightMinimum(sender);
    private void OnTrackControlStackSizeChanged(object sender, SizeChangedEventArgs e) => UpdateTrackHeightMinimum(sender);

    private void UpdateTrackHeightMinimum(object sender)
    {
        if (deactivationStarted || sender is not StackPanel { Parent: Grid row }) return;
        var left = FindVisualChild<StackPanel>(row, "TrackLeftControls");
        var right = FindVisualChild<StackPanel>(row, "TrackRightControls");
        if (left is not null && right is not null)
            viewModel.SetTrackHeightMinimum(Math.Max(left.DesiredSize.Height, right.DesiredSize.Height));
    }

    private void OnTrackListViewportChanged(object sender, ScrollChangedEventArgs e)
    {
        if (deactivationStarted || sender is not ScrollViewer scroll || !ReferenceEquals(e.OriginalSource, sender)) return;
        var gutter = Math.Max(0, scroll.ActualWidth - scroll.ViewportWidth);
        if (double.IsFinite(gutter) && TrackScrollBarGutter != gutter)
        {
            TrackScrollBarGutter = gutter;
            readinessCandidate = null;
        }
    }
}
