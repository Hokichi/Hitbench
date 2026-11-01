namespace Hitbench.Resources.DataModels;
public sealed record OHsTrackAppearanceRequest(string? Color, int TrackColorIndex)
{
    public string Name { get; init; } = string.Empty;
}
