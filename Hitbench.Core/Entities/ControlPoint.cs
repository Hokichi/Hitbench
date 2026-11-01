namespace Hitbench.Core.Entities;

public class ControlPoint
{
    public const int MinimumEditableVolume = 5;
    public const int MaximumVolume = 100;
    public static IReadOnlyList<string> SamplesetOptions { get; } = Array.AsReadOnly(new[] { "Normal", "Soft", "Drum" });
    public int Timestamp { get; set; }
    public int Volume { get; set; } = 100;
    public string Sampleset { get; set; } = "Normal";
    public bool IsDisabled { get; set; }
    public bool IsRamp { get; set; }
}
