using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Onset.ViewModels.Entities;

public partial class SampleVM : ObservableObject
{
    [ObservableProperty] private string path = string.Empty;
    [ObservableProperty] private ObservableCollection<int> timestamps = [];
}
