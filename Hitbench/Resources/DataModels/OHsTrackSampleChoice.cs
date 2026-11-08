namespace Hitbench.Resources.DataModels;
public sealed record OHsTrackSampleChoice(string? FileName)
{
    public bool IsDefault => FileName is null;
    public string DisplayName => FileName ?? "Default";
}
