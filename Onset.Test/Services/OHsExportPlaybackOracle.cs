using System.Globalization;
namespace Onset.Test.Services;
public static class OHsExportPlaybackOracle
{
    public static IReadOnlyList<(int Timestamp, int Set, string Type, int Index, int Volume)> Resolve(string osuText)
    {
        var timing = new List<(decimal Time, int Set, int Index, int Volume)>();
        var objects = new List<string[]>(); var section = "";
        foreach (var raw in osuText.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[')) { section = line; continue; }
            if (line.Length == 0 || line.StartsWith("//")) continue;
            var fields = line.Split(',');
            if (section == "[TimingPoints]") timing.Add((decimal.Parse(fields[0], CultureInfo.InvariantCulture), int.Parse(fields[3]), int.Parse(fields[4]), int.Parse(fields[5])));
            if (section == "[HitObjects]") objects.Add(fields);
        }
        var result = new List<(int, int, string, int, int)>();
        foreach (var hit in objects)
        {
            var time = int.Parse(hit[2]); var flags = int.Parse(hit[4]); var sample = hit[5].Split(':');
            var state = timing.Where(p => p.Time <= (decimal)time + 5).DefaultIfEmpty(timing.Count == 0 ? (0m, 1, 0, 100) : timing[0]).Last();
            var normal = int.Parse(sample[0]); if (normal == 0) normal = state.Item2 == 0 ? 1 : state.Item2;
            var addition = int.Parse(sample[1]); if (addition == 0) addition = normal;
            result.Add((time, normal, "normal", state.Item3, state.Item4));
            if ((flags & 2) != 0) result.Add((time, addition, "whistle", state.Item3, state.Item4));
            if ((flags & 4) != 0) result.Add((time, addition, "finish", state.Item3, state.Item4));
            if ((flags & 8) != 0) result.Add((time, addition, "clap", state.Item3, state.Item4));
        }
        return result;
    }
}
