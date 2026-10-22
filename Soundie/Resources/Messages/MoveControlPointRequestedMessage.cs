using Soundie.Resources.DataModels;

namespace Soundie.Resources.Messages;

public sealed record MoveControlPointRequestedMessage(ControlPointsContext Context, Guid PointId,
    long ExpectedRevision, int Timestamp);
