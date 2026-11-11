using Hitbench.Core.Entities;
namespace Hitbench.Test;
internal static class TimingTestData
{
 public static List<UninheritedPoint> Points(IReadOnlyList<int> offsets,IReadOnlyList<int> bpms) => offsets.Select((o,i)=>new UninheritedPoint {Offset=o,BPM=i<bpms.Count?bpms[i]:0}).ToList();
 public static List<UninheritedPoint> Replace(IReadOnlyList<UninheritedPoint>? points,IReadOnlyList<int> values,bool offsets) => values.Select((v,i)=>new UninheritedPoint {Offset=offsets?v:i<(points?.Count ?? 0)?points![i].Offset:0,BPM=offsets?(i<(points?.Count ?? 0)?points![i].BPM:0):v}).ToList();
}
