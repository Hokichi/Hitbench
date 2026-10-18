using osuHitsounder.Resources.DataModels;

namespace osuHitsounder.Resources.Messages;

public sealed record SplitControlPointRequestedMessage(ControlPointsContext Context, Guid PointId, long ExpectedRevision);
