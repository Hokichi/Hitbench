using System.Windows;
using Soundie.Core.Entities;

namespace Soundie.Resources.CustomControls;

public sealed class SampleSelectionRequestedEventArgs(RoutedEvent routedEvent, Sample sample) : RoutedEventArgs(routedEvent)
{
    public Sample Sample { get; } = sample;
}
