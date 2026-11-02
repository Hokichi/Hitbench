using Hitbench.Resources.DataModels;

namespace Hitbench.Services;

public static partial class OHsExportPlanner
{
    internal static Func<decimal, int> CreateTimingVolumeResolver(OHsExportInput input)
    {
        if (input.ControlPoints.Count > 0)
        {
            var timeline = new ControlPointVolumeTimeline(input.ControlPoints);
            return time => (int)Math.Round(timeline.GetVolumeAtTimestamp(time), MidpointRounding.AwayFromZero);
        }
        var required = input.RequiredPoints.OrderBy(p => p.Timestamp).ThenBy(p => p.Kind).ToArray();
        return time =>
        {
            var low = 0; var high = required.Length;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (required[middle].Timestamp <= time) low = middle + 1; else high = middle;
            }
            return low == 0 ? 100 : required[low - 1].Volume;
        };
    }

    internal static OHsExportTimingPoint[] BuildTimingPoints(OHsExportInput input, IReadOnlyList<int> groupTimes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var volumeAt = CreateTimingVolumeResolver(input);
        var required = input.RequiredPoints.OrderBy(p => p.Timestamp).ThenBy(p => p.Kind).Select(p =>
        {
            token.ThrowIfCancellationRequested();
            return p with { Volume = volumeAt(p.Timestamp) };
        }).ToArray();
        var rows = required.ToList();
        var index = 0;
        OHsExportTimingPoint? preceding = null;
        foreach (var time in groupTimes.Distinct().Order().Select(t => (decimal)t + 5))
        {
            token.ThrowIfCancellationRequested();
            while (index < required.Length && required[index].Timestamp <= time) preceding = required[index++];
            // The timeline builder owns the initial fallback anchor and counts it as an added row.
            if (preceding is null) continue;
            var volume = volumeAt(time);
            if (volume == preceding.Volume) continue;
            preceding = new(time, preceding.Kind == OHsExportTimingKind.SourceRed ? "-100" : preceding.BeatLength,
                preceding.Meter, preceding.SampleSet, input.Options.StartingIndex, volume,
                preceding.Effects, OHsExportTimingKind.BankSwitch);
            rows.Add(preceding);
        }
        return rows.OrderBy(p => p.Timestamp).ThenBy(p => p.Kind).ToArray();
    }
}
