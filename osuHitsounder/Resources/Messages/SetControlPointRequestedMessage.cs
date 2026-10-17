using osuHitsounder.Resources.DataModels;
namespace osuHitsounder.Resources.Messages;

public sealed record SetControlPointRequestedMessage(ControlPointsContext Context, Guid PointId, decimal? Volume, string? Sampleset);
