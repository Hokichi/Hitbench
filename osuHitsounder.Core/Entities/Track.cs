namespace osuHitsounder.Core.Entities;

public class Track
{
    public string? SelectedSampleFileName { get; set; }
    public List<Sample> Samples { get; set; } = [];
    public int Volume { get; set; } = 100;
}
