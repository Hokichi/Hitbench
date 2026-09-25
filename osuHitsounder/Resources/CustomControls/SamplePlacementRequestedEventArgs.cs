using System.Windows;

namespace osuHitsounder.Resources.CustomControls;

public sealed class SamplePlacementRequestedEventArgs(RoutedEvent routedEvent, double leftBoundaryMilliseconds)
    : RoutedEventArgs(routedEvent)
{
    public double LeftBoundaryMilliseconds { get; } = leftBoundaryMilliseconds;
}
