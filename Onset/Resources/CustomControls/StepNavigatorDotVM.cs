using CommunityToolkit.Mvvm.ComponentModel;

namespace Onset.Resources.CustomControls;

public sealed partial class StepNavigatorDotVM : ObservableObject
{
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isCompleted;
    public bool IsFirst { get; init; }
}


