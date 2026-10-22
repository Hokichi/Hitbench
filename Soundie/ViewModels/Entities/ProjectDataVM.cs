using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Soundie.Core.Entities;

namespace Soundie.ViewModels.Entities;

public partial class ProjectDataVM : ObservableObject
{
    [ObservableProperty] private string beatmapPath = string.Empty;
    [ObservableProperty] private ObservableCollection<Sample> samples = [];
    [ObservableProperty] private ObservableCollection<ControlPoint> controlPoints = [];
}
