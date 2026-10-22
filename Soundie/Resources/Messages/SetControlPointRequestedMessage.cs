using Soundie.Resources.DataModels;
namespace Soundie.Resources.Messages;

public sealed record SetControlPointRequestedMessage(ControlPointsContext Context, Guid PointId, decimal? Volume, string? Sampleset, long? ExpectedRevision = null);
