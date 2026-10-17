using osuHitsounder.Resources.DataModels;

namespace osuHitsounder.Resources.Messages;

public sealed record MoveControlPointRequestedMessage(ControlPointsContext Context, Guid PointId,
    long ExpectedRevision, int Timestamp);
