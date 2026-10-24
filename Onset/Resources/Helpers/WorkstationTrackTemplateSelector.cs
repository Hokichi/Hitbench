using System.Windows;
using System.Windows.Controls;
using Onset.Core.Entities;
namespace Onset.Resources.Helpers;

public sealed class WorkstationTrackTemplateSelector : DataTemplateSelector
{
    public DataTemplate? SampleTrackTemplate { get; set; }
    public DataTemplate? InstrumentalTrackTemplate { get; set; }
    public override DataTemplate? SelectTemplate(object item, DependencyObject container) => item is InstrumentalTrack ? InstrumentalTrackTemplate : SampleTrackTemplate;
}
