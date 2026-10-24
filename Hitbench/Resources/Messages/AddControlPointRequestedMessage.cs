using Hitbench.Resources.DataModels;
namespace Hitbench.Resources.Messages;

public sealed record AddControlPointRequestedMessage(ControlPointsContext Context, Guid PointId,
    long ExpectedRevision, int Timestamp, int Volume, string Sampleset);
