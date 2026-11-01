using Hitbench.Resources.DataModels;

namespace Hitbench.Services;

public static partial class OHsExportPlanner
{
    internal static OHsExportPlan BuildSelectedTimeline(
        (int Time, IReadOnlyList<OHsExportRepresentation> Choices)[] groups,
        (int Bank, OHsExportRepresentation Choice)[] selected,
        IReadOnlyList<IReadOnlyDictionary<OHsExportSlot, OHsExportAsset>> layout,
        OHsExportTimingPoint[] required, int start, CancellationToken token)
    {
        var firstVolume = selected[0].Choice.NativeVolume;
        var rows = required.ToList();
        var switches = new List<(decimal Time, int Bank, int Volume)>();
        var added = 0;
        for (var i = 1; i < groups.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            if (selected[i].Bank == selected[i - 1].Bank && selected[i].Choice.NativeVolume == selected[i - 1].Choice.NativeVolume) continue;
            var lower = (decimal)groups[i - 1].Time + 5;
            var upper = (decimal)groups[i].Time + 5;
            var preceding = required.LastOrDefault(p => p.Timestamp <= upper);
            var opportunity = preceding is not null && preceding.Timestamp > lower ? preceding : null;
            var time = opportunity?.Timestamp ?? (groups[i].Time > lower ? groups[i].Time : upper);
            switches.Add((time, selected[i].Bank + start, selected[i].Choice.NativeVolume));
            if (opportunity is null)
            {
                rows.Add(new(time, preceding is null || preceding.Kind == OHsExportTimingKind.SourceRed ? "-100" : preceding.BeatLength,
                    preceding?.Meter ?? 4, preceding?.SampleSet ?? 1, start, firstVolume, preceding?.Effects ?? 0, OHsExportTimingKind.BankSwitch));
                added++;
            }
        }
        if (rows.Count == 0)
        { rows.Add(new(groups[0].Time, "-100", 4, 1, start, firstVolume, 0, OHsExportTimingKind.BankSwitch)); added++; }
        var scheduled = new List<OHsExportTimingPoint>();
        var change = 0; var bank = start; var volume = firstVolume;
        foreach (var row in rows.OrderBy(p => p.Timestamp).ThenBy(p => p.Kind))
        {
            while (change < switches.Count && switches[change].Time <= row.Timestamp)
            { bank = switches[change].Bank; volume = switches[change++].Volume; }
            scheduled.Add(row with { SampleIndex = bank, Volume = volume });
        }
        // A future first row must not apply a later bank/gain to the first event.
        if (scheduled[0].Timestamp > (decimal)groups[0].Time + 5 &&
            (scheduled[0].SampleIndex != start || scheduled[0].Volume != firstVolume))
        { scheduled.Insert(0, new(groups[0].Time, "-100", 4, 1, start, firstVolume, 0, OHsExportTimingKind.BankSwitch)); added++; }
        var circles = groups.Select((g, i) => new OHsExportCircle(g.Time, selected[i].Choice.NormalSet,
            selected[i].Choice.AdditionSet, selected[i].Choice.HitSound, selected[i].Bank + start)).ToArray();
        return new(circles, scheduled, layout.Select((b, i) => (b, i)).ToDictionary(p => p.i + start, p => p.b), added);
    }

    private static OHsExportPlan BuildTimeline(
        (int Time, IReadOnlyList<OHsExportRepresentation> Choices)[] groups,
        OHsExportTimingPoint[] required, int start,
        IReadOnlyList<IReadOnlyDictionary<OHsExportSlot, OHsExportAsset>> layout, CancellationToken token)
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
                selected[i] = (bank, choice);
                found = true;
                break;
            }
            if (!found) throw new InvalidOperationException("The sample layout cannot represent an export event.");
        }

        // osu! also uses the first timing row before its timestamp. Give the first
        // played bank the starting index before constructing the native timeline.
        var indices = new int[layout.Count];
        var next = start + 1;
        for (var bank = 0; bank < layout.Count; bank++) indices[bank] = bank == selected[0].Bank ? start : next++;
        var banks = Enumerable.Range(0, layout.Count).OrderBy(b => indices[b]).ToDictionary(b => indices[b], b => layout[b]);
        var circles = new List<OHsExportCircle>(groups.Length);
        var rows = required.ToList();
        var switches = new List<(decimal Time, int Bank)>();
        var active = start;
        var requiredIndex = 0;
        var added = 0;
        for (var i = 0; i < groups.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var bank = indices[selected[i].Bank];
            if (bank != active)
            {
                var lower = (decimal)groups[i - 1].Time + 5;
                var upper = (decimal)groups[i].Time + 5;
                while (requiredIndex < required.Length && required[requiredIndex].Timestamp <= upper) requiredIndex++;
                var preceding = requiredIndex == 0 ? null : required[requiredIndex - 1];
                var opportunity = preceding is not null && preceding.Timestamp > lower ? preceding : null;
                var time = opportunity?.Timestamp ?? (groups[i].Time > lower ? groups[i].Time : upper);
                switches.Add((time, bank));
                if (opportunity is null)
                {
                    rows.Add(new(time, preceding is null || preceding.Kind == OHsExportTimingKind.SourceRed ? "-100" : preceding.BeatLength,
                        preceding?.Meter ?? 4, preceding?.SampleSet ?? 1, bank, preceding?.Volume ?? 100,
                        preceding?.Effects ?? 0, OHsExportTimingKind.BankSwitch));
                    added++;
                }
                active = bank;
            }
            var choice = selected[i].Choice;
            circles.Add(new(groups[i].Time, choice.NormalSet, choice.AdditionSet, choice.HitSound, bank));
        }

        var scheduled = new List<OHsExportTimingPoint>(rows.Count + 1);
        var change = 0;
        active = start;
        foreach (var row in rows.OrderBy(p => p.Timestamp).ThenBy(p => p.Kind))
        {
            token.ThrowIfCancellationRequested();
            while (change < switches.Count && switches[change].Time <= row.Timestamp) active = switches[change++].Bank;
            scheduled.Add(row with { SampleIndex = active });
        }
        var first = scheduled.FirstOrDefault();
        if (first is null || first.Timestamp > (decimal)groups[0].Time + 5 &&
            (first.SampleIndex != start || first.SampleSet != 1 || first.Volume != 100))
        {
            scheduled.Insert(0, new(groups[0].Time, "-100", 4, 1, start, 100, 0, OHsExportTimingKind.BankSwitch));
            added++;
        }
        token.ThrowIfCancellationRequested();
        return new(circles, scheduled, banks, added);
    }
}
