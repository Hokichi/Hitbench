using Onset.Resources.DataModels;
namespace Onset.Resources.Messages;

public sealed record DeleteControlPointRequestedMessage(ControlPointsContext Context, Guid PointId);
