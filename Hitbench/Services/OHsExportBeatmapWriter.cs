using System.IO;
using System.Text.RegularExpressions;
using Hitbench.Core.Entities;
using Hitbench.Resources.DataModels;
namespace Hitbench.Services;

public static class OHsExportBeatmapWriter
{
    private static string ReplaceSection(string text, string section, Func<string, string> replace, string newline)
    {
        var pattern = new Regex(@"^(?<header>[ \t]*\[" + Regex.Escape(section) + @"\][ \t]*)(?<end>\r?\n|\z)(?<body>[\s\S]*?)(?=^[ \t]*\[|\z)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (!pattern.IsMatch(text))
            return text + (text.EndsWith('\n') ? "" : newline) + "[" + section + "]" + newline + replace("");
        return pattern.Replace(text, match => match.Groups["header"].Value +
            (match.Groups["end"].Length == 0 ? newline : match.Groups["end"].Value) + replace(match.Groups["body"].Value));
    }

    private static string ReplaceValue(string body, string key, string value, string newline)
    {
        var pattern = new Regex(@"^(?<prefix>[ \t]*" + Regex.Escape(key) + @"[ \t]*:[ \t]*)[^\r\n]*?(?<suffix>[ \t]*)(?=\r?$)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        return pattern.IsMatch(body)
            ? pattern.Replace(body, match => match.Groups["prefix"].Value + value + match.Groups["suffix"].Value)
            : body + (body.Length == 0 || body.EndsWith('\n') ? "" : newline) + key + ":" + value + newline;
    }

    public static string Write(OHsExportInput input, OHsExportPlan plan)
    {
        input.Options.Validate();
        if (plan.Circles.Select(c => c.Timestamp).Distinct().Count() != plan.Circles.Count)
            throw new InvalidDataException("Export has simultaneous duplicate circles.");
        var template = input.Beatmap.OriginalText;
        var newline = Regex.Match(template, @"\r?\n") is { Success: true } lineEnd ? lineEnd.Value : "\n";
        var timing = plan.TimingPoints.OrderBy(p => p.Timestamp).ThenBy(p => p.Kind).Select(p =>
            FormattableString.Invariant($"{p.Timestamp},{p.BeatLength},{p.Meter},{p.SampleSet},{p.SampleIndex},{p.Volume},{(p.Kind == OHsExportTimingKind.SourceRed ? 1 : 0)},{p.Effects}"));
        var objects = plan.Circles.OrderBy(c => c.Timestamp).Select(c =>
        {
            var normal = Array.IndexOf(ControlPoint.SamplesetOptions.ToArray(), c.NormalSet) + 1;
            var addition = Array.IndexOf(ControlPoint.SamplesetOptions.ToArray(), c.AdditionSet) + 1;
            if (normal == 0 || addition == 0 || c.Volume is < 0 or > 100 || c.SampleIndex < 0 || c.SampleIndex != 0 && (!plan.Banks.TryGetValue(c.SampleIndex, out var bank) ||
                !bank.ContainsKey(new(c.NormalSet, Core.Enums.SampleType.Normal)))) throw new InvalidDataException("Unresolved export circle.");
            return FormattableString.Invariant($"256,192,{c.Timestamp},5,{c.HitSound},{normal}:{addition}:0:{c.Volume}:");
        });
        string Rows(IEnumerable<string> rows) => string.Concat(rows.Select(row => row + newline));
        template = ReplaceSection(template, "TimingPoints", _ => Rows(timing) + newline, newline);
        template = ReplaceSection(template, "HitObjects", _ => Rows(objects), newline);
        template = ReplaceSection(template, "Metadata", body => ReplaceValue(body, "Version", input.Options.DiffName, newline), newline);
        return ReplaceSection(template, "Difficulty", body =>
        {
            foreach (var (key, value) in new[] { ("HPDrainRate", "3"), ("CircleSize", "3"), ("OverallDifficulty", "5"), ("ApproachRate", "9.7") })
                body = ReplaceValue(body, key, value, newline);
            return body;
        }, newline);
    }
}
