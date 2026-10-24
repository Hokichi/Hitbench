using Hitbench.Resources.DataModels;

namespace Hitbench.Resources.Messages;

public sealed record SplitControlPointRequestedMessage(ControlPointsContext Context, Guid PointId, long ExpectedRevision);
