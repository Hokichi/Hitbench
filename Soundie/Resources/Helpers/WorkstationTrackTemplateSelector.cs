using System.Windows;
using System.Windows.Controls;
using Soundie.Core.Entities;
namespace Soundie.Resources.Helpers;

public sealed class WorkstationTrackTemplateSelector : DataTemplateSelector
{
    public DataTemplate? SampleTrackTemplate { get; set; }
    public DataTemplate? InstrumentalTrackTemplate { get; set; }
    public override DataTemplate? SelectTemplate(object item, DependencyObject container) => item is InstrumentalTrack ? InstrumentalTrackTemplate : SampleTrackTemplate;
}
