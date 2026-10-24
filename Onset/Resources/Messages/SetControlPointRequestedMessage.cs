using Onset.Resources.DataModels;
namespace Onset.Resources.Messages;

public sealed record SetControlPointRequestedMessage(ControlPointsContext Context, Guid PointId, decimal? Volume, string? Sampleset, long? ExpectedRevision = null);
