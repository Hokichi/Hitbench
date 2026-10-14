using osuHitsounder.Resources.DataModels;
namespace osuHitsounder.Resources.Messages;

public sealed record DeleteControlPointRequestedMessage(ControlPointsContext Context, Guid PointId);
