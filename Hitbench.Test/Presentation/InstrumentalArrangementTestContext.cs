using Hitbench.Resources.DataModels;

namespace Hitbench.Test.Presentation;

public static class InstrumentalArrangementTestContext
{
 public static InstrumentalEditorContext Create(InstrumentalTrackSnapshot track, IReadOnlyList<int> offsets, IReadOnlyList<int> bpms, TimeSpan position, int subdivision, double zoom) => Create(track, TimingTestData.Points(offsets,bpms), position, subdivision, zoom);

    public static InstrumentalEditorContext Create(InstrumentalTrackSnapshot track, IReadOnlyList<Hitbench.Core.Entities.UninheritedPoint> points, TimeSpan position, int subdivision, double zoom) =>
        new(track, new(track.Context, 0, TimeSpan.FromSeconds(10), position, false, true, true,
            points, zoom, Math.Max(0, Array.IndexOf(new[] { 1, 2, 3, 4, 5, 6, 8, 12, 16 }, subdivision)), true, null));
}
