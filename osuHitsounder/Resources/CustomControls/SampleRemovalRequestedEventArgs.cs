using System.Windows;
using osuHitsounder.Core.Entities;

namespace osuHitsounder.Resources.CustomControls;

public sealed class SampleRemovalRequestedEventArgs(RoutedEvent routedEvent, Sample sample)
    : RoutedEventArgs(routedEvent)
{
    public Sample Sample { get; } = sample;
}
