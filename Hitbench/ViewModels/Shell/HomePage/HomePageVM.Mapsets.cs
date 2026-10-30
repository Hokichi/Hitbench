using System.IO;
using CommunityToolkit.Mvvm.Input;
using Hitbench.Resources.DataModels;

namespace Hitbench.ViewModels.Shell.HomePage;

public partial class HomePageVM
{
    private bool isLocatingFolder;
    public string MapsetsDirectory => osuDirectory ?? "";
    public OHsHomeMapsetsAvailability MapsetsAvailability => !active ? OHsHomeMapsetsAvailability.Inactive
        : IsLoadingSongs && songsInventory is null ? OHsHomeMapsetsAvailability.Loading
        : osuDirectory is null ? OHsHomeMapsetsAvailability.NotFound
        : songsInventory is { Count: > 0 } ? OHsHomeMapsetsAvailability.Available : OHsHomeMapsetsAvailability.Empty;
    private bool CanLocateFolder() => picker is not null && CanInteract && !isLocatingFolder && !IsProcessing && !IsLoadingSongs && !IsSearchingSongs && !IsCreatingProject;
    [RelayCommand(CanExecute = nameof(CanLocateFolder))]
    private Task LocateOsuFolderAsync(CancellationToken token) => LocateFolderAsync(true, token);
    [RelayCommand(CanExecute = nameof(CanLocateFolder))]
    private Task LocateMapsetsDirectoryAsync(CancellationToken token) => LocateFolderAsync(false, token);
    private async Task LocateFolderAsync(bool installation, CancellationToken token)
    {
        if (!CanLocateFolder()) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        isLocatingFolder = true;
        RefreshCommands();
        try
        {
            var selected = await picker!.SelectAsync(new SamplePickerContext(null, osuDirectory,
                SampleSelectionMode.Folder, osuDirectory, installation ? OHsFolderSelectionPurpose.OsuInstallation : OHsFolderSelectionPurpose.Mapsets), linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            if (selected.Count == 0) return;
            if (selected.Count != 1 || !Path.IsPathFullyQualified(selected[0])) throw new IOException("Invalid folder selection.");
            if (installation && !await folders.IsInstallationAsync(selected[0], linked.Token)) throw new IOException("osu! not found.");
            var root = installation ? Path.Combine(selected[0], "Songs") : selected[0];
            var snapshot = await folders.ReloadSongsAsync(root, linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            if (snapshot is null)
            {
                if (!installation || songsInventory is not null) throw new DirectoryNotFoundException(root);
                await settings.SaveOsuPathAsync(selected[0], linked.Token);
                linked.Token.ThrowIfCancellationRequested();
                osuDirectory = root;
                UpdateSongsStatus();
                return;
            }
            var filtered = await Task.Run(() => snapshot.FilterSongs(SongSearchText, linked.Token), linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            if (installation) await settings.SaveOsuPathAsync(selected[0], linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            await settings.SaveMapsetsDirectoryAsync(root, linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            if (!active) return;
            songQueryCancellation?.Cancel();
            songsGeneration++;
            osuDirectory = root;
            songsInventory = snapshot;
            songsSnapshot = filtered;
            appliedSongSearchText = SongSearchText;
            Songs.Clear();
            foreach (var entry in filtered.ReadPage(0, InitialSongCount)) Songs.Add(entry);
            if (SelectedMapset is { } current && !snapshot.ReadPage(0, snapshot.Count).Any(x => string.Equals(x.FullName, current.FullName, StringComparison.OrdinalIgnoreCase))) SelectedMapset = null;
            songsInventoryGeneration++;
            OnPropertyChanged(nameof(SongsInventoryGeneration));
            OnPropertyChanged(nameof(MapsetInventoryText));
            OnPropertyChanged(nameof(SongsPresentationGeneration));
            UpdateSongsStatus();
            IsAwaitingSongsPresentation = true;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch (Exception error) { if (active) FailPanel(false, error); }
        finally { isLocatingFolder = false; RefreshCommands(); }
    }
}
