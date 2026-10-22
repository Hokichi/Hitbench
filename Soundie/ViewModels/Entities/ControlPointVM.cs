using CommunityToolkit.Mvvm.ComponentModel;

namespace Soundie.ViewModels.Entities;

public partial class ControlPointVM : ObservableObject
{
    [ObservableProperty] private int timestamp;
    [ObservableProperty] private int volume;
}
