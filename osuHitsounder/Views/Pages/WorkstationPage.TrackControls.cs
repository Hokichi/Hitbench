using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using Track = osuHitsounder.Core.Entities.Track;

namespace osuHitsounder.Views.Pages;

public partial class WorkstationPage
{
    internal void RestoreTrackFocusBeforeCollapse(DependencyObject? focusedElement)
    {
        if (deactivationStarted || !IsWithin(focusedElement, TrackList)) return;
        for (var current = focusedElement; current is not null; current = GetInputParent(current))
        {
            if (current is not StackPanel { Name: "TrackLeftExpandedControls" or "TrackRightExpandedControls" }) continue;
            AudioCard.FocusCollapseAction();
            return;
        }
    }

    public static readonly DependencyProperty TrackScrollBarGutterProperty = DependencyProperty.Register(
        nameof(TrackScrollBarGutter), typeof(double), typeof(WorkstationPage));
    public double TrackScrollBarGutter
    {
        get => (double)GetValue(TrackScrollBarGutterProperty);
        set => SetValue(TrackScrollBarGutterProperty, value);
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
        UpdateNodeAdornerGeometry();
    }
}
