using System.Windows;
using Hitbench.Core.Entities;

namespace Hitbench.Resources.CustomControls;

public sealed class SampleRemovalRequestedEventArgs(RoutedEvent routedEvent, Sample sample)
    : RoutedEventArgs(routedEvent)
{
    public Sample Sample { get; } = sample;
}
