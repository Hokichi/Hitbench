namespace Hitbench.Resources.DataModels;

public sealed record OHsControlPointCurveProjection(Guid PointId, double StartTimestamp, double EndTimestamp,
    double StartVolume, double EndVolume, double? ConnectorFromVolume);
