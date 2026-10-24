using Onset.Resources.DataModels;

namespace Onset.Resources.Messages;

public sealed record MoveControlPointRequestedMessage(ControlPointsContext Context, Guid PointId,
    long ExpectedRevision, int Timestamp);
