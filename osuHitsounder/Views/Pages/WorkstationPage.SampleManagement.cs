using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.DataModels;

namespace osuHitsounder.Views.Pages;

public partial class WorkstationPage
{
    private void OnTrackGridHostLoaded(object sender, RoutedEventArgs e) => RefreshTrackSamplePresentation();
    private void OnTrackGridHostDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) => RefreshTrackSamplePresentation();
    private void RefreshTrackSamplePresentation() => RefreshTrackSamplePresentation(TrackList);
    private void RefreshTrackSamplePresentation(DependencyObject root)
    {
        if (root is Grid { Name: "TrackGridHost", DataContext: Track track } host)
        {
            var state = viewModel.GetSampleAvailability(track); host.Tag = state;
            var content = FindVisualChild<Grid>(host, "TrackGridContent");
            var status = FindVisualChild<TextBlock>(host, "MissingGridStatus");
            if (content is not null) content.IsEnabled = state == TrackSampleAvailability.Available;
            if (status is not null) status.Text = state switch
            {
                TrackSampleAvailability.Missing => "Sample not found.",
                TrackSampleAvailability.Unselected => "No sample selected. Choose a sample or use Manage Sample.",
                _ => string.Empty
            };
            return;
        }
        for (var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) RefreshTrackSamplePresentation(VisualTreeHelper.GetChild(root,i));
    }
}
