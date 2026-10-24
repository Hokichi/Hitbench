using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
namespace Hitbench.Resources.DataModels;

public sealed class OHsExportBeatmap
{
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Sections { get; }
    public IReadOnlyList<OHsExportTimingPoint> TimingPoints { get; }
    private OHsExportBeatmap(Dictionary<string, List<string>> sections, List<OHsExportTimingPoint> timing)
    {
        Sections = new ReadOnlyDictionary<string, IReadOnlyList<string>>(sections.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)Array.AsReadOnly(p.Value.ToArray()), StringComparer.OrdinalIgnoreCase));
        TimingPoints = Array.AsReadOnly(timing.OrderBy(p => p.Timestamp).ThenBy(p => p.Kind).ToArray());
    }
    public static OHsExportBeatmap Parse(string text)
    {
        var sections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var timing = new List<OHsExportTimingPoint>();
        string? section = null;
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } raw)
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1];
                if (!sections.TryAdd(section, [])) throw new InvalidDataException("Duplicate beatmap section.");
                continue;
            }
            if (section is null) continue;
            sections[section].Add(raw);
            if (!section.Equals("TimingPoints", StringComparison.OrdinalIgnoreCase) || line.Length == 0 || line.StartsWith("//")) continue;
            var f = line.Split(',');
            if (f.Length != 8 || !decimal.TryParse(f[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var time) ||
                !decimal.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var length) || length == 0 ||
                !int.TryParse(f[2], out var meter) || meter < 1 || !int.TryParse(f[3], out var set) || set is < 0 or > 3 ||
                !int.TryParse(f[4], out var index) || index < 0 || !int.TryParse(f[5], out var volume) || volume is < 0 or > 100 ||
                !int.TryParse(f[6], out var red) || red is < 0 or > 1 || !int.TryParse(f[7], out var effects) ||
                red == 1 && length <= 0 || red == 0 && length >= 0)
                throw new InvalidDataException("Invalid source timing point.");
            timing.Add(new(time, f[1].Trim(), meter, set, index, volume, effects, red == 1 ? OHsExportTimingKind.SourceRed : OHsExportTimingKind.SourceState));
        }
        if (timing.Count == 0 || !timing.Any(p => p.Kind == OHsExportTimingKind.SourceRed)) throw new InvalidDataException("Source difficulty has no timing definition.");
        return new(sections, timing);
    }
}
