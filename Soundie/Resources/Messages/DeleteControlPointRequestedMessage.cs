using Soundie.Resources.DataModels;
namespace Soundie.Resources.Messages;

public sealed record DeleteControlPointRequestedMessage(ControlPointsContext Context, Guid PointId);
