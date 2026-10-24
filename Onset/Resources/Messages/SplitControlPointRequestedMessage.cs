using Onset.Resources.DataModels;

namespace Onset.Resources.Messages;

public sealed record SplitControlPointRequestedMessage(ControlPointsContext Context, Guid PointId, long ExpectedRevision);
