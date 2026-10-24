using Hitbench.Resources.DataModels;

namespace Hitbench.Resources.Messages;

public sealed record SetControlPointDisabledRequestedMessage(ControlPointsContext Context, Guid PointId,
    long ExpectedRevision, bool IsDisabled);
