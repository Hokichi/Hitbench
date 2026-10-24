namespace Onset.Resources.DataModels;

public sealed record ControlPointSnapshot(Guid Id, int Timestamp, int Volume, string Sampleset, bool IsDisabled = false);
