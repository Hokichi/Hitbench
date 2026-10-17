using osuHitsounder.Resources.DataModels;
namespace osuHitsounder.Resources.Messages;

public sealed record AddControlPointRequestedMessage(ControlPointsContext Context, Guid PointId,
    long ExpectedRevision, int Timestamp, int Volume, string Sampleset);
