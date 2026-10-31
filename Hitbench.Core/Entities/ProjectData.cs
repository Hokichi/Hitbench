namespace Hitbench.Core.Entities;

public class ProjectData
{
    public bool IsPinned { get; set; }
    public DateTimeOffset LastChanged { get; set; }
    public int WorkstationZoom { get; set; } = 32;
    public string BeatmapPath { get; set; } = string.Empty;
    public List<Track> Tracks { get; set; } = [];
    public List<ControlPoint> ControlPoints { get; set; } = [];
    public List<int> Offsets { get; set; } = [];
    public List<int> BPMs { get; set; } = [];

    public bool IsOffsetControlPoint(ControlPoint point) => Offsets.Count > 0 && point.Timestamp == Offsets[0];

    public bool EnsureOffsetControlPoint()
    {
        if (Offsets.Count == 0 || ControlPoints.Any(IsOffsetControlPoint)) return false;
        ControlPoints.Add(new ControlPoint { Timestamp = Offsets[0] });
        return true;
    }
}
