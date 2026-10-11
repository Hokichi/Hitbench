namespace osuHitsounder.Core.Entities;

public class Track
{
    public static IReadOnlyList<string> SamplesetOptions { get; } = Array.AsReadOnly(new[] { "Normal", "Soft", "Drum" });
    public static IReadOnlyList<string> AdditionOptions { get; } = Array.AsReadOnly(new[] { "Normal", "Soft", "Drum", "None" });
    public string Sampleset { get; set; } = "Normal";
    public string Addition { get; set; } = "None";
    public string Name { get; set; } = string.Empty;
    public string? SelectedSampleFileName { get; set; }
    public List<Sample> Samples { get; set; } = [];
    public int Volume { get; set; } = 100;
    public bool IsTrackMuted { get; set; }
    public bool IsTrackSoloed { get; set; }
}
