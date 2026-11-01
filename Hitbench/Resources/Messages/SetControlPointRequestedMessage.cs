using Hitbench.Resources.DataModels;
namespace Hitbench.Resources.Messages;

public sealed record SetControlPointRequestedMessage(ControlPointsContext Context, Guid PointId, decimal? Volume, string? Sampleset, long? ExpectedRevision = null, bool? IsRamp = null);
