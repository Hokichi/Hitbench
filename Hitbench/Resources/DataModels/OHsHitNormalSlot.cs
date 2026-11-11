using CommunityToolkit.Mvvm.ComponentModel;

namespace Hitbench.Resources.DataModels;

public sealed partial class OHsHitNormalSlot(string name, OHsHitNormalSample sample) : ObservableObject
{
    public string Name { get; } = name;
    [ObservableProperty] private OHsHitNormalSample sample = sample;
}
