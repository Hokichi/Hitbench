namespace Hitbench.Resources.DataModels;
public sealed class OHsTimingSnapshot(IReadOnlyList<Hitbench.Core.Entities.UninheritedPoint> uninheritedPoints)
{
 public IReadOnlyList<Hitbench.Core.Entities.UninheritedPoint> UninheritedPoints { get; } = Array.AsReadOnly(uninheritedPoints.ToArray());
}
