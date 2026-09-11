using CommunityToolkit.Mvvm.ComponentModel;

namespace osuHitsounder.ViewModels.Entities;

public partial class ControlPointVM : ObservableObject
{
    [ObservableProperty] private int timestamp;
    [ObservableProperty] private int volume;
}
