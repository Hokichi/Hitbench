using Hitbench.Resources.DataModels;

namespace Hitbench.Resources.Messages;

public sealed record MoveControlPointRequestedMessage(ControlPointsContext Context, Guid PointId,
    long ExpectedRevision, int Timestamp);
