using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Onset.Resources.DataModels;
using Onset.Services;
using Onset.Services.FileSystem;
using Onset.Services.Ui;

namespace Onset.ViewModels.Popups;

public sealed partial class SampleLoaderPopupVM(ISampleBrowserService browser, ISamplePreviewService preview,
    IUiScheduler uiScheduler, PlaybackVolumeSettings volumes) : ObservableObject
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly HashSet<Task> operations = [];
    private readonly HashSet<SampleBrowserEntry> observed = [];
    private readonly SemaphoreSlim previewGate = new(1, 1);
    private CancellationTokenSource? navigationCancellation, metadataCancellation, playCancellation;
    private Task previewMonitor = Task.CompletedTask;
    private CancellationTokenRegistration externalCancellation;
    private Task? deactivation;
    private int navigationGeneration, selectionGeneration;
    private bool deactivated;
    private bool isStartingPreview;
    private double playbackPositionSeconds;
    private bool isSeeking;
    public ObservableCollection<SampleBrowserEntry> Entries { get; } = [];
    public ObservableCollection<SampleBrowserLocation> Locations { get; } = [];
    [ObservableProperty] private SampleBrowserLocation? activeLocation;
    public SamplePickerContext? Context { get; private set; }

    public Task InitializeAsync(SamplePickerContext context, CancellationToken token)
    {
        Context = context;
        ConfigureSelectionMode(context.Mode);
        return InitializeAsync(context.InitialDirectory ?? context.SongDirectory, token);
    }

    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private Task NavigateLocationAsync(SampleBrowserLocation location) => OwnAsync(async () =>
    {
        if (!location.IsAvailable || location.FullPath is null) return;
        await NavigateToAsync(location.FullPath);
        if (SamePath(CurrentDirectory, location.FullPath) && DirectoryError is null)
            await UpdateAsync(() => ActiveLocation = location);
    });
    public SampleSelectionMode SelectionMode { get; private set; }
    public void ConfigureSelectionMode(SampleSelectionMode mode) { SelectionMode = mode; OnPropertyChanged(nameof(SelectionMode)); NotifyState(); }
    public bool IsFolderSelection => SelectionMode == SampleSelectionMode.Folder;
    public string PopupTitle => IsFolderSelection ? "Select sample folder" : "Load samples";
    public string ConfirmActionText => IsFolderSelection ? "Select folder" : CheckedCount == 0 ? "Import samples" : string.Format(CultureInfo.CurrentCulture, CheckedCount == 1 ? "Import {0} sample" : "Import {0} samples", CheckedCount);
    public string ConfirmAutomationName => IsFolderSelection ? "Select sample folder" : "Import checked files";
    public string SelectionPrompt => IsFolderSelection ? "Choose a folder, then select it to map note-named audio files." : "Select a .wav, .ogg or .mp3";
    public string FolderEmptyPromptText => IsFolderSelection ? "No subfolders. Select this folder to import its audio files." : "No audio file to display";
    public string ContentsAutomationName => IsFolderSelection ? "Folders" : "Folders and audio files";
    [ObservableProperty] private SampleBrowserEntry? selectedFolder;
    public string SelectedFolderPath => SelectedFolder?.FullPath ?? CurrentDirectory;
    partial void OnSelectedFolderChanged(SampleBrowserEntry? value) => NotifyState();
    public ObservableCollection<SampleBrowserBreadcrumb> Breadcrumbs { get; } = [];
    public bool IsThisPc => CurrentDirectory.Length == 0;
    public bool IsFolderEmpty => !IsNavigating && !IsThisPc && Entries.Count == 0;

    [ObservableProperty] private string currentDirectory = "";
    [ObservableProperty] private SampleBrowserEntry? selectedFile;
    [ObservableProperty] private SampleFileMetadata? metadata;
    [ObservableProperty] private bool isNavigating;
    [ObservableProperty] private bool isMetadataLoading;
    [ObservableProperty] private bool isPreviewPlaying;
    [ObservableProperty] private bool isConfirming;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(CanConfirm))][NotifyPropertyChangedFor(nameof(CanImport))] private string? directoryError;
    [ObservableProperty] private string? metadataError;
    [ObservableProperty] private string? previewError;
    [ObservableProperty] private string? previewWarning;
    [ObservableProperty] private bool autoPlaySamples = true;

    public double PlaybackPositionSeconds => playbackPositionSeconds;
    public double PlaybackDurationSeconds => Math.Max(0, Metadata?.Duration.TotalSeconds ?? 0);
    public bool IsSeeking => isSeeking;
    public bool CanSeek => CanPreview && PlaybackDurationSeconds > 0;
    public string PlaybackTimeText => string.Format(CultureInfo.CurrentCulture, "{0:0.000} / {1:0.000} s",
        PlaybackPositionSeconds, PlaybackDurationSeconds);

    public int CheckedCount => selectedSamples.Count;
    public string CheckedCountText => IsFolderSelection ? string.Format(CultureInfo.CurrentCulture, "Selected folder: {0}", SelectedFolderPath.Length == 0 ? "None" : SelectedFolderPath) : string.Format(CultureInfo.CurrentCulture, "{0:N0} files checked", CheckedCount);
    public bool CanNavigate => !deactivated && !IsConfirming;
    public bool CanConfirm => !deactivated && !IsNavigating && !IsConfirming && (IsFolderSelection ? SelectedFolderPath.Length > 0 && DirectoryError is null : CheckedCount > 0 && (SelectionMode!=SampleSelectionMode.Single || CheckedCount==1));
    public bool CanImport => CanConfirm;
    public bool CanPreview => !IsFolderSelection && !deactivated && !IsNavigating && !IsConfirming && !IsMetadataLoading && !isStartingPreview && !IsSeeking && Metadata is not null;
    public string BrowserStatusText => IsNavigating ? "Loading folder..." : CurrentDirectory.Length == 0 ? Entries.Count == 0 ? "No drives to display" : "Choose a drive." : "";
    public string PreviewActionText => IsPreviewPlaying ? "Stop" : "Play";
    public string MetadataStatusText => IsMetadataLoading ? "Reading metadata..." : SelectedFile is null ? "Select a file to inspect and preview." : "";
    public string PreviewMetadataText => Metadata is { } data ? string.Format(CultureInfo.CurrentCulture,
        "{0:0.000} s · {1:N1} KiB · {2:0.#} kHz · {3}", data.Duration.TotalSeconds, data.Length / 1024d,
        data.SampleRate / 1000d, data.Channels == 1 ? "mono" : data.Channels == 2 ? "stereo" : string.Format(CultureInfo.CurrentCulture, "{0} channels", data.Channels)) : "";
    public string SizeText => Metadata is { } data ? string.Format(CultureInfo.CurrentCulture, "{0:N1} KiB", data.Length / 1024d) : "";
    public string DurationText => Metadata is { } data ? string.Format(CultureInfo.CurrentCulture, "{0:N3} s", data.Duration.TotalSeconds) : "";

    public Task InitializeAsync(CancellationToken token) => InitializeAsync((string?)null, token);

    public Task InitializeAsync(string? initialDirectory, CancellationToken token) => OwnAsync(async () =>
    {
        externalCancellation = token.Register(lifetime.Cancel);
        await UpdateAsync(() =>
        {
            Locations.Clear();
            Locations.Add(new("Song folder", Context?.SongDirectory, "This project", !string.IsNullOrWhiteSpace(Context?.SongDirectory)));
            string? saved = null;
            try { if (Context?.ProjectPath is { } project) saved = SamplePaths.SavedRoot(project); }
            catch (ArgumentException) { }
            Locations.Add(new("Saved Samples", saved, "This project", saved is not null));
        });
        try
        {
            var roots = await browser.GetRootsAsync(lifetime.Token);
            if (!deactivated) await UpdateAsync(() =>
            {
                foreach (var root in roots) Locations.Add(new(root.Name, root.FullPath, "Drives", true));
            });
        }
        catch (OperationCanceledException) { return; }
        catch (Exception error) { System.Diagnostics.Trace.TraceWarning("Sample drives unavailable: {0}", error.Message); }
        try
        {
            var folder = string.IsNullOrWhiteSpace(initialDirectory)
                ? await browser.GetDefaultDirectoryAsync(lifetime.Token) : initialDirectory;
            await NavigateToAsync(folder);
            if (CurrentDirectory.Length == 0 && DirectoryError is { } failure)
            { await NavigateToAsync(""); await UpdateAsync(() => DirectoryError = failure); }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { await NavigateToAsync(""); await ReportAsync(() => { if (Breadcrumbs.Count == 0) BuildBreadcrumbs(""); DirectoryError = error.Message; }); }
    });

    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private Task NavigateAsync(string? path) => OwnAsync(() => NavigateToAsync(path ?? ""));

    private async Task NavigateToAsync(string path)
    {
        if (deactivated || IsConfirming) return;
        var request = ++navigationGeneration;
        navigationCancellation?.Cancel();
        navigationCancellation?.Dispose();
        var cancellation = navigationCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        await UpdateAsync(() => { IsNavigating = true; DirectoryError = null; NotifyState(); });
        try
        {
            path = string.IsNullOrWhiteSpace(path) ? "" : Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
            if (SamePath(path, CurrentDirectory) && Breadcrumbs.Count > 0)
            {

                return;
            }
            var children = path.Length == 0 ? await browser.GetRootsAsync(cancellation.Token) : await browser.GetEntriesAsync(path, cancellation.Token);
            if (request != navigationGeneration || deactivated) return;
            ++selectionGeneration;
            metadataCancellation?.Cancel();
            await StopPreviewCoreAsync();
            await UpdateAsync(() =>
            {
                if (request != navigationGeneration || deactivated || cancellation.IsCancellationRequested) return;
                foreach (var entry in observed.ToArray()) { entry.IsSelected = false; if (!selectedSamples.Contains(entry)) Forget(entry); }
                Entries.Clear();
                foreach (var child in children.Where(child => !IsFolderSelection || child.IsDirectory))
                {
                    var entry = selectedSamples.FirstOrDefault(x => SamePath(x.FullPath, child.FullPath)) ?? child;
                    entry.IsAlreadyInProject = IsImportedPath(entry.FullPath);
                    Entries.Add(entry); Observe(entry);
                }
                rangeAnchor = null;
                SearchText = "";
                RefreshVisibleEntries();
                SelectedFolder = null;
                BuildBreadcrumbs(path);
                CurrentDirectory = path;
                ActiveLocation = Locations.Where(location => location.FullPath is { } root &&
                    (SamePath(root, path) || path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(location => location.FullPath!.Length).FirstOrDefault();
                SelectedFile = null;
                Waveform = null;
                Metadata = null;
                MetadataError = null;
                IsMetadataLoading = false;
                NotifyState();
            });
            if (!IsFolderSelection) StartListingMetadata(request, cancellation.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (request == navigationGeneration) await ReportAsync(() => { if (request == navigationGeneration) DirectoryError = error.Message; }); }
        finally { if (request == navigationGeneration) await ReportAsync(() => { IsNavigating = false; NotifyState(); }); }
    }

    private void BuildBreadcrumbs(string path)
    {
        Breadcrumbs.Clear();
        Breadcrumbs.Add(new(null, "This PC", path.Length == 0));
        if (path.Length == 0) return;
        var root = Path.GetPathRoot(path)!;
        Breadcrumbs.Add(new(root, root, SamePath(root, path)));
        var current = root;
        foreach (var part in path[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            Breadcrumbs.Add(new(current, part, SamePath(current, path)));
        }
    }

    public Task SelectAsync(SampleBrowserEntry entry) => OwnAsync(() => SelectCoreAsync(entry));

    public Task ActivateFileAsync(SampleBrowserEntry entry) => OwnAsync(async () =>
    {
        if (deactivated || IsConfirming || entry.IsDirectory) return;
        if (!SamePath(SelectedFile?.FullPath, entry.FullPath))
        {
            await SelectCoreAsync(entry);
            return;
        }
        if (!AutoPlaySamples || !CanPreview) return;
        var request = selectionGeneration;
        await StopPreviewCoreAsync();
        if (request == selectionGeneration && !deactivated && AutoPlaySamples)
            await StartPreviewAsync();
    });

    private async Task SelectCoreAsync(SampleBrowserEntry entry)
    {
        if (deactivated || IsConfirming) return;
        if (IsFolderSelection)
        {
            if (!entry.IsDirectory) return;
            await UpdateAsync(() =>
            {
                var actual = Entries.FirstOrDefault(child => SamePath(child.FullPath, entry.FullPath));
                if (actual is null || !actual.IsDirectory) return;
                foreach (var item in Entries) item.IsSelected = ReferenceEquals(item, actual);
                SelectedFolder = actual; NotifyState();
            });
            return;
        }
        if (entry.IsDirectory) { ++selectionGeneration; metadataCancellation?.Cancel(); await StopPreviewCoreAsync(); await UpdateAsync(() => { SelectedFile = null; Waveform = null; Metadata = null; MetadataError = null; IsMetadataLoading = false; NotifyState(); }); return; }
        var parent = Path.GetDirectoryName(entry.FullPath)!;
        if (!SamePath(parent, CurrentDirectory)) await NavigateToAsync(parent);
        if (deactivated || !SamePath(parent, CurrentDirectory)) return;
        var actual = Entries.FirstOrDefault(child => SamePath(child.FullPath, entry.FullPath));
        if (actual is null || actual.IsDirectory || SamePath(SelectedFile?.FullPath, actual.FullPath)) return;
        var request = ++selectionGeneration;
        metadataCancellation?.Cancel();
        metadataCancellation?.Dispose();
        var cancellation = metadataCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        await StopPreviewCoreAsync();
        if (request != selectionGeneration || deactivated) return;
        await UpdateAsync(() =>
        {
            SelectedFile = actual;
            Waveform = null;
            foreach (var item in observed) item.IsSelected = false;
            actual.IsSelected = true;
            Metadata = null;
            MetadataError = null;
            IsMetadataLoading = true;
            NotifyState();
        });
        _ = OwnAsync(() => LoadWaveformAsync(actual, request, cancellation.Token));
        try
        {
            var result = await ReadMetadataBoundedAsync(actual.FullPath, cancellation.Token);
            if (request == selectionGeneration && !deactivated) await UpdateAsync(() => { Metadata = result; actual.Metadata = result; NotifyState(); });
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (request == selectionGeneration) await ReportAsync(() => { if (request != selectionGeneration) return; MetadataError = error.Message; actual.Metadata = null; actual.IsMetadataUnavailable = true; }); }
        finally { if (request == selectionGeneration) await ReportAsync(() => { IsMetadataLoading = false; NotifyState(); }); }
        if (request == selectionGeneration && !deactivated && AutoPlaySamples && CanPreview && stoppedSelectionGeneration != request)
            await StartPreviewAsync();
    }

    public Task SetCheckedAsync(SampleBrowserEntry entry, bool value) => OwnAsync(async () =>
    {
        if (IsFolderSelection || entry.IsDirectory || deactivated || IsConfirming) return;
        var parent = Path.GetDirectoryName(entry.FullPath)!;
        if (!SamePath(parent, CurrentDirectory)) await NavigateToAsync(parent);
        if (!SamePath(parent, CurrentDirectory) || deactivated) return;
        await UpdateAsync(() =>
        {
            var actual = Entries.FirstOrDefault(child => SamePath(child.FullPath, entry.FullPath));
            if (actual is not null) { actual.IsChecked = value; SynchronizeChecked(actual); }
        });
        var inspection = SelectCoreAsync(entry);
        if (inspection.IsCompleted) await inspection;
        else _ = ObserveInspectionAsync(inspection);
    });

    private async Task ObserveInspectionAsync(Task inspection)
    {
        lock (operations) operations.Add(inspection);
        try { await inspection; }
        catch (OperationCanceledException) { }
        catch (Exception error) { await ReportAsync(() => MetadataError = error.Message); }
        finally { lock (operations) operations.Remove(inspection); }
    }

    public async Task<IReadOnlyList<string>> ConfirmAsync()
    {
        if (!CanImport) return Array.Empty<string>();
        IsConfirming = true;
        NotifyState();
        var result = IsFolderSelection ? new[] { SelectedFolderPath } : selectedSamples.Select(entry => entry.FullPath).ToArray();
        try { await StopPreviewCoreAsync(); return deactivated ? Array.Empty<string>() : Array.AsReadOnly(result); }
        catch (Exception error) { PreviewError = error.Message; IsConfirming = false; NotifyState(); return Array.Empty<string>(); }
    }

    private void Observe(SampleBrowserEntry entry)
    {
        if (observed.Add(entry)) entry.PropertyChanged += EntryChanged;
    }
    private void Forget(SampleBrowserEntry entry)
    {
        if (observed.Remove(entry)) entry.PropertyChanged -= EntryChanged;
    }
    private void EntryChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SampleBrowserEntry.IsChecked) && sender is SampleBrowserEntry entry) SynchronizeChecked(entry);
        else if (args.PropertyName is nameof(SampleBrowserEntry.Metadata) or nameof(SampleBrowserEntry.IsMetadataLoading) or nameof(SampleBrowserEntry.IsMetadataUnavailable)) NotifyState();
    }
    private void NotifyState()
    {
        foreach (var entry in observed) entry.IsPreviewing = IsPreviewPlaying && SamePath(entry.FullPath, SelectedFile?.FullPath);
        PlayPreviewCommand.NotifyCanExecuteChanged(); StopPreviewCommand.NotifyCanExecuteChanged(); SeekPreviewCommand.NotifyCanExecuteChanged();
        foreach (var name in new[] { nameof(PlaybackPosition), nameof(CanPlayPreview), nameof(CanStopPreview), nameof(PreviewMetadataText), nameof(SelectionAnnouncement), nameof(SelectionSummary), nameof(SelectedDurationText), nameof(CheckedCount), nameof(CheckedCountText), nameof(CanConfirm), nameof(CanImport), nameof(CanPreview), nameof(BrowserStatusText),
            nameof(IsFolderSelection), nameof(PopupTitle), nameof(ConfirmActionText), nameof(ConfirmAutomationName), nameof(SelectionPrompt), nameof(SelectedFolderPath), nameof(FolderEmptyPromptText), nameof(ContentsAutomationName),
            nameof(PreviewActionText), nameof(MetadataStatusText), nameof(SizeText), nameof(DurationText),
            nameof(PlaybackPositionSeconds), nameof(PlaybackDurationSeconds), nameof(PlaybackTimeText), nameof(CanSeek), nameof(IsSeeking), nameof(IsThisPc), nameof(IsFolderEmpty) }) OnPropertyChanged(name);
        TogglePreviewCommand.NotifyCanExecuteChanged();
        NavigateCommand.NotifyCanExecuteChanged();
        NavigateLocationCommand.NotifyCanExecuteChanged();
        RemoveSelectedCommand.NotifyCanExecuteChanged(); ClearSelectedCommand.NotifyCanExecuteChanged(); ActivateSelectedCommand.NotifyCanExecuteChanged();
    }
    private Task UpdateAsync(Action action) => uiScheduler.InvokeAsync(action, lifetime.Token);
    private Task ReportAsync(Action action) => deactivated ? Task.CompletedTask : uiScheduler.InvokeAsync(action, CancellationToken.None);
    private async Task OwnAsync(Func<Task> operation)
    {
        if (deactivated) return;
        var task = operation();
        lock (operations) operations.Add(task);
        try { await task; }
        finally { lock (operations) operations.Remove(task); }
    }
    private static bool SamePath(string? left, string? right)
    {
        if (left is null || right is null) return false;
        if (left.Length == 0 || right.Length == 0) return left.Length == right.Length;
        try { return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or System.Security.SecurityException) { return false; }
    }

    public Task DeactivateAsync() => deactivation ??= DeactivateCoreAsync();
    private async Task DeactivateCoreAsync()
    {
        deactivated = true;
        lifetime.Cancel();
        navigationCancellation?.Cancel(); metadataCancellation?.Cancel(); playCancellation?.Cancel();
        Task[] pending;
        lock (operations) pending = operations.ToArray();
        try { await Task.WhenAll(pending); }
        catch (OperationCanceledException) { }
        finally
        {
            await previewMonitor;
            try { await preview.StopAsync(CancellationToken.None); }
            finally { await preview.DisposeAsync(); }
            foreach (var entry in observed.ToArray()) Forget(entry);
            await uiScheduler.InvokeAsync(() => { IsPreviewPlaying = false; IsNavigating = false; IsMetadataLoading = false; NotifyState(); }, CancellationToken.None);
            externalCancellation.Dispose();
            navigationCancellation?.Dispose(); metadataCancellation?.Dispose(); playCancellation?.Dispose(); lifetime.Dispose();
        }
    }
}
