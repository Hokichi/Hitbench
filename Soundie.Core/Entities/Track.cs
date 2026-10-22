namespace Soundie.Core.Entities;

public class Track
{
    public static IReadOnlyList<string> AdditionOptions { get; } = Array.AsReadOnly(new[] { "None", "Normal", "Soft", "Drum" });
    public static IReadOnlyList<Soundie.Core.Enums.SampleType> SampleTypeOptions { get; } = Array.AsReadOnly(Enum.GetValues<Soundie.Core.Enums.SampleType>());
    public Soundie.Core.Enums.SampleType SampleType { get; set; }
    public string Addition { get; set; } = "None";
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
    public string? SelectedSampleFileName { get; set; }
    public List<Sample> Samples { get; set; } = [];
    public int Volume { get; set; } = 100;
    public bool IsCollapsed { get; set; }
    public bool IsTrackMuted { get; set; }
    public bool IsTrackSoloed { get; set; }
}
