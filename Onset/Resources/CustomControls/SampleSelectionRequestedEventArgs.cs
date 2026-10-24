using System.Windows;
using Onset.Core.Entities;

namespace Onset.Resources.CustomControls;

public sealed class SampleSelectionRequestedEventArgs(RoutedEvent routedEvent, Sample sample) : RoutedEventArgs(routedEvent)
{
    public Sample Sample { get; } = sample;
}
