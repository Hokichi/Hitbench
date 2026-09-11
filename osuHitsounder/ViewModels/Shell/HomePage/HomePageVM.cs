using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using osuHitsounder.Services.FileSystem;

namespace osuHitsounder.ViewModels.Shell.HomePage;

public partial class HomePageVM(IHomeFolderService folders) : ObservableObject
{
    [ObservableProperty] private IReadOnlyList<DirectoryInfo> projects = [];
    [ObservableProperty] private IReadOnlyList<DirectoryInfo> songs = [];
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string projectsStatus = "Loading project folders…";
    [ObservableProperty] private string songsStatus = "Searching local drives for osu!…";
    [ObservableProperty] private string? fatalError;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (IsLoading)
            return;

        IsLoading = true;
        FatalError = null;
        ProjectsStatus = "Loading project folders…";
        SongsStatus = "Searching local drives for osu!…";
        try
        {
            var data = await folders.LoadAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Projects = data.Projects;
            Songs = data.Songs;
            ProjectsStatus = Projects.Count == 0 ? "No project folders found." : "";
            SongsStatus = !data.SongsFolderExists ? "Songs folder wasn't found."
                : Songs.Count == 0 ? "No song folders found." : "";
            FatalError = data.OsuDirectory is null
                ? data.SkippedLocations
                    ? "osu! folder wasn't found. Some locations could not be scanned."
                    : "osu! folder wasn't found."
                : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            FatalError = $"Unable to load folders: {error.Message}";
            ProjectsStatus = "Folder loading failed.";
            SongsStatus = "Folder loading failed.";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
