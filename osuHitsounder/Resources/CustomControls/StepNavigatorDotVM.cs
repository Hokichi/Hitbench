using CommunityToolkit.Mvvm.ComponentModel;

namespace osuHitsounder.Resources.CustomControls;

public sealed partial class StepNavigatorDotVM : ObservableObject
{
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isCompleted;
    public bool IsFirst { get; init; }
}


