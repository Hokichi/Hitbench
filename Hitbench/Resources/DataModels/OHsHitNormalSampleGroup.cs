using CommunityToolkit.Mvvm.ComponentModel;

namespace Hitbench.Resources.DataModels;

public sealed partial class OHsHitNormalSampleGroup(string name, bool isPinned = false) : ObservableObject
{
    public string Name { get; } = name;
    public bool IsPinned { get; } = isPinned;
    [ObservableProperty] private bool isExpanded = true;
}
