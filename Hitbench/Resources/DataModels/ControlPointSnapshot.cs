namespace Hitbench.Resources.DataModels;

public sealed record ControlPointSnapshot(Guid Id, int Timestamp, int Volume, string Sampleset, bool IsDisabled = false, bool IsOffsetAnchor = false, bool IsRamp = false);
