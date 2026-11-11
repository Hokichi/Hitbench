namespace Hitbench.Core.Entities;

public class ProjectData
{
    public string NormalHitNormalPath { get; set; } = string.Empty;
    public string SoftHitNormalPath { get; set; } = string.Empty;
    public string DrumHitNormalPath { get; set; } = string.Empty;
    public string CurrentSkin { get; set; } = string.Empty;
    public bool AllowDefaults { get; set; }
    public bool IsPinned { get; set; }
    public bool IsMarkerStatic { get; set; }
    public DateTimeOffset LastChanged { get; set; }
    public int WorkstationZoom { get; set; } = 32;
    public string BeatmapPath { get; set; } = string.Empty;
    public List<Track> Tracks { get; set; } = [];
    public List<ControlPoint> ControlPoints { get; set; } = [];
    public List<UninheritedPoint> UninheritedPoints { get; set; } = [];

    public bool IsOffsetControlPoint(ControlPoint point) => UninheritedPoints.Count > 0 && point.Timestamp == UninheritedPoints[0].Offset;

    public bool EnsureOffsetControlPoint()
    {
        if (UninheritedPoints.Count == 0 || ControlPoints.Any(IsOffsetControlPoint)) return false;
        ControlPoints.Add(new ControlPoint { Timestamp = UninheritedPoints[0].Offset });
        return true;
    }
}
