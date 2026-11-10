using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.DataModels;
using Hitbench.Resources.Messages;
using Hitbench.Services.Dialogs;
using Hitbench.Services.FileSystem;
using Hitbench.Services.ProjectFiles;
using Hitbench.Services.Ui;

namespace Hitbench.ViewModels.Shell.HomePage;

public partial class HomePageVM(
    IHomeFolderService folders, IAppSettingsService settings, IInstallationDialogService dialogs,
    IUiScheduler ui,
    IProjectFileService projectFiles, IMessenger messenger,
    IProjectRemovalDialogService? removalDialogs = null, Func<TimeSpan, CancellationToken, Task>? searchDelay = null, ISampleFileDialogService? picker = null, OHsMapsetSampleImportService? mapsetImports = null) : ObservableObject, System.ComponentModel.INotifyDataErrorInfo
{
    private readonly TaskCompletionSource projectsReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public OHsHomePanelState ProjectsState => !active ? OHsHomePanelState.Inactive : IsLoadingProjects ? OHsHomePanelState.Loading : CanRetryProjects ? OHsHomePanelState.Failed : projectsSnapshot is null ? OHsHomePanelState.Unavailable : OHsHomePanelState.Ready;
    public OHsHomePanelState SongsState => !active ? OHsHomePanelState.Inactive : IsLoadingSongs ? OHsHomePanelState.Loading : IsAwaitingSongsPresentation ? OHsHomePanelState.AwaitingPresentation : CanRetrySongs ? OHsHomePanelState.Unavailable : songsSnapshot is null ? OHsHomePanelState.Unavailable : OHsHomePanelState.Ready;
    public bool CanUseProjects => CanInteract && ProjectsState == OHsHomePanelState.Ready;
    public bool CanUseSongs => CanInteract && SongsState == OHsHomePanelState.Ready;
    public bool IsSongsProcessing => IsProcessing;
    public async Task LoadProjectsAsync(CancellationToken token)
    {
        _ = LoadAsync(token); // The loading task remains owned by this screen and CleanupAsync.
        await projectsReady.Task.WaitAsync(token);
    }
    internal int SongSearchFilterCount { get; private set; }
    internal const int InitialSongCount = 9 * 5;
    [ObservableProperty] private string songSearchText = "";
    [ObservableProperty] private bool isSongSearchExpanded;
    [ObservableProperty] private bool isSearchingSongs;
    public int SongsInventoryGeneration => songsInventoryGeneration;
    public string MapsetInventoryText => songsInventory is null ? "" : string.Format(
        System.Globalization.CultureInfo.CurrentCulture, "{0} mapsets", songsInventory.Count);

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
    [ObservableProperty] private bool isScanningMapsets;
    private int mapsetScanGeneration;
    private int scanPresentationGeneration = -1;
    private int scanPresentationOwner;
    private int BeginMapsetScan()
    {
        scanPresentationGeneration = -1;
        var generation = ++mapsetScanGeneration;
        IsScanningMapsets = true;
        songsGeneration++;
        OnPropertyChanged(nameof(SongsPresentationGeneration));
        return generation;
    }
    private void EndMapsetScan(int generation)
    {
        if (generation != mapsetScanGeneration) return;
        scanPresentationGeneration = -1;
        IsScanningMapsets = false;
    }
    private void AwaitMapsetScanPresentation(int generation)
    {
        if (generation != mapsetScanGeneration || !active) return;
        if (songsSnapshot is not { Count: > 0 }) { EndMapsetScan(generation); return; }
        scanPresentationGeneration = songsGeneration;
        scanPresentationOwner = generation;
    }
    private bool initialDataComplete;
    [ObservableProperty] private bool isInitialLoading;
    private Task? cleanup;
    private FolderSnapshot? projectsSnapshot;
    private FolderSnapshot? songsSnapshot;
    private FolderSnapshot? songsInventory;
    private CancellationTokenSource? songQueryCancellation, projectQueryCancellation;
    private readonly HashSet<Task> searchOperations = [];
    private int songsInventoryGeneration;
    private string appliedSongSearchText = "";
    private string? osuDirectory;
    private bool active = true;
    private int projectsGeneration;
    private int songsGeneration;

    public bool IsLoading => IsLoadingProjects || IsLoadingSongs;
    public bool CanInteract => active && !isLocatingFolder && !IsInteractionBlocked && !RequiresShutdown && FatalError is null && !IsMutatingProjects;
    public string SongsStatus => IsCreatingProject ? "Creating project..." : SongsLoadStatus;
    public bool HasMoreProjects => active && projectsSnapshot is not null && Projects.Count < projectsSnapshot.Count;
    public bool HasMoreSongs => active && !IsSearchingSongs && songsSnapshot is not null && Songs.Count < songsSnapshot.Count;

    partial void OnIsScanningMapsetsChanged(bool value) => RefreshCommands();
    partial void OnIsInteractionBlockedChanged(bool value) => RefreshCommands();
    partial void OnRequiresShutdownChanged(bool value) => RefreshCommands();
    partial void OnIsCreatingProjectChanged(bool value) => RefreshCommands();
    partial void OnIsLoadingProjectsChanged(bool value) => RefreshCommands();
    partial void OnIsLoadingSongsChanged(bool value) => RefreshCommands();
    partial void OnCanRetryProjectsChanged(bool value) => RefreshCommands();
    partial void OnCanRetrySongsChanged(bool value) => RefreshCommands();
    partial void OnIsSearchingSongsChanged(bool value) => RefreshCommands();
    partial void OnIsAwaitingSongsPresentationChanged(bool value) => RefreshCommands();
    partial void OnIsProcessingChanged(bool value)
    {
        RefreshCommands();
        if (!value && appliedSongSearchText != SongSearchText)
            OnSongSearchTextChanged(SongSearchText);
    }
    partial void OnSongSearchTextChanged(string value)
    {
        if (active && songsInventory is not null && !IsProcessing)
            SearchSongsCommand.Execute(true);
    }

    private bool CanSearchSongs() => CanInteract && !IsCreatingProject;
    private bool CanReloadSongs() => !isLocatingFolder && CanSearchSongs() && !IsLoadingSongs &&
        !IsSearchingSongs && !IsAwaitingSongsPresentation;

    [RelayCommand(CanExecute = nameof(CanSearchSongs))]
    private void OpenSongSearch() => IsSongSearchExpanded = true;

    [RelayCommand(CanExecute = nameof(CanSearchSongs))]
    private void CloseSongSearch()
    {
        SongSearchText = "";
        IsSongSearchExpanded = false;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchSongsAsync(bool? debounce, CancellationToken cancellationToken)
    {
        if (!active || songsInventory is null || IsProcessing) return;
        var task = SearchSongsCoreAsync(debounce == true, cancellationToken);
        searchOperations.Add(task);
        try { await task; }
        finally { searchOperations.Remove(task); }
    }

    private async Task SearchSongsCoreAsync(bool debounce, CancellationToken cancellationToken)
    {
        songQueryCancellation?.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
        songQueryCancellation = linked;
        var generation = ++songsGeneration;
        IsLoadingSongs = false;
        var inventory = songsInventory!;
        var query = SongSearchText;
        IsAwaitingSongsPresentation = false;
        IsSearchingSongs = true;
        SongsLoadStatus = "Searching songs...";
        try
        {
            if (debounce && !string.IsNullOrEmpty(query)) await (searchDelay ?? Task.Delay)(TimeSpan.FromMilliseconds(150), linked.Token);
            SongSearchFilterCount++;
            var filtered = await Task.Run(() => inventory.FilterSongs(query, linked.Token), linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            if (!IsCurrent(false, generation)) return;
            var initialPage = filtered.ReadPage(0, InitialSongCount);
            songsSnapshot = filtered;
            appliedSongSearchText = query;
            IsAwaitingSongsPresentation = true;
            Songs.Clear();
            foreach (var row in initialPage) Songs.Add(row);
            UpdateSongsStatus();
            OnPropertyChanged(nameof(SongsPresentationGeneration));
            IsAwaitingSongsPresentation = true;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            if (IsCurrent(false, generation))
            {
                SongsLoadStatus = "Song search cancelled.";
                CanRetrySongs = true;
            }
        }
        catch (Exception error) { if (IsCurrent(false, generation)) FailPanel(false, error); }
        finally
        {
            if (ReferenceEquals(songQueryCancellation, linked)) songQueryCancellation = null;
            if (IsCurrent(false, generation))
            {
                IsSearchingSongs = false;
                RefreshCommands();
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanReloadSongs))]
    private Task ReloadSongsAsync(CancellationToken cancellationToken) =>
        CanReloadSongs() ? RetryAsync(false, cancellationToken, true) : Task.CompletedTask;

    private void RefreshCommands()
    {
        OnPropertyChanged(nameof(CanInteract));
        OnPropertyChanged(nameof(MapsetsAvailability));
        OnPropertyChanged(nameof(MapsetsDirectory));
        LocateOsuFolderCommand.NotifyCanExecuteChanged();
        LocateMapsetsDirectoryCommand.NotifyCanExecuteChanged();
        foreach (var name in new[] { nameof(ProjectsState), nameof(SongsState), nameof(CanUseProjects), nameof(CanUseSongs), nameof(IsSongsProcessing) }) OnPropertyChanged(name);
        OnPropertyChanged(nameof(CanSearchProjects));
        OnPropertyChanged(nameof(CanSelectMapset));
        SelectMapsetCommand.NotifyCanExecuteChanged();
        ShowAllProjectsCommand.NotifyCanExecuteChanged();
        ShowProjectCreationCommand.NotifyCanExecuteChanged();
        CancelProjectCreationCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasMoreProjects));
        OnPropertyChanged(nameof(HasMoreSongs));
        OpenProjectCommand.NotifyCanExecuteChanged();
        CreateProjectCommand.NotifyCanExecuteChanged();
        LoadMoreProjectsCommand.NotifyCanExecuteChanged();
        LoadMoreSongsCommand.NotifyCanExecuteChanged();
        RetryProjectsCommand.NotifyCanExecuteChanged();
        RetrySongsCommand.NotifyCanExecuteChanged();
        ReloadSongsCommand.NotifyCanExecuteChanged();
        OpenSongSearchCommand.NotifyCanExecuteChanged();
        CloseSongSearchCommand.NotifyCanExecuteChanged();
        ToggleProjectPinCommand.NotifyCanExecuteChanged();
        DeleteProjectCommand.NotifyCanExecuteChanged();
    }

    private bool CanOpenProject(FolderEntry? entry) => entry is not null && CanInteract && !IsCreatingProject;
    private bool CanLoadMoreProjects() => CanInteract && HasMoreProjects && !IsLoadingProjects && !CanRetryProjects;
    private bool CanLoadMoreSongs(int requestedCount) =>
        requestedCount > 0 && CanInteract && HasMoreSongs && !IsLoadingSongs &&
        !IsAwaitingSongsPresentation && !CanRetrySongs;
    private bool CanRestartProjects() => CanInteract && CanRetryProjects && !IsLoadingProjects;
    private bool CanRestartSongs() => CanReloadSongs() && CanRetrySongs;

    [RelayCommand(CanExecute = nameof(CanOpenProject))]
    private void OpenProject(FolderEntry? entry)
    {
        if (!CanOpenProject(entry)) return;
        messenger.Send(new ProjectOpenRequestedMessage(Path.Combine(entry!.FullName, "project.json")));
    }

    public Task LoadAsync(CancellationToken cancellationToken) => loading ??= LoadCoreAsync(cancellationToken);

    private async Task LoadCoreAsync(CancellationToken callerToken)
    {
        callerToken.ThrowIfCancellationRequested();
        if (!active) throw new OperationCanceledException();
        callerRegistration = callerToken.Register(() => lifetime.Cancel());
        IsInitialLoading = true;
        var songs = InitializeSongsAsync();
        var projects = InitializeProjectsAsync();
        try { await Task.WhenAll(projects, songs); }
        finally { initialDataComplete = true; IsInitialLoading = false; }
        if (callerToken.IsCancellationRequested || !active)
            throw new OperationCanceledException(callerToken.IsCancellationRequested ? callerToken : new CancellationToken(true));
    }

    private async Task InitializeProjectsAsync()
    {
        try { await PreparePanelAsync(true, lifetime.Token); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { FailPanel(true, error); }
        finally { IsInitialLoading = false; projectsReady.TrySetResult(); }
    }

    private async Task InitializeSongsAsync()
    {
        var scan = BeginMapsetScan();
        IsProcessing = true;
        IsInteractionBlocked = false;
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
                AwaitMapsetScanPresentation(scan);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { FailPanel(false, error); }
        finally
        {
            IsLoadingSongs = false;
            if (!IsAwaitingSongsPresentation || lifetime.IsCancellationRequested || RequiresShutdown)
            {
                EndMapsetScan(scan);
                EndProcessing();
            }
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
        if (initialDataComplete || RequiresShutdown || lifetime.IsCancellationRequested) IsInitialLoading = false;
        if (scanPresentationGeneration == songsGeneration) EndMapsetScan(scanPresentationOwner);
        IsAwaitingSongsPresentation = false;
        IsProcessing = false;
        IsInteractionBlocked = false;
    }

    public bool CompleteSongsPresentation(int generation)
    {
        if (!active || generation != songsGeneration || !IsAwaitingSongsPresentation || IsLoading || IsSearchingSongs ||
            (IsScanningMapsets && scanPresentationGeneration != generation)) return false;
        EndProcessing();
        return true;
    }

    public void SuspendSongsPresentation()
    {
        if (active && songsSnapshot is not null) IsAwaitingSongsPresentation = true;
    }

    public async Task LoadInitialSongsAsync(CancellationToken cancellationToken)
    {
        if (!active || !IsAwaitingSongsPresentation || IsLoadingSongs || IsSearchingSongs ||
            songsSnapshot is null || Songs.Count >= InitialSongCount) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
        var generation = songsGeneration;
        SetBusy(false, true);
        try
        {
            await PublishPageAsync(false, songsSnapshot, generation,
                InitialSongCount - Songs.Count, linked.Token);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        { if (IsCurrent(false, generation)) EndProcessing(); }
        catch (Exception error)
        { if (IsCurrent(false, generation)) { FailPanel(false, error); EndProcessing(); } }
        finally { if (IsCurrent(false, generation)) SetBusy(false, false); }
    }

    private async Task<string?> ResolveInstallationAsync(CancellationToken token)
    {
        var configured = (await settings.LoadAsync(token)).MapsetsDirectory;
        token.ThrowIfCancellationRequested();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!Path.IsPathFullyQualified(configured)) throw new IOException("The mapsets directory must be absolute.");
            return configured;
        }
        var result = await folders.FindInstallationAsync(token);
        token.ThrowIfCancellationRequested();
        if (result.Directory is not { } found || !await folders.IsInstallationAsync(found, token)) return null;
        token.ThrowIfCancellationRequested();
        await settings.SaveOsuPathAsync(found, token);
        token.ThrowIfCancellationRequested();
        return Path.Combine(found, "Songs");
    }

    private async Task PreparePanelAsync(bool projects, CancellationToken token, bool forceReload = false)
    {
        var generation = projects ? ++projectsGeneration : ++songsGeneration;
        SetBusy(projects, true);
        if (projects) { CanRetryProjects = false; ProjectsStatus = "Loading project folders..."; }
        else { CanRetrySongs = false; SongsLoadStatus = "Loading song folders..."; }
        try
        {
            var snapshot = projects ? await folders.LoadProjectsAsync(token)
                : forceReload ? await folders.ReloadSongsAsync(osuDirectory!, token)
                : await folders.LoadSongsAsync(osuDirectory!, token);
            token.ThrowIfCancellationRequested();
            if (!IsCurrent(projects, generation)) return;
            if (projects)
            {
                projectsInventory = snapshot;
                snapshot = SelectedMapset is { } selected ? snapshot?.FilterProjectsByMapset(selected.FullName, token)
                    : snapshot?.FilterProjects(ProjectSearchText, token);
                projectsSnapshot = snapshot;
            }
            else
            {
                FolderSnapshot? filtered;
                string query;
                do
                {
                    query = SongSearchText;
                    filtered = snapshot is null ? null :
                        await Task.Run(() => snapshot.FilterSongs(query, token), token);
                    token.ThrowIfCancellationRequested();
                    if (!IsCurrent(false, generation)) return;
                } while (query != SongSearchText);
                if (snapshot is not null) await settings.SaveMapsetsDirectoryAsync(osuDirectory!, token);
                token.ThrowIfCancellationRequested();
                if (!IsCurrent(false, generation)) return;
                songsInventory = snapshot;
                songsSnapshot = filtered;
                appliedSongSearchText = query;
                songsInventoryGeneration++;
                OnPropertyChanged(nameof(SongsInventoryGeneration));
                OnPropertyChanged(nameof(MapsetInventoryText));
                OnPropertyChanged(nameof(SongsPresentationGeneration));
            }
            var rows = projects ? Projects : Songs;
            var selection = projects ? null : SelectedMapset;
            rows.Clear();
            if (!projects && selection is not null)
                SelectedMapset = snapshot?.ReadPage(0, snapshot.Count).FirstOrDefault(x =>
                    string.Equals(x.FullName, selection.FullName, StringComparison.OrdinalIgnoreCase));
            if (projects && snapshot is not null)
                await PublishPageAsync(true, snapshot, generation, 64, token);
            if (projects) RebuildProjectItems();
            token.ThrowIfCancellationRequested();
            if (!IsCurrent(projects, generation)) return;
            if (projects) ProjectsStatus = rows.Count == 0 ? "No project folders found." : "";
            else
            {
                UpdateSongsStatus();
            }
        }
        finally { if (IsCurrent(projects, generation)) SetBusy(projects, false); }
    }

    private void UpdateSongsStatus()
    {
        SongsLoadStatus = songsInventory is null ? "Songs folder wasn't found."
            : songsInventory.Count == 0 ? "No song folders found."
            : songsSnapshot?.Count == 0 ? "No songs match your search." : "";
        CanRetrySongs = songsInventory is null || songsInventory.Count == 0;
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
        if (projects) RebuildProjectItems();
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
        var generation = projects ? projectsGeneration : songsGeneration;
        SetBusy(projects, true);
        try
        {
            await PublishPageAsync(projects, (projects ? projectsSnapshot : songsSnapshot)!,
                generation, requestedCount, linked.Token);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch (Exception error) { if (IsCurrent(projects, generation)) FailPanel(projects, error); }
        finally { if (IsCurrent(projects, generation)) SetBusy(projects, false); }
    }

    private async Task RetryAsync(bool projects, CancellationToken cancellationToken, bool reload = false)
    {
        if (!(projects ? CanRestartProjects() : reload ? CanReloadSongs() : CanRestartSongs())) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
        var scan = projects ? 0 : BeginMapsetScan();
        try
        {
            if (!projects)
            {
                IsAwaitingSongsPresentation = false;
                IsProcessing = true;
                IsInteractionBlocked = false;
                await ui.YieldForRenderAsync(linked.Token);
                linked.Token.ThrowIfCancellationRequested();
            }
            if (!projects && osuDirectory is null)
            {
                osuDirectory = await ResolveInstallationAsync(linked.Token);
                if (osuDirectory is null) { EndProcessing(); return; }
            }
            await PreparePanelAsync(projects, linked.Token, forceReload: !projects);
            if (!projects)
            {
                IsAwaitingSongsPresentation = true;
                AwaitMapsetScanPresentation(scan);
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch (Exception error) { if (projects || scan == mapsetScanGeneration) FailPanel(projects, error); }
        finally
        {
            if (!projects && scan == mapsetScanGeneration)
            {
                if (!IsAwaitingSongsPresentation || linked.IsCancellationRequested)
                {
                    EndMapsetScan(scan);
                    EndProcessing();
                    if (active && songsSnapshot is not null) SuspendSongsPresentation();
                }
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
        IsInitialLoading = false;
        if (!active) return;
        active = false;
        EndMapsetScan(mapsetScanGeneration);
        mapsetScanGeneration++;
        projectsGeneration++;
        songsGeneration++;
        projectsSnapshot = songsSnapshot = null;
        songsInventory = null;
        IsSearchingSongs = false;
        IsLoadingProjects = IsLoadingSongs = false;
        EndProcessing();
        lifetime.Cancel();
        CreateProjectCommand.Cancel();
        ShowProjectCreationCommand.Cancel();
        LoadMoreProjectsCommand.Cancel();
        LoadMoreSongsCommand.Cancel();
        RetryProjectsCommand.Cancel();
        RetrySongsCommand.Cancel();
        ReloadSongsCommand.Cancel();
        LocateOsuFolderCommand.Cancel();
        LocateMapsetsDirectoryCommand.Cancel();
        SearchSongsCommand.Cancel();
        SearchProjectsCommand.Cancel();
        ToggleProjectPinCommand.Cancel();
        DeleteProjectCommand.Cancel();
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
        var tasks = new[] { loading, CreateProjectCommand.ExecutionTask, ShowProjectCreationCommand.ExecutionTask, LoadMoreProjectsCommand.ExecutionTask,
            LoadMoreSongsCommand.ExecutionTask, RetryProjectsCommand.ExecutionTask, RetrySongsCommand.ExecutionTask,
            ReloadSongsCommand.ExecutionTask, LocateOsuFolderCommand.ExecutionTask, LocateMapsetsDirectoryCommand.ExecutionTask }.Concat(searchOperations).Concat(projectOperations).ToArray();
        try { await Task.WhenAll(tasks.OfType<Task>()); }
        catch (OperationCanceledException) { }
        catch (Exception error) { Trace.TraceError("HomePage cleanup: {0}", error); }
        callerRegistration.Dispose();
        lifetime.Dispose();
    }
}
