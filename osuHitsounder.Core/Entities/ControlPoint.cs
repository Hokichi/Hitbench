namespace osuHitsounder.Core.Entities;

public class ControlPoint
{
    public int Timestamp { get; set; }
    public int Volume { get; set; } = 100;
    public string Sampleset { get; set; } = "Normal";
}
