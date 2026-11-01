using Hitbench.Core.Entities;

namespace Hitbench.ViewModels.Shell;

public partial class WorkstationPageVM
{
    private readonly Dictionary<InstrumentalTrack, string> folderNamesPending = new(ReferenceEqualityComparer.Instance);
    private Task folderWork = Task.CompletedTask;
    private bool folderRenamesActive;
    private bool flushFolderRenames;
    private CancellationTokenSource? folderDelay;

    private void QueueInstrumentFolderRename(InstrumentalTrack track, string name)
    {
        folderNamesPending[track] = name;
        folderDelay?.Cancel();
        if (!folderRenamesActive) { folderRenamesActive = true; folderWork = RenameInstrumentFoldersAsync(); }
        NotifyInstrumentState();
    }

    private async Task RenameInstrumentFoldersAsync()
    {
        await Task.Yield();
        while (folderNamesPending.Count > 0)
        {
            folderDelay?.Dispose(); folderDelay = new();
            if (!flushFolderRenames)
            {
                try { await (folderRenameDelay ?? Task.Delay)(TimeSpan.FromMilliseconds(250), folderDelay.Token); }
                catch (OperationCanceledException) when (folderDelay.IsCancellationRequested) { if (!flushFolderRenames) continue; }
            }
            while (instrumentBusy || managingSamples || importBusy || removalBusy) await Task.Delay(25);
            var requests = folderNamesPending.ToArray(); folderNamesPending.Clear();
            foreach (var (track, name) in requests)
            {
                await RenameInstrumentFolderAsync(track, name);
            }
        }
        folderDelay?.Dispose(); folderDelay = null;
        folderRenamesActive = false;
        NotifyInstrumentState();
    }

    private async Task<bool> RenameInstrumentFolderAsync(InstrumentalTrack track, string name, Hitbench.Resources.DataModels.OHsTrackAppearanceResult? appearance = null, Func<bool>? cancelled = null)
    {
        if (!tracks.Contains(track) || Project is null || projectPath is null) return false;
        var applied = false;
        var project = Project; var path = projectPath; var generation = loadGeneration;
        try
        {
            var old = track.FolderName;
            var reserved = tracks.OfType<InstrumentalTrack>().Where(t => t != track).Select(t => t.FolderName).ToArray();
            var renamed = await sampleFiles!.RenameFolderAsync(path, old, name, reserved, CancellationToken.None);
            await uiScheduler.InvokeAsync(() =>
            {
                if (cancelled?.Invoke() == true || deactivated || generation != loadGeneration || !ReferenceEquals(project, Project) || !tracks.Contains(track)) return;
                string Rewrite(string file) => file.StartsWith(old + "/", StringComparison.OrdinalIgnoreCase) ? renamed + file[old.Length..] : file;
                foreach (var item in tracks)
                {
                    if (item is InstrumentalTrack instrument)
                        foreach (var key in instrument.KeyFiles.Keys.ToArray()) instrument.KeyFiles[key] = Rewrite(instrument.KeyFiles[key]);
                    if (item.SelectedSampleFileName is { } selected) item.SelectedSampleFileName = Rewrite(selected);
                    foreach (var sample in item.Samples) sample.FileName = Rewrite(sample.FileName);
                }
                history.RenameSampleFolder(old, renamed);
                track.Name = name; track.FolderName = renamed;
                if (appearance is not null) track.Color = appearance.Color;
                applied = true;
                NotifyTrackPresentation();
                RefreshAvailableSamples(availableSamples.Select(Rewrite).ToArray(), false);
                samplesRevision++; OnPropertyChanged(nameof(SamplesRevision));
                instrumentRevision++; PublishInstrumentSnapshot(); QueueSave(); QueuePlaybackUpdate();
            }, CancellationToken.None);
        }
        catch (Exception error) { await uiScheduler.InvokeAsync(() =>
        { if (!deactivated && generation == loadGeneration && ReferenceEquals(project, Project)) ErrorMessage = "Could not rename instrumental folder: " + error.Message; }, CancellationToken.None); }
        return applied;
    }

    public async Task SettleInstrumentalFoldersAsync()
    {
        flushFolderRenames = true;
        folderDelay?.Cancel();
        try { await folderWork; }
        finally { flushFolderRenames = false; }
    }
}
