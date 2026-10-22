using Soundie.Resources.DataModels;

namespace Soundie.Resources.Messages;

public sealed record SplitControlPointRequestedMessage(ControlPointsContext Context, Guid PointId, long ExpectedRevision);
