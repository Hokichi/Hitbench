using Hitbench.Core.Entities;
using Hitbench.Core.Enums;
using System.IO;

namespace Hitbench.Services;

public static class OHsDefaultSamples
{
    public const string FolderName = "SkinDefault";
    private static readonly string[] Extensions = [".wav", ".ogg", ".mp3"];

    public static string GetStem(string sampleset, SampleType type)
    {
        if (!ControlPoint.SamplesetOptions.Contains(sampleset) || !Enum.IsDefined(type))
            throw new ArgumentException("Invalid skin hitsound family or type.");
        return $"{sampleset.ToLowerInvariant()}-hit{type.ToString().ToLowerInvariant()}";
    }

    public static string GetSampleset(ProjectData project, Track track, decimal timestamp)
    {
        if (track.Addition != "None") return track.Addition;
        return project.ControlPoints.Where(p => !p.IsDisabled && p.Timestamp <= timestamp)
            .OrderBy(p => p.Timestamp).LastOrDefault()?.Sampleset ?? "Normal";
    }

    public static string? Resolve(IReadOnlyCollection<string> files, string sampleset, SampleType type)
    {
        var stem = GetStem(sampleset, type);
        foreach (var extension in Extensions)
        {
            var expected = FolderName + "/" + stem + extension;
            var found = files.FirstOrDefault(file => string.Equals(file.Replace('\\', '/'), expected, StringComparison.OrdinalIgnoreCase));
            if (found is not null) return found;
        }
        return null;
    }

    public static bool IsAvailable(ProjectData project, Track track, IReadOnlyCollection<string> files,
        double durationMilliseconds = double.PositiveInfinity)
    {
        if (!project.AllowDefaults || !track.UsesDefaultSample || track is InstrumentalTrack) return false;
        var sets = new HashSet<string> { GetSampleset(project, track, 5) };
        if (track.Addition == "None")
            foreach (var point in project.ControlPoints.Where(p => !p.IsDisabled && p.Timestamp > 5 && p.Timestamp < durationMilliseconds)
                         .GroupBy(p => p.Timestamp).Select(g => g.Last())) sets.Add(point.Sampleset);
        return sets.All(set => Resolve(files, set, track.SampleType) is not null);
    }
}
