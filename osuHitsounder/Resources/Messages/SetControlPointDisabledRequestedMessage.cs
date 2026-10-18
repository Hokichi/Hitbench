using osuHitsounder.Resources.DataModels;

namespace osuHitsounder.Resources.Messages;

public sealed record SetControlPointDisabledRequestedMessage(ControlPointsContext Context, Guid PointId,
    long ExpectedRevision, bool IsDisabled);
