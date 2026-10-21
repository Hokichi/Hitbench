using System.Globalization;
using System.IO;
using System.Text;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Services.FileSystem;
namespace osuHitsounder.Services;

public static class OHsExportBeatmapWriter
{
    public static string Write(OHsExportInput input, OHsExportPlan plan, string audioFileName)
    {
        input.Options.Validate();
        if (SampleFileNames.GetFileNameError(audioFileName) is not null) throw new ArgumentException("Invalid exported song filename.");
        if (plan.Circles.Select(c => c.Timestamp).Distinct().Count() != plan.Circles.Count) throw new InvalidDataException("Export has simultaneous duplicate circles.");
        var text = new StringBuilder("osu file format v14\r\n");
        void Section(string name, IEnumerable<string> lines)
        { text.Append("\r\n[").Append(name).Append("]\r\n"); foreach (var line in lines) text.Append(line).Append("\r\n"); }
        IEnumerable<string> Settings(string section, IReadOnlyDictionary<string, string> overrides)
        {
            var source = input.Beatmap.Sections.GetValueOrDefault(section) ?? [];
            foreach (var line in source)
            {
                var colon = line.IndexOf(':');
                if (colon >= 0 && overrides.Keys.Any(k => k.Equals(line[..colon].Trim(), StringComparison.OrdinalIgnoreCase))) continue;
                if (!string.IsNullOrWhiteSpace(line)) yield return line;
            }
            foreach (var pair in overrides) yield return pair.Key + ":" + pair.Value;
        }
        Section("General", Settings("General", new Dictionary<string, string> { ["AudioFilename"] = audioFileName, ["Mode"] = "0", ["StackLeniency"] = "0" }));
        Section("Editor", Settings("Editor", new Dictionary<string, string>()));
        Section("Metadata", Settings("Metadata", new Dictionary<string, string> { ["Version"] = input.Options.DiffName, ["BeatmapID"] = "0" }));
        Section("Difficulty", Settings("Difficulty", new Dictionary<string, string>()));
        var breaks = new List<string>();
        var circleTimes = plan.Circles.Select(c => c.Timestamp).Order().ToArray();
        bool HasCircle(decimal begin, decimal end)
        {
            var low = 0; var high = circleTimes.Length;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (circleTimes[middle] < begin) low = middle + 1; else high = middle;
            }
            return low < circleTimes.Length && circleTimes[low] <= end;
        }
        foreach (var line in input.Beatmap.Sections.GetValueOrDefault("Events") ?? [])
        {
            var fields = line.Trim().Split(',');
            if (fields.Length >= 3 && fields[0] is "2" or "Break" &&
                decimal.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var begin) &&
                decimal.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var end) && end > begin &&
                !HasCircle(begin, end)) breaks.Add(line.Trim());
        }
        Section("Events", breaks);
        Section("TimingPoints", plan.TimingPoints.OrderBy(p => p.Timestamp).ThenBy(p => p.Kind).Select(p =>
            FormattableString.Invariant($"{p.Timestamp},{p.BeatLength},{p.Meter},{p.SampleSet},{p.SampleIndex},{p.Volume},{(p.Kind == OHsExportTimingKind.SourceRed ? 1 : 0)},{p.Effects}")));
        if (input.Beatmap.Sections.TryGetValue("Colours", out var colours)) Section("Colours", colours.Where(l => !string.IsNullOrWhiteSpace(l)));
        Section("HitObjects", plan.Circles.OrderBy(c => c.Timestamp).Select(c =>
        {
            var normal = Array.IndexOf(ControlPoint.SamplesetOptions.ToArray(), c.NormalSet) + 1;
            var addition = Array.IndexOf(ControlPoint.SamplesetOptions.ToArray(), c.AdditionSet) + 1;
            if (normal == 0 || addition == 0 || !plan.Banks.TryGetValue(c.SampleIndex, out var bank) ||
                !bank.ContainsKey(new(c.NormalSet, Core.Enums.SampleType.Normal))) throw new InvalidDataException("Unresolved export circle.");
            return FormattableString.Invariant($"256,192,{c.Timestamp},1,{c.HitSound},{normal}:{addition}:0:0:");
        }));
        return text.ToString();
    }
}
