using Onset.Resources.DataModels;

namespace Onset.Resources.Messages;

public sealed record SetControlPointDisabledRequestedMessage(ControlPointsContext Context, Guid PointId,
    long ExpectedRevision, bool IsDisabled);
