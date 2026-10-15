namespace osuHitsounder.Core.Entities;

public class ControlPoint
{
    public static IReadOnlyList<string> SamplesetOptions { get; } = Array.AsReadOnly(new[] { "Normal", "Soft", "Drum" });
    public int Timestamp { get; set; }
    public int Volume { get; set; } = 100;
    public string Sampleset { get; set; } = "Normal";
}
