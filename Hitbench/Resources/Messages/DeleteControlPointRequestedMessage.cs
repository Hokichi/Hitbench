using Hitbench.Resources.DataModels;
namespace Hitbench.Resources.Messages;

public sealed record DeleteControlPointRequestedMessage(ControlPointsContext Context, Guid PointId);
