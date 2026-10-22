using CommunityToolkit.Mvvm.ComponentModel;

namespace Soundie.Resources.CustomControls;

public sealed partial class StepNavigatorDotVM : ObservableObject
{
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isCompleted;
    public bool IsFirst { get; init; }
}


