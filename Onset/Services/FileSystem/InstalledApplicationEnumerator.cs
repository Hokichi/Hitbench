using System.IO;
using System.Security;
using Microsoft.Win32;

namespace Onset.Services.FileSystem;

public static class InstalledApplicationEnumerator
{
    public static IEnumerable<string> Enumerate(CancellationToken token, Action onSkippedLocation)
    {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            token.ThrowIfCancellationRequested();
            using var key = OpenBaseKey(hive, view, onSkippedLocation);
            if (key is null) continue;
            using var root = OpenKey(key, @"Software\Microsoft\Windows\CurrentVersion", onSkippedLocation);
            if (root is null) continue;
            foreach (var directory in Enumerate(root, token, onSkippedLocation))
                yield return directory;
        }
    }

    internal static IEnumerable<string> Enumerate(RegistryKey root, CancellationToken token, Action onSkippedLocation)
    {
        using var uninstall = OpenKey(root, "Uninstall", onSkippedLocation);
        if (uninstall is not null)
        {
            foreach (var name in SubKeyNames(uninstall, onSkippedLocation))
            {
                token.ThrowIfCancellationRequested();
                using var application = OpenKey(uninstall, name, onSkippedLocation);
                if (application is null) continue;
                var location = ReadValue(application, "InstallLocation", onSkippedLocation);
                if (!string.IsNullOrWhiteSpace(location))
                    yield return Environment.ExpandEnvironmentVariables(location.Trim().Trim('"'));
                var icon = ExecutableDirectory(ReadValue(application, "DisplayIcon", onSkippedLocation));
                if (icon is not null) yield return icon;
            }
        }
        using var paths = OpenKey(root, "App Paths", onSkippedLocation);
        if (paths is null) yield break;
        token.ThrowIfCancellationRequested();
        using var registered = OpenKey(paths, "osu!.exe", onSkippedLocation);
        if (registered is null) yield break;
        var directory = ExecutableDirectory(ReadValue(registered, "", onSkippedLocation));
        if (directory is not null) yield return directory;
    }

    private static string? ExecutableDirectory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var path = Environment.ExpandEnvironmentVariables(value.Trim());
        var comma = path.LastIndexOf(',');
        if (comma >= 0 && int.TryParse(path[(comma + 1)..], out _)) path = path[..comma];
        path = path.Trim().Trim('"');
        if (!Path.IsPathFullyQualified(path)) return null;
        var name = Path.GetFileName(path);
        return name.Equals("osu!.exe", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(path) : null;
    }

    private static RegistryKey? OpenBaseKey(RegistryHive hive, RegistryView view, Action skipped)
    {
        try { return RegistryKey.OpenBaseKey(hive, view); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
        { skipped(); return null; }
    }

    private static RegistryKey? OpenKey(RegistryKey root, string path, Action skipped)
    {
        try { return root.OpenSubKey(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
        { skipped(); return null; }
    }

    private static string[] SubKeyNames(RegistryKey root, Action skipped)
    {
        try { return root.GetSubKeyNames(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
        { skipped(); return []; }
    }

    private static string? ReadValue(RegistryKey key, string name, Action skipped)
    {
        try { return key.GetValue(name) as string; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
        { skipped(); return null; }
    }
}
