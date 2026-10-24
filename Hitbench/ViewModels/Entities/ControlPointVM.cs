using CommunityToolkit.Mvvm.ComponentModel;

namespace Hitbench.ViewModels.Entities;

public partial class ControlPointVM : ObservableObject
{
    [ObservableProperty] private int timestamp;
    [ObservableProperty] private int volume;
}
