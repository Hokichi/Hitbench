namespace Hitbench.Resources.DataModels;

public sealed record OHsHitNormalChoice(string Path, string DisplayName,
    OHsHitNormalSampleSource Source = OHsHitNormalSampleSource.Project);
