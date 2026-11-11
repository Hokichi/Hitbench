using CommunityToolkit.Mvvm.ComponentModel;
using Hitbench.Core.Entities;

namespace Hitbench.Resources.DataModels;

public sealed partial class OHsHitNormalSample(string path, string displayName, OHsHitNormalSampleSource source,
    OHsHitNormalSampleGroup group) : ObservableObject
{
    public string Path { get; } = path;
    public string DisplayName { get; } = displayName;
    public string DisplayFileName => System.IO.Path.GetFileName(DisplayName);
    public OHsHitNormalSampleSource Source { get; } = source;
    [ObservableProperty] private WaveformData? waveform;
    [ObservableProperty] private bool isDefault;
    [ObservableProperty] private bool isCurrent;
    [ObservableProperty] private OHsHitNormalSampleGroup group = group;
}
