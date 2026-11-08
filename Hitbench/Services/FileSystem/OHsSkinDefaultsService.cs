using Hitbench.Core.Entities;
using Hitbench.Core.Enums;
using Hitbench.Resources.DataModels;
using System.IO;

namespace Hitbench.Services.FileSystem;

public sealed class OHsSkinDefaultsService(IAppSettingsService settings, string? userName = null) : IOHsSkinDefaultsService
{
    private readonly SemaphoreSlim mutations = new(1, 1);
    private static readonly HashSet<string> Stems = ControlPoint.SamplesetOptions.SelectMany(set => Enum.GetValues<SampleType>()
        .Select(type => OHsDefaultSamples.GetStem(set, type))).ToHashSet(StringComparer.OrdinalIgnoreCase);
    private static bool IsHitsound(string file) => Stems.Contains(Path.GetFileNameWithoutExtension(file)) &&
        new[] { ".wav", ".ogg", ".mp3" }.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase);

    public async Task<OHsSkinDefaultsSnapshot> RefreshAsync(string projectPath, string currentSkin, bool allowDefaults, bool force, CancellationToken token)
    {
        var configuration = await settings.LoadAsync(token).ConfigureAwait(false);
        var text = await File.ReadAllLinesAsync(Path.Combine(configuration.OsuPath, $"osu!.{userName ?? Environment.UserName}.cfg"), token).ConfigureAwait(false);
        var skin = text.Select(line => line.Trim().TrimStart('\uFEFF')).Select(line => line.Split('=', 2))
            .LastOrDefault(pair => pair.Length == 2 && pair[0].Trim() == "Skin")?[1].Trim()
            ?? throw new InvalidDataException("The osu! configuration has no Skin setting.");
        return await Task.Run(async () =>
        {
            await mutations.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var cache = SamplePaths.ResolveFolder(projectPath, OHsDefaultSamples.FolderName);
                IReadOnlyList<string> List() => Directory.Exists(cache) ? Directory.EnumerateFiles(cache).Where(IsHitsound)
                    .Select(path => SamplePaths.Relative(projectPath, path)).Order(StringComparer.OrdinalIgnoreCase).ToArray() : [];
                if (!allowDefaults || !force && skin == currentSkin && Directory.Exists(cache))
                    return new OHsSkinDefaultsSnapshot(currentSkin, List(), false);
                if (string.IsNullOrWhiteSpace(skin) || skin is "." or ".." || skin.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    throw new InvalidDataException("Invalid skin folder name.");
                var source = Path.Combine(Path.GetFullPath(configuration.OsuPath), "Skins", skin);
                SamplePaths.RejectLinkedPath(source);
                var root = SamplePaths.Root(projectPath); Directory.CreateDirectory(root);
                var stage = Path.Combine(root, ".skin-stage-" + Guid.NewGuid().ToString("N"));
                var backup = Path.Combine(root, ".skin-backup-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(stage);
                try
                {
                    foreach (var file in Directory.EnumerateFiles(source).Where(IsHitsound))
                    {
                        token.ThrowIfCancellationRequested(); SamplePaths.RejectLinkedPath(file);
                        using var input = File.OpenRead(file);
                        using var output = File.Create(Path.Combine(stage, Path.GetFileName(file)));
                        await input.CopyToAsync(output, token).ConfigureAwait(false);
                    }
                    token.ThrowIfCancellationRequested();
                    // No cancellation inside the directory exchange: either restore or publish one complete cache.
                    if (Directory.Exists(cache)) Directory.Move(cache, backup);
                    try { Directory.Move(stage, cache); }
                    catch { if (Directory.Exists(backup)) Directory.Move(backup, cache); throw; }
                    if (Directory.Exists(backup)) Directory.Delete(backup, true);
                    return new OHsSkinDefaultsSnapshot(skin, List(), true);
                }
                finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
            }
            finally { mutations.Release(); }
        }, token).ConfigureAwait(false);
    }
}
