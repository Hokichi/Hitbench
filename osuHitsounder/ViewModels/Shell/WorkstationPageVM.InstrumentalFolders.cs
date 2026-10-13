using osuHitsounder.Core.Entities;

namespace osuHitsounder.ViewModels.Shell;

public partial class WorkstationPageVM
{
    private readonly Dictionary<InstrumentalTrack, string> folderNamesPending = new(ReferenceEqualityComparer.Instance);
    private Task folderWork = Task.CompletedTask;
    private CancellationTokenSource? folderDelay;

    private void QueueInstrumentFolderRename(InstrumentalTrack track, string name)
    {
        folderNamesPending[track] = name;
        folderDelay?.Cancel();
        if (folderWork.IsCompleted) folderWork = RenameInstrumentFoldersAsync();
        OnPropertyChanged(nameof(HasPendingSaves));
    }

    private async Task RenameInstrumentFoldersAsync()
    {
        await Task.Yield();
        while (folderNamesPending.Count > 0)
        {
            folderDelay?.Dispose(); folderDelay = new();
            try { await Task.Delay(250, folderDelay.Token); } catch (OperationCanceledException) { }
            while (instrumentBusy || managingSamples || importBusy || removalBusy) await Task.Delay(25);
            var requests = folderNamesPending.ToArray(); folderNamesPending.Clear();
            foreach (var (track, name) in requests)
            {
                if (!tracks.Contains(track) || Project is null || projectPath is null) continue;
                try
                {
                    var old = track.FolderName;
                    var reserved = tracks.OfType<InstrumentalTrack>().Where(t => t != track).Select(t => t.FolderName).ToArray();
                    var renamed = await sampleFiles!.RenameFolderAsync(projectPath, old, name, reserved, CancellationToken.None);
                    await uiScheduler.InvokeAsync(() =>
                    {
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
                        RefreshAvailableSamples(availableSamples.Select(Rewrite).ToArray(), false);
                        samplesRevision++; OnPropertyChanged(nameof(SamplesRevision));
                        instrumentRevision++; PublishInstrumentSnapshot(); QueueSave(); QueuePlaybackUpdate();
                    }, CancellationToken.None);
                }
                catch (Exception error) { await uiScheduler.InvokeAsync(() => ErrorMessage = "Could not rename instrumental folder: " + error.Message, CancellationToken.None); }
            }
        }
        folderDelay?.Dispose(); folderDelay = null;
        OnPropertyChanged(nameof(HasPendingSaves));
    }

    public async Task SettleInstrumentalFoldersAsync()
    {
        folderDelay?.Cancel();
        await folderWork;
    }
}
