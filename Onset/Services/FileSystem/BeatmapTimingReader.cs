using System.Globalization;
using System.IO;
using Onset.Core.Entities;

namespace Onset.Services.FileSystem;

public static class BeatmapTimingReader
{
    public static string FindBeatmap(string songFolder) =>
        Directory.EnumerateFiles(songFolder, "*.osu", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .FirstOrDefault() ?? throw new FileNotFoundException("No .osu beatmap was found in the song folder.");

    public static void Populate(ProjectData project, string beatmapPath, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(project);
        var offsets = new List<int>();
        var bpms = new List<int>();
        var inTimingPoints = false;
        var lineNumber = 0;

        foreach (var rawLine in File.ReadLines(beatmapPath))
        {
            token.ThrowIfCancellationRequested();
            lineNumber++;
            var line = rawLine.Trim().TrimStart('\uFEFF').Trim();
            if (line.StartsWith('['))
            {
                inTimingPoints = string.Equals(line, "[TimingPoints]", StringComparison.Ordinal);
                continue;
            }
            if (!inTimingPoints || line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal)) continue;

            var fields = line.Split(',');
            if (fields.Length != 8) throw InvalidRow(beatmapPath, lineNumber, "expected 8 comma-separated fields");
            if (!int.TryParse(fields[6].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var uninherited) || uninherited is not (0 or 1))
                throw InvalidRow(beatmapPath, lineNumber, "uninherited must be 0 or 1");
            if (uninherited == 0) continue;

            if (!double.TryParse(fields[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var time) || !double.IsFinite(time))
                throw InvalidRow(beatmapPath, lineNumber, "time must be a finite number");
            if (!double.TryParse(fields[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var beatLength) || !double.IsFinite(beatLength) || beatLength <= 0)
                throw InvalidRow(beatmapPath, lineNumber, "beatLength must be a positive finite number");

            var roundedOffset = Math.Round(time, MidpointRounding.AwayFromZero);
            var roundedBpm = Math.Round(60000d / beatLength, MidpointRounding.AwayFromZero);
            if (roundedOffset < int.MinValue || roundedOffset > int.MaxValue ||
                !double.IsFinite(roundedBpm) || roundedBpm < int.MinValue || roundedBpm > int.MaxValue)
                throw InvalidRow(beatmapPath, lineNumber, "rounded timing values must fit in Int32");
            offsets.Add(checked((int)roundedOffset));
            bpms.Add(checked((int)roundedBpm));
        }

        project.Offsets = offsets;
        project.BPMs = bpms;
    }

    private static InvalidDataException InvalidRow(string path, int lineNumber, string reason) =>
        new($"Invalid timing point in '{Path.GetFileName(path)}' at line {lineNumber}: {reason}.");
}
