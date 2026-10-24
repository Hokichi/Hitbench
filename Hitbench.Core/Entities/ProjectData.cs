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
}
