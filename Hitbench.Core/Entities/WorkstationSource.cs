namespace Hitbench.Core.Entities;

public sealed record WorkstationSource(string BeatmapPath, string AudioPath, ProjectData Project, bool RequiresTimingMigration = false);
