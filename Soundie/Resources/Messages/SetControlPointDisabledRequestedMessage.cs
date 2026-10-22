using Soundie.Resources.DataModels;

namespace Soundie.Resources.Messages;

public sealed record SetControlPointDisabledRequestedMessage(ControlPointsContext Context, Guid PointId,
    long ExpectedRevision, bool IsDisabled);
