using System.ComponentModel.DataAnnotations;
using System.IO;

namespace Hitbench.Services.FileSystem;

public static class SampleFileNames
{
    public static string GetUniqueCopyName(string fileName, long suffix)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(suffix, 1);
        if (suffix == 1) return fileName;
        var extension = Path.GetExtension(fileName);
        var ending = " (" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
        var available = Math.Min(251, 255 - extension.Length) - ending.Length;
        if (available < 1) throw new ArgumentException("The filename is too long.", nameof(fileName));
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var length = Math.Min(stem.Length, available);
        if (length > 0 && char.IsHighSurrogate(stem[length - 1])) length--;
        var shortened = stem[..length].TrimEnd(' ', '.') + ending;
        if (GetStemError(shortened) is { } error) throw new ArgumentException(error, nameof(fileName));
        return shortened + extension;
    }

    public static string? GetStemError(string stem)
    {
        if (string.IsNullOrWhiteSpace(stem)) return "Enter a filename.";
        if (stem.Length > 251) return "The filename is too long.";
        if (stem.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || stem.Contains('/') || stem.Contains('\\'))
            return "Use a filename without path separators or invalid characters.";
        if (stem.EndsWith(' ') || stem.EndsWith('.')) return "Remove the trailing space or dot.";
        var device = stem.Split('.')[0].ToUpperInvariant();
        if (device is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
            device.Length == 4 && (device.StartsWith("COM") || device.StartsWith("LPT")) && "123456789¹²³".Contains(device[3]))
            return "This filename is reserved by Windows.";
        return null;
    }
    public static string? GetFileNameError(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        if (!new[] { ".wav", ".ogg", ".mp3" }.Contains(extension, StringComparer.OrdinalIgnoreCase))
            return "Select a WAV, OGG, or MP3 filename.";
        return GetStemError(Path.GetFileNameWithoutExtension(fileName)) ??
            (Path.GetFileName(fileName) != fileName ? "Use a filename without a path." : null);
    }
    public static ValidationResult? ValidateStem(string stem, ValidationContext context) =>
        GetStemError(stem) is { } error ? new ValidationResult(error) : ValidationResult.Success;
}
