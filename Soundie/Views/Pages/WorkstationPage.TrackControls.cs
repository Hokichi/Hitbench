using System.Windows;
namespace Soundie.Views.Pages;
public partial class WorkstationPage
{
    public static readonly DependencyProperty TrackScrollBarGutterProperty = DependencyProperty.Register(nameof(TrackScrollBarGutter), typeof(double), typeof(WorkstationPage));
    public double TrackScrollBarGutter { get => (double)GetValue(TrackScrollBarGutterProperty); set => SetValue(TrackScrollBarGutterProperty, value); }
    internal void RestoreTrackFocusBeforeCollapse(DependencyObject? focusedElement)
    {
        if (TrackListComponent.RestoreTrackFocusBeforeCollapse(focusedElement)) AudioCard.FocusCollapseAction();
    }
}
