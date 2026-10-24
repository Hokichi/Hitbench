using System.Windows;

namespace Onset.Resources.CustomControls;

public sealed class SamplePlacementRequestedEventArgs(RoutedEvent routedEvent, double leftBoundaryMilliseconds)
    : RoutedEventArgs(routedEvent)
{
    public double LeftBoundaryMilliseconds { get; } = leftBoundaryMilliseconds;
}
