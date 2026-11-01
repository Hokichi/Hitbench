namespace Hitbench.Core.Entities;

public sealed record ControlPointVolumeSnapshot(int Timestamp, int Volume, bool IsDisabled = false, bool IsRamp = false);
