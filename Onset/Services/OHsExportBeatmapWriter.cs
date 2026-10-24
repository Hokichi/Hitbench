using System.IO;
using System.Text.RegularExpressions;
using Onset.Core.Entities;
using Onset.Resources.DataModels;
using Onset.Services.FileSystem;
namespace Onset.Services;

public static class OHsExportBeatmapWriter
{
    private static string ReadTemplate()
    {
        using var stream = typeof(OHsExportBeatmapWriter).Assembly.GetManifestResourceStream("Onset.Resources.OHsExportTemplate.osu")
            ?? throw new InvalidDataException("Export template resource is missing.");
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();
        var map = OHsExportBeatmap.Parse(text);
        var audio = (map.Sections.GetValueOrDefault("General") ?? [])
            .Select(line => line.Split(':', 2))
            .Where(parts => parts.Length == 2 && parts[0].Trim().Equals("AudioFilename", StringComparison.OrdinalIgnoreCase))
            .Select(parts => parts[1].Trim().Trim('"')).ToArray();
        if (audio.Length != 1 || SamplePaths.GetRelativePathError(audio[0]) is not null)
            throw new InvalidDataException("Export template has an invalid audio reference.");
        return text;
    }

    private static string ReplaceSectionBodies(string template, string timingRows, string objectRows)
    {
        var pattern = new Regex(@"^(\[(?<section>TimingPoints|HitObjects)\][ \t]*\r?\n)[\s\S]*?(?=^\[|\z)", RegexOptions.Multiline);
        var matches = pattern.Matches(template);
        if (matches.Count != 2 || matches.Select(m => m.Groups["section"].Value).Distinct().Count() != 2)
            throw new InvalidDataException("Export template must contain one timing and one hit-object section.");
        return pattern.Replace(template, m => m.Groups[1].Value + (m.Groups["section"].Value == "TimingPoints" ? timingRows : objectRows));
    }

    public static string Write(OHsExportInput input, OHsExportPlan plan)
    {
        input.Options.Validate();
        if (plan.Circles.Select(c => c.Timestamp).Distinct().Count() != plan.Circles.Count)
            throw new InvalidDataException("Export has simultaneous duplicate circles.");
        var template = ReadTemplate();
        var newline = template.Contains("\r\n") ? "\r\n" : "\n";
        var timing = plan.TimingPoints.OrderBy(p => p.Timestamp).ThenBy(p => p.Kind).Select(p =>
            FormattableString.Invariant($"{p.Timestamp},{p.BeatLength},{p.Meter},{p.SampleSet},{p.SampleIndex},{p.Volume},{(p.Kind == OHsExportTimingKind.SourceRed ? 1 : 0)},{p.Effects}"));
        var objects = plan.Circles.OrderBy(c => c.Timestamp).Select(c =>
        {
            var normal = Array.IndexOf(ControlPoint.SamplesetOptions.ToArray(), c.NormalSet) + 1;
            var addition = Array.IndexOf(ControlPoint.SamplesetOptions.ToArray(), c.AdditionSet) + 1;
            if (normal == 0 || addition == 0 || !plan.Banks.TryGetValue(c.SampleIndex, out var bank) ||
                !bank.ContainsKey(new(c.NormalSet, Core.Enums.SampleType.Normal))) throw new InvalidDataException("Unresolved export circle.");
            return FormattableString.Invariant($"256,192,{c.Timestamp},5,{c.HitSound},{normal}:{addition}:0:0:");
        });
        string Rows(IEnumerable<string> rows) => string.Concat(rows.Select(row => row + newline));
        return ReplaceSectionBodies(template, Rows(timing) + newline, Rows(objects));
    }
}
