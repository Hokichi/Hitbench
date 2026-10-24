using System.IO;
using System.Text;

namespace Onset.Services.ProjectFiles;

public static class ProjectNameRules
{
    private static readonly HashSet<char> InvalidCharacters = [.. Path.GetInvalidFileNameChars(), '/', '\\'];

    public static string RemoveInvalidCharacters(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var character in value)
            if (!InvalidCharacters.Contains(character)) result.Append(character);
        return result.ToString();
    }

    public static string? GetSyntaxError(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Enter a project name.";
        if (value.Any(InvalidCharacters.Contains)) return "Project name contains invalid characters.";
        if (value.EndsWith('.') || value.EndsWith(' ')) return "Project name cannot end with a dot or space.";

        var stem = value.Split('.', 2)[0];
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            IsReservedNumberedName(stem, "COM") ||
            IsReservedNumberedName(stem, "LPT"))
            return "Project name is reserved by Windows.";

        return null;
    }

    private static bool IsReservedNumberedName(string value, string prefix) =>
        value.Length == 4 && value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
        value[3] is >= '1' and <= '9';
}
