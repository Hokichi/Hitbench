using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Hitbench.Resources.DataModels;

public sealed record OHsReleaseVersion(string Tag, Version NumericVersion, string Description)
{
    public static bool TryParse(string? tag, [NotNullWhen(true)] out OHsReleaseVersion? version)
    {
        version = null;
        if (tag is null || tag.Length > 128 || tag.Contains("..", StringComparison.Ordinal) ||
            tag.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)) return false;
        var match = Regex.Match(tag, @"\A(\d+)\.(\d+)\.(\d+)(?:[-._]?([A-Za-z][A-Za-z0-9]*)(?:[._-][A-Za-z0-9]+)*)?\z", RegexOptions.CultureInvariant);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var major) ||
            !int.TryParse(match.Groups[2].Value, out var minor) || !int.TryParse(match.Groups[3].Value, out var patch)) return false;
        version = new(tag, new(major, minor, patch), match.Groups[4].Success ? match.Groups[4].Value.ToLowerInvariant() : "official");
        return true;
    }
}
