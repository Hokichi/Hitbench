using CommunityToolkit.Mvvm.ComponentModel;

namespace Onset.ViewModels.Entities;

public partial class ControlPointVM : ObservableObject
{
    [ObservableProperty] private int timestamp;
    [ObservableProperty] private int volume;
}
