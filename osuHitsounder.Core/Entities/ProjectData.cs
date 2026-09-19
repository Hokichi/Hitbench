namespace osuHitsounder.Core.Entities;

public class ProjectData
{
    public string BeatmapPath { get; set; } = string.Empty;
    public List<Sample> Samples { get; set; } = [];
    public List<ControlPoint> ControlPoints { get; set; } = [];
    public List<int> Offsets { get; set; } = [];
    public List<int> BPMs { get; set; } = [];
}
