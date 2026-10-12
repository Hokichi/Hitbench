using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Services;
using osuHitsounder.Services.FileSystem;
using osuHitsounder.Services.Ui;

namespace osuHitsounder.ViewModels.Popups;

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
    public SampleSelectionMode SelectionMode { get; private set; }
    public void ConfigureSelectionMode(SampleSelectionMode mode) => SelectionMode=mode;
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
    [ObservableProperty] private string? directoryError;
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

    public int CheckedCount => Entries.Count(entry => !entry.IsDirectory && entry.IsChecked);
    public string CheckedCountText => string.Format(CultureInfo.CurrentCulture, "{0:N0} files checked", CheckedCount);
    public bool CanNavigate => !deactivated && !IsConfirming;
    public bool CanImport => !deactivated && !IsNavigating && !IsConfirming && CheckedCount > 0 && (SelectionMode!=SampleSelectionMode.Single || CheckedCount==1);
    public bool CanPreview => !deactivated && !IsNavigating && !IsConfirming && !IsMetadataLoading && !isStartingPreview && !IsSeeking && Metadata is not null;
    public string BrowserStatusText => IsNavigating ? "Loading folder..." : CurrentDirectory.Length == 0 ? Entries.Count == 0 ? "No drives to display" : "Choose a drive." : "";
    public string PreviewActionText => IsPreviewPlaying ? "Stop" : "Play";
    public string MetadataStatusText => IsMetadataLoading ? "Reading metadata..." : SelectedFile is null ? "Select a file to inspect and preview." : "";
    public string SizeText => Metadata is { } data ? string.Format(CultureInfo.CurrentCulture, "{0:N1} KiB", data.Length / 1024d) : "";
    public string DurationText => Metadata is { } data ? string.Format(CultureInfo.CurrentCulture, "{0:N3} s", data.Duration.TotalSeconds) : "";

    public Task InitializeAsync(CancellationToken token) => InitializeAsync(null, token);

    public Task InitializeAsync(string? initialDirectory, CancellationToken token) => OwnAsync(async () =>
    {
        externalCancellation = token.Register(lifetime.Cancel);
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
            await StopPreviewAsync();
            await UpdateAsync(() =>
            {
                if (request != navigationGeneration || deactivated || cancellation.IsCancellationRequested) return;
                foreach (var entry in observed.ToArray()) { entry.IsChecked = false; entry.IsSelected = false; Forget(entry); }
                Entries.Clear();
                foreach (var child in children) { Entries.Add(child); Observe(child); }
                BuildBreadcrumbs(path);
                CurrentDirectory = path;
                SelectedFile = null;
                Metadata = null;
                MetadataError = null;
                IsMetadataLoading = false;
                NotifyState();
            });
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
        await StopPreviewAsync();
        if (request == selectionGeneration && !deactivated && AutoPlaySamples)
            await StartPreviewAsync();
    });

    private async Task SelectCoreAsync(SampleBrowserEntry entry)
    {
        if (deactivated || IsConfirming) return;
        if (entry.IsDirectory) { ++selectionGeneration; metadataCancellation?.Cancel(); await StopPreviewAsync(); await UpdateAsync(() => { SelectedFile = null; Metadata = null; MetadataError = null; IsMetadataLoading = false; NotifyState(); }); return; }
        var parent = Path.GetDirectoryName(entry.FullPath)!;
        if (!SamePath(parent, CurrentDirectory)) await NavigateToAsync(parent);
        if (deactivated || !SamePath(parent, CurrentDirectory)) return;
        var actual = Entries.FirstOrDefault(child => SamePath(child.FullPath, entry.FullPath));
        if (actual is null || actual.IsDirectory || SamePath(SelectedFile?.FullPath, actual.FullPath)) return;
        var request = ++selectionGeneration;
        metadataCancellation?.Cancel();
        metadataCancellation?.Dispose();
        var cancellation = metadataCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        await StopPreviewAsync();
        if (request != selectionGeneration || deactivated) return;
        await UpdateAsync(() =>
        {
            SelectedFile = actual;
            foreach (var item in observed) item.IsSelected = false;
            actual.IsSelected = true;
            Metadata = null;
            MetadataError = null;
            IsMetadataLoading = true;
            NotifyState();
        });
        try
        {
            var result = await browser.ReadMetadataAsync(actual.FullPath, cancellation.Token);
            if (request == selectionGeneration && !deactivated) await UpdateAsync(() => { Metadata = result; NotifyState(); });
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (request == selectionGeneration) await ReportAsync(() => MetadataError = error.Message); }
        finally { if (request == selectionGeneration) await ReportAsync(() => { IsMetadataLoading = false; NotifyState(); }); }
        if (request == selectionGeneration && !deactivated && AutoPlaySamples && CanPreview)
            await StartPreviewAsync();
    }

    public Task SetCheckedAsync(SampleBrowserEntry entry, bool value) => OwnAsync(async () =>
    {
        if (entry.IsDirectory || deactivated || IsConfirming) return;
        await SelectCoreAsync(entry);
        await UpdateAsync(() =>
        {
            var actual = Entries.FirstOrDefault(child => SamePath(child.FullPath, entry.FullPath));
            if(value && SelectionMode==SampleSelectionMode.Single)
                foreach(var other in Entries.Where(child=>!ReferenceEquals(child,actual))) other.IsChecked=false;
            if (actual is not null && !actual.IsDirectory) actual.IsChecked = value;
            NotifyState();
        });
    });

    [RelayCommand(CanExecute = nameof(CanPreview))]
    private Task TogglePreviewAsync() => OwnAsync(async () =>
    {
        if (IsPreviewPlaying) { await StopPreviewAsync(); return; }
        await StartPreviewAsync();
    });

    private async Task StartPreviewAsync()
    {
        if (!CanPreview || SelectedFile is not { } file) return;
        playCancellation?.Cancel();
        playCancellation?.Dispose();
        var cancellation = playCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var request = selectionGeneration;
        var entered = false;
        try
        {
            await UpdateAsync(() => { isStartingPreview = true; PreviewError = null; PreviewWarning = null; NotifyState(); });
            await previewGate.WaitAsync(cancellation.Token);
            entered = true;
            if (request != selectionGeneration || deactivated) return;
            var start = PlaybackPositionSeconds >= PlaybackDurationSeconds ? 0 : PlaybackPositionSeconds;
            await preview.PlayAsync(file.FullPath, volumes.ApplicationVolume, cancellation.Token, TimeSpan.FromSeconds(start));
            if (request != selectionGeneration || deactivated || cancellation.IsCancellationRequested) { await preview.StopAsync(CancellationToken.None); return; }
            await UpdateAsync(() => { playbackPositionSeconds = start; IsPreviewPlaying = true; NotifyState(); });
            previewMonitor = MonitorPreviewAsync(request, cancellation.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (request == selectionGeneration) await ReportAsync(() => { PreviewError = error.Message; IsPreviewPlaying = false; NotifyState(); });
        }
        finally
        {
            await ReportAsync(() => { isStartingPreview = false; NotifyState(); });
            if (entered) previewGate.Release();
        }
    }

    public Task SeekAsync(double seconds) => OwnAsync(async () =>
    {
        if (!CanSeek || !double.IsFinite(seconds)) return;
        var request = selectionGeneration;
        var target = Math.Clamp(seconds, 0, PlaybackDurationSeconds);
        if (!IsPreviewPlaying)
        {
            await UpdateAsync(() => { playbackPositionSeconds = target; NotifyState(); });
            return;
        }
        var entered = false;
        var wasPlaying = true;
        playCancellation?.Cancel();
        playCancellation?.Dispose();
        var cancellation = playCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        try
        {
            await UpdateAsync(() => { isSeeking = true; PreviewError = null; NotifyState(); });
            await previewGate.WaitAsync(cancellation.Token);
            entered = true;
            await previewMonitor;
            if (request != selectionGeneration || deactivated) return;
            await preview.SeekAsync(TimeSpan.FromSeconds(target), cancellation.Token);
            if (request != selectionGeneration || deactivated || cancellation.IsCancellationRequested) return;
            await UpdateAsync(() => { playbackPositionSeconds = target; NotifyState(); });
        }
        catch (OperationCanceledException) { wasPlaying = false; }
        catch (Exception error)
        {
            wasPlaying = false;
            if (request == selectionGeneration) await ReportAsync(() => { PreviewError = error.Message; IsPreviewPlaying = false; NotifyState(); });
        }
        finally
        {
            await ReportAsync(() => { isSeeking = false; NotifyState(); });
            if (entered) previewGate.Release();
            if (wasPlaying && request == selectionGeneration && !deactivated && !cancellation.IsCancellationRequested)
                previewMonitor = MonitorPreviewAsync(request, cancellation.Token);
        }
    });

    private async Task MonitorPreviewAsync(int request, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var state = await preview.GetSnapshotAsync(token);
                if (request != selectionGeneration || deactivated) return;
                if (token.IsCancellationRequested) return;
                await UpdateAsync(() =>
                {
                    if (request != selectionGeneration || token.IsCancellationRequested) return;
                    playbackPositionSeconds = Math.Clamp(state.Position.TotalSeconds, 0, PlaybackDurationSeconds);
                    IsPreviewPlaying = state.IsPlaying; PreviewError = state.Error; PreviewWarning = state.Warning; NotifyState();
                });
                if (!state.IsPlaying) return;
                await Task.Delay(100, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { await ReportAsync(() => { PreviewError = error.Message; IsPreviewPlaying = false; NotifyState(); }); }
    }

    private async Task StopPreviewAsync()
    {
        playCancellation?.Cancel();
        await previewMonitor;
        await previewGate.WaitAsync();
        try { await preview.StopAsync(CancellationToken.None); }
        finally { previewGate.Release(); }
        await ReportAsync(() => { playbackPositionSeconds = 0; IsPreviewPlaying = false; PreviewError = null; PreviewWarning = null; NotifyState(); });
    }

    public async Task<IReadOnlyList<string>> ConfirmAsync()
    {
        if (!CanImport) return Array.Empty<string>();
        IsConfirming = true;
        NotifyState();
        var result = Entries.Where(entry => !entry.IsDirectory && entry.IsChecked).Select(entry => entry.FullPath).ToArray();
        try { await StopPreviewAsync(); return deactivated ? Array.Empty<string>() : Array.AsReadOnly(result); }
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
        if (args.PropertyName == nameof(SampleBrowserEntry.IsChecked)) NotifyState();
    }
    private void NotifyState()
    {
        foreach (var name in new[] { nameof(CheckedCount), nameof(CheckedCountText), nameof(CanImport), nameof(CanPreview), nameof(BrowserStatusText),
            nameof(PreviewActionText), nameof(MetadataStatusText), nameof(SizeText), nameof(DurationText),
            nameof(PlaybackPositionSeconds), nameof(PlaybackDurationSeconds), nameof(PlaybackTimeText), nameof(CanSeek), nameof(IsSeeking), nameof(IsThisPc), nameof(IsFolderEmpty) }) OnPropertyChanged(name);
        TogglePreviewCommand.NotifyCanExecuteChanged();
        NavigateCommand.NotifyCanExecuteChanged();
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
    private static bool SamePath(string? left, string? right) => left is not null && right is not null &&
        string.Equals(Path.TrimEndingDirectorySeparator(left), Path.TrimEndingDirectorySeparator(right), StringComparison.OrdinalIgnoreCase);

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
