using Hitbench.Resources.DataModels;

namespace Hitbench.Services;

public static partial class OHsExportPlanner
{
    private static OHsExportPlan BuildTimeline(
        (int Time, IReadOnlyList<OHsExportRepresentation> Choices)[] groups,
        OHsExportTimingPoint[] required, int start,
        IReadOnlyList<IReadOnlyDictionary<OHsExportSlot, OHsExportAsset>> layout, CancellationToken token,
        Func<decimal, int> timingVolume)
    {
        var selected = new (int Bank, OHsExportRepresentation Choice)[groups.Length];
        for (var i = 0; i < groups.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var found = false;
            for (var bank = 0; bank < layout.Count; bank++)
            {
                var choice = groups[i].Choices.FirstOrDefault(c => c.Slots.All(p =>
                    layout[bank].TryGetValue(p.Key, out var asset) && asset.Id == p.Value.Id));
                if (choice is null) continue;
                selected[i] = (bank, choice); found = true; break;
            }
            if (!found) throw new InvalidOperationException("The sample layout cannot represent an export event.");
        }
        // Number the first played bank first, so native fallback uses the requested starting index.
        var order = Enumerable.Range(0, layout.Count).OrderBy(b => b == selected[0].Bank ? -1 : b).ToArray();
        var indices = new int[layout.Count];
        for (var i = 0; i < order.Length; i++) indices[order[i]] = i;
        for (var i = 0; i < selected.Length; i++) selected[i].Bank = indices[selected[i].Bank];
        return BuildSelectedTimeline(groups, selected, order.Select(b => layout[b]).ToArray(), required, start, token, timingVolume);
    }

    private static OHsExportPlan BuildSelectedTimeline(
        (int Time, IReadOnlyList<OHsExportRepresentation> Choices)[] groups,
        (int Bank, OHsExportRepresentation Choice)[] selected,
        IReadOnlyList<IReadOnlyDictionary<OHsExportSlot, OHsExportAsset>> layout,
        OHsExportTimingPoint[] required, int start, CancellationToken token, Func<decimal, int>? timingVolume = null)
    {
        var rows = required.ToList();
        var switches = new List<(decimal Time, int Bank)>();
        var added = 0;
        var requiredIndex = 0;
        for (var i = 1; i < groups.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            if (selected[i].Bank == selected[i - 1].Bank) continue;
            var lower = (decimal)groups[i - 1].Time + 5;
            var upper = (decimal)groups[i].Time + 5;
            while (requiredIndex < required.Length && required[requiredIndex].Timestamp <= upper) requiredIndex++;
            var preceding = requiredIndex == 0 ? null : required[requiredIndex - 1];
            var opportunity = preceding is not null && preceding.Timestamp > lower ? preceding : null;
            var time = opportunity?.Timestamp ?? (groups[i].Time > lower ? groups[i].Time : upper);
            switches.Add((time, selected[i].Bank + start));
            if (opportunity is null)
            {
                rows.Add(new(time, preceding is null || preceding.Kind == OHsExportTimingKind.SourceRed ? "-100" : preceding.BeatLength,
                    preceding?.Meter ?? 4, preceding?.SampleSet ?? 1, start, timingVolume?.Invoke(time) ?? preceding?.Volume ?? 100,
                    preceding?.Effects ?? 0, OHsExportTimingKind.BankSwitch));
                added++;
            }
        }
        var scheduled = new List<OHsExportTimingPoint>();
        var change = 0; var bank = start;
        foreach (var row in rows.OrderBy(p => p.Timestamp).ThenBy(p => p.Kind))
        {
            token.ThrowIfCancellationRequested();
            while (change < switches.Count && switches[change].Time <= row.Timestamp) bank = switches[change++].Bank;
            scheduled.Add(row with { SampleIndex = bank });
        }
        // osu! uses the first row before its timestamp too. Keep later state off an earlier hit.
        var first = scheduled.FirstOrDefault();
        var firstVolume = timingVolume?.Invoke((decimal)groups[0].Time + 5) ?? 100;
        if (first is null || first.Timestamp > (decimal)groups[0].Time + 5 &&
            (first.SampleIndex != start || first.SampleSet != 1 || first.Volume != firstVolume))
        {
            scheduled.Insert(0, new(groups[0].Time, "-100", 4, 1, start, firstVolume, 0, OHsExportTimingKind.BankSwitch));
            added++;
        }
        var circles = groups.Select((g, i) => new OHsExportCircle(g.Time, selected[i].Choice.NormalSet,
            selected[i].Choice.AdditionSet, selected[i].Choice.HitSound, selected[i].Bank + start)).ToArray();
        return new(circles, scheduled, layout.Select((b, i) => (b, i)).ToDictionary(p => p.i + start, p => p.b), added);
    }
}
