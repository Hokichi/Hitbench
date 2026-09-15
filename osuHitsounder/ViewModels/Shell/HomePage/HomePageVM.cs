using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Resources.Messages;
using osuHitsounder.Services.Dialogs;
using osuHitsounder.Services.FileSystem;
using osuHitsounder.Services.ProjectFiles;
using osuHitsounder.Services.Ui;

namespace osuHitsounder.ViewModels.Shell.HomePage;

public partial class HomePageVM(
    IHomeFolderService folders, IAppSettingsService settings, IInstallationDialogService dialogs,
    IProjectCreationDialogService projectDialogs, IUiScheduler ui,
    IProjectFileService projectFiles, IMessenger messenger) : ObservableObject
{
    public ObservableCollection<FolderEntry> Projects { get; } = [];
    public ObservableCollection<FolderEntry> Songs { get; } = [];
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsLoading))] private bool isLoadingProjects;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsLoading), nameof(SongsStatus))] private bool isLoadingSongs;
    [ObservableProperty] private bool isProcessing;
    [ObservableProperty] private bool isAwaitingSongsPresentation;
    public int SongsPresentationGeneration => songsGeneration;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanInteract))] private bool isInteractionBlocked;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanInteract))] private bool requiresShutdown;
    [ObservableProperty] private string? fatalError;
    [ObservableProperty] private string projectsStatus = "Loading project folders...";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SongsStatus))] private string songsLoadStatus = "Loading song folders...";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SongsStatus))] private bool isCreatingProject;
    [ObservableProperty] private bool canRetryProjects;
    [ObservableProperty] private bool canRetrySongs;

    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenRegistration callerRegistration;
    private Task? loading;
    private Task? cleanup;
    private FolderSnapshot? projectsSnapshot;
    private FolderSnapshot? songsSnapshot;
    private string? osuDirectory;
    private bool active = true;
    private int projectsGeneration;
    private int songsGeneration;

    public bool IsLoading => IsLoadingProjects || IsLoadingSongs;
    public bool CanInteract => active && !IsInteractionBlocked && !RequiresShutdown && FatalError is null;
    public string SongsStatus => IsCreatingProject ? "Creating project..." : SongsLoadStatus;
    public bool HasMoreProjects => active && projectsSnapshot is not null && Projects.Count < projectsSnapshot.Count;
    public bool HasMoreSongs => active && songsSnapshot is not null && Songs.Count < songsSnapshot.Count;

    partial void OnIsInteractionBlockedChanged(bool value) => RefreshCommands();
    partial void OnRequiresShutdownChanged(bool value) => RefreshCommands();
    partial void OnIsCreatingProjectChanged(bool value) => RefreshCommands();
    partial void OnIsLoadingProjectsChanged(bool value) => RefreshCommands();
    partial void OnIsLoadingSongsChanged(bool value) => RefreshCommands();
    partial void OnCanRetryProjectsChanged(bool value) => RefreshCommands();
    partial void OnCanRetrySongsChanged(bool value) => RefreshCommands();

    private void RefreshCommands()
    {
        OnPropertyChanged(nameof(CanInteract));
        OnPropertyChanged(nameof(HasMoreProjects));
        OnPropertyChanged(nameof(HasMoreSongs));
        CreateProjectCommand.NotifyCanExecuteChanged();
        LoadMoreProjectsCommand.NotifyCanExecuteChanged();
        LoadMoreSongsCommand.NotifyCanExecuteChanged();
        RetryProjectsCommand.NotifyCanExecuteChanged();
        RetrySongsCommand.NotifyCanExecuteChanged();
    }

    private bool CanCreateProject(FolderEntry? entry) => entry is not null && CanInteract && !IsCreatingProject;
    private bool CanLoadMoreProjects() => CanInteract && HasMoreProjects && !IsLoadingProjects && !CanRetryProjects;
    private bool CanLoadMoreSongs(int requestedCount) =>
        requestedCount > 0 && CanInteract && HasMoreSongs && !IsLoadingSongs && !CanRetrySongs;
    private bool CanRestartProjects() => CanInteract && CanRetryProjects && !IsLoadingProjects;
    private bool CanRestartSongs() => CanInteract && CanRetrySongs && !IsLoadingSongs;

    [RelayCommand(CanExecute = nameof(CanCreateProject))]
    private async Task CreateProjectAsync(FolderEntry? entry, CancellationToken cancellationToken)
    {
        if (!CanCreateProject(entry)) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        IsCreatingProject = true;
        try
        {
            string projectPath;
            try
            {
                var existingNames = await projectFiles.GetExistingNamesAsync(linked.Token);
                linked.Token.ThrowIfCancellationRequested();
                var projectName = await projectDialogs.RequestNameAsync(
                    entry!.DisplayName, existingNames, linked.Token);
                linked.Token.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(projectName)) return;
                projectPath = await projectFiles.CreateAsync(entry.Directory, projectName, linked.Token);
                linked.Token.ThrowIfCancellationRequested();
                if (!active) return;
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested) { return; }
            catch (Exception error)
            {
                if (active) messenger.Send(new ProjectCreationFailedMessage($"Unable to create project: {error.Message}"));
                return;
            }
            messenger.Send(new ProjectCreatedMessage(projectPath));
        }
        finally { IsCreatingProject = false; }
    }

    public Task LoadAsync(CancellationToken cancellationToken) => loading ??= LoadCoreAsync(cancellationToken);

    private async Task LoadCoreAsync(CancellationToken callerToken)
    {
        callerToken.ThrowIfCancellationRequested();
        if (!active) throw new OperationCanceledException();
        callerRegistration = callerToken.Register(() => lifetime.Cancel());
        var songs = InitializeSongsAsync();
        var projects = InitializeProjectsAsync();
        await Task.WhenAll(projects, songs);
        if (callerToken.IsCancellationRequested || !active)
            throw new OperationCanceledException(callerToken.IsCancellationRequested ? callerToken : new CancellationToken(true));
    }

    private async Task InitializeProjectsAsync()
    {
        try { await PreparePanelAsync(true, lifetime.Token); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { FailInitialization(error); }
    }

    private async Task InitializeSongsAsync()
    {
        IsProcessing = true;
        IsInteractionBlocked = true;
        IsLoadingSongs = true;
        try
        {
            await ui.YieldForRenderAsync(lifetime.Token);
            lifetime.Token.ThrowIfCancellationRequested();
            osuDirectory = await ResolveInstallationAsync(lifetime.Token);
            lifetime.Token.ThrowIfCancellationRequested();
            if (osuDirectory is not null)
            {
                await PreparePanelAsync(false, lifetime.Token);
                IsAwaitingSongsPresentation = true;
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { FailInitialization(error); }
        finally
        {
            IsLoadingSongs = false;
            if (!IsAwaitingSongsPresentation || lifetime.IsCancellationRequested || RequiresShutdown)
                EndProcessing();
        }
    }

    private void FailInitialization(Exception error)
    {
        if (!active || RequiresShutdown || lifetime.IsCancellationRequested) return;
        FatalError = $"Unable to load folders: {error.Message}";
        RequiresShutdown = true;
        lifetime.Cancel();
        EndProcessing();
    }

    private void EndProcessing()
    {
        IsAwaitingSongsPresentation = false;
        IsProcessing = false;
        IsInteractionBlocked = false;
    }

    public void CompleteSongsPresentation(int generation)
    {
        if (!active || generation != songsGeneration || !IsAwaitingSongsPresentation || IsLoading) return;
        EndProcessing();
    }

    public async Task LoadInitialSongsAsync(int requestedCount, CancellationToken cancellationToken)
    {
        if (!active || !IsAwaitingSongsPresentation || IsLoadingSongs ||
            songsSnapshot is null || requestedCount <= Songs.Count) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
        SetBusy(false, true);
        try
        {
            await PublishPageAsync(false, songsSnapshot, songsGeneration, requestedCount - Songs.Count, linked.Token);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { EndProcessing(); }
        catch (Exception error) { FailPanel(false, error); EndProcessing(); }
        finally { SetBusy(false, false); }
    }

    private async Task<string?> ResolveInstallationAsync(CancellationToken token)
    {
        var configured = (await settings.LoadAsync(token)).OsuPath;
        token.ThrowIfCancellationRequested();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!await folders.IsInstallationAsync(configured, token))
                throw new IOException("Invalid osuPath in appsettings.json beside the application. Correct the installation folder or clear osuPath to scan.");
            token.ThrowIfCancellationRequested();
            return configured;
        }

        var result = await folders.FindInstallationAsync(token);
        token.ThrowIfCancellationRequested();
        var path = result.Directory;
        var manual = path is null;
        if (manual) path = await dialogs.LocateAfterScanAsync(token);
        token.ThrowIfCancellationRequested();
        if (path is null)
        {
            RequiresShutdown = true;
            lifetime.Cancel();
            return null;
        }
        bool valid;
        try { valid = await folders.IsInstallationAsync(path, token); }
        catch (Exception error) when (manual && error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            valid = false;
        }
        token.ThrowIfCancellationRequested();
        if (!valid)
        {
            if (!manual) throw new IOException("The discovered osu! installation is no longer available.");
            await dialogs.ShowNotFoundAsync(token);
            token.ThrowIfCancellationRequested();
            RequiresShutdown = true;
            lifetime.Cancel();
            return null;
        }
        await settings.SaveOsuPathAsync(path, token);
        token.ThrowIfCancellationRequested();
        return path;
    }

    private async Task PreparePanelAsync(bool projects, CancellationToken token)
    {
        var generation = projects ? ++projectsGeneration : ++songsGeneration;
        SetBusy(projects, true);
        if (projects) { CanRetryProjects = false; ProjectsStatus = "Loading project folders..."; }
        else { CanRetrySongs = false; SongsLoadStatus = "Loading song folders..."; }
        try
        {
            var snapshot = projects ? await folders.LoadProjectsAsync(token)
                : await folders.LoadSongsAsync(osuDirectory!, token);
            token.ThrowIfCancellationRequested();
            if (!IsCurrent(projects, generation)) return;
            if (projects) projectsSnapshot = snapshot;
            else songsSnapshot = snapshot;
            var rows = projects ? Projects : Songs;
            rows.Clear();
            if (projects && snapshot is not null)
                await PublishPageAsync(true, snapshot, generation, 64, token);
            token.ThrowIfCancellationRequested();
            if (!IsCurrent(projects, generation)) return;
            if (projects) ProjectsStatus = rows.Count == 0 ? "No project folders found." : "";
            else
            {
                SongsLoadStatus = snapshot is null ? "Songs folder wasn't found."
                    : snapshot.Count == 0 ? "No song folders found." : "";
                CanRetrySongs = snapshot is null || snapshot.Count == 0;
            }
        }
        finally { SetBusy(projects, false); }
    }

    private bool IsCurrent(bool projects, int generation) =>
        active && generation == (projects ? projectsGeneration : songsGeneration);

    private void SetBusy(bool projects, bool value)
    {
        if (projects) IsLoadingProjects = value;
        else IsLoadingSongs = value;
        RefreshCommands();
    }

    private async Task PublishPageAsync(
        bool projects, FolderSnapshot snapshot, int generation, int requestedCount, CancellationToken token)
    {
        var collection = projects ? Projects : Songs;
        var offset = collection.Count;
        var page = await Task.Run(() => snapshot.ReadPage(offset, requestedCount), token);
        token.ThrowIfCancellationRequested();
        if (!IsCurrent(projects, generation)) return;
        foreach (var row in page)
        {
            token.ThrowIfCancellationRequested();
            if (!IsCurrent(projects, generation)) return;
            collection.Add(row);
        }
        RefreshCommands();
    }

    [RelayCommand(CanExecute = nameof(CanLoadMoreProjects))]
    private Task LoadMoreProjectsAsync(CancellationToken token) => LoadMoreAsync(true, 64, token);
    [RelayCommand(CanExecute = nameof(CanLoadMoreSongs))]
    private Task LoadMoreSongsAsync(int requestedCount, CancellationToken token) =>
        LoadMoreAsync(false, requestedCount, token);
    [RelayCommand(CanExecute = nameof(CanRestartProjects))]
    private Task RetryProjectsAsync(CancellationToken token) => RetryAsync(true, token);
    [RelayCommand(CanExecute = nameof(CanRestartSongs))]
    private Task RetrySongsAsync(CancellationToken token) => RetryAsync(false, token);

    private async Task LoadMoreAsync(bool projects, int requestedCount, CancellationToken cancellationToken)
    {
        if (!(projects ? CanLoadMoreProjects() : CanLoadMoreSongs(requestedCount))) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
        SetBusy(projects, true);
        try
        {
            await PublishPageAsync(projects, (projects ? projectsSnapshot : songsSnapshot)!,
                projects ? projectsGeneration : songsGeneration, requestedCount, linked.Token);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch (Exception error) { FailPanel(projects, error); }
        finally { SetBusy(projects, false); }
    }

    private async Task RetryAsync(bool projects, CancellationToken cancellationToken)
    {
        if (!(projects ? CanRestartProjects() : CanRestartSongs())) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
        try
        {
            if (!projects)
            {
                IsAwaitingSongsPresentation = false;
                IsProcessing = true;
                IsInteractionBlocked = true;
                await ui.YieldForRenderAsync(linked.Token);
                linked.Token.ThrowIfCancellationRequested();
            }
            await PreparePanelAsync(projects, linked.Token);
            if (!projects) IsAwaitingSongsPresentation = true;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch (Exception error) { FailPanel(projects, error); }
        finally
        {
            if (!projects)
            {
                if (!IsAwaitingSongsPresentation || linked.IsCancellationRequested) EndProcessing();
            }
        }
    }

    private void FailPanel(bool projects, Exception error)
    {
        if (!active) return;
        if (projects) { ProjectsStatus = $"Unable to load projects: {error.Message}"; CanRetryProjects = true; }
        else { SongsLoadStatus = $"Unable to load songs: {error.Message}"; CanRetrySongs = true; }
    }

    public void Deactivate()
    {
        if (!active) return;
        active = false;
        projectsGeneration++;
        songsGeneration++;
        projectsSnapshot = songsSnapshot = null;
        EndProcessing();
        lifetime.Cancel();
        CreateProjectCommand.Cancel();
        LoadMoreProjectsCommand.Cancel();
        LoadMoreSongsCommand.Cancel();
        RetryProjectsCommand.Cancel();
        RetrySongsCommand.Cancel();
        RefreshCommands();
        cleanup = CleanupAsync();
    }

    public void ReportDemandFailure(Exception error)
    {
        FailPanel(true, error);
        FailPanel(false, error);
        EndProcessing();
    }

    private async Task CleanupAsync()
    {
        var tasks = new[] { loading, CreateProjectCommand.ExecutionTask, LoadMoreProjectsCommand.ExecutionTask,
            LoadMoreSongsCommand.ExecutionTask, RetryProjectsCommand.ExecutionTask, RetrySongsCommand.ExecutionTask };
        try { await Task.WhenAll(tasks.OfType<Task>()); }
        catch (OperationCanceledException) { }
        catch (Exception error) { Trace.TraceError("HomePage cleanup: {0}", error); }
        callerRegistration.Dispose();
        lifetime.Dispose();
    }
}
