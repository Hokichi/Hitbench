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
    private CancellationTokenSource? navigationCancellation, metadataCancellation, playCancellation;
    private Task previewMonitor = Task.CompletedTask;
    private CancellationTokenRegistration externalCancellation;
    private Task? deactivation;
    private int navigationGeneration, selectionGeneration;
    private bool deactivated;
    private SampleBrowserEntry? currentFolder;
    public ObservableCollection<SampleBrowserEntry> Roots { get; } = [];

    [ObservableProperty] private string directoryText = "";
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

    public int CheckedCount => currentFolder?.Children.Count(entry => !entry.IsDirectory && entry.IsChecked) ?? 0;
    public string CheckedCountText => string.Format(CultureInfo.CurrentCulture, "{0:N0} files checked", CheckedCount);
    public bool CanImport => !deactivated && !IsNavigating && !IsConfirming && CheckedCount > 0;
    public bool CanPreview => !deactivated && !IsNavigating && !IsConfirming && !IsMetadataLoading && Metadata is not null;
    public string BrowserStatusText => IsNavigating ? "Loading folder..." : CurrentDirectory.Length == 0 ? "Choose a folder." :
        currentFolder?.Children.Any(entry => !entry.IsDirectory && !entry.IsPlaceholder) == true ? "" : "No WAV, OGG, or MP3 files in this folder.";
    public string PreviewActionText => IsPreviewPlaying ? "Stop" : "Play";
    public string MetadataStatusText => IsMetadataLoading ? "Reading metadata..." : SelectedFile is null ? "Select a file to inspect and preview." : "";
    public string SizeText => Metadata is { } data ? string.Format(CultureInfo.CurrentCulture, "{0:N1} KiB", data.Length / 1024d) : "";
    public string DurationText => Metadata is { } data ? string.Format(CultureInfo.CurrentCulture, "{0:N3} s", data.Duration.TotalSeconds) : "";
    public string SampleRateText => Metadata is { } data ? string.Format(CultureInfo.CurrentCulture, "{0:N0} Hz", data.SampleRate) : "";
    public string ChannelsText => Metadata?.Channels.ToString(CultureInfo.CurrentCulture) ?? "";

    public Task InitializeAsync(CancellationToken token) => InitializeAsync(null, token);

    public Task InitializeAsync(string? initialDirectory, CancellationToken token) => OwnAsync(async () =>
    {
        externalCancellation = token.Register(lifetime.Cancel);
        try
        {
            var roots = await browser.GetRootsAsync(lifetime.Token);
            await UpdateAsync(() => { foreach (var root in roots) { Roots.Add(root); Observe(root); } });
            var folder = string.IsNullOrWhiteSpace(initialDirectory)
                ? await browser.GetDefaultDirectoryAsync(lifetime.Token) : initialDirectory;
            await UpdateAsync(() => DirectoryText = folder);
            await NavigateToAsync(folder);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { await ReportAsync(() => DirectoryError = error.Message); }
    });

    public Task ExpandAsync(SampleBrowserEntry entry) => OwnAsync(async () =>
    {
        if (deactivated || !entry.IsDirectory || entry.IsLoaded || entry.IsLoading) return;
        await UpdateAsync(() => { entry.IsLoading = true; entry.Error = null; });
        try
        {
            var children = await browser.GetEntriesAsync(entry.FullPath, lifetime.Token);
            await UpdateAsync(() => { ReplaceChildren(entry, children); entry.IsExpanded = true; });
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { await ReportAsync(() => entry.Error = error.Message); }
        finally { await ReportAsync(() => entry.IsLoading = false); }
    });

    [RelayCommand]
    private Task NavigateAsync() => OwnAsync(() => NavigateToAsync(DirectoryText));

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
            path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
            if (SamePath(path, CurrentDirectory))
            {
                await UpdateAsync(() => { DirectoryText = CurrentDirectory; if (currentFolder is not null) currentFolder.IsSelected = true; });
                return;
            }
            var children = await browser.GetEntriesAsync(path, cancellation.Token);
            if (request != navigationGeneration || deactivated) return;
            var folder = await MaterializeDirectoryAsync(path, cancellation.Token);
            if (request != navigationGeneration || deactivated) return;
            ++selectionGeneration;
            metadataCancellation?.Cancel();
            await StopPreviewAsync();
            await UpdateAsync(() =>
            {
                if (request != navigationGeneration) return;
                if (currentFolder is not null)
                    foreach (var entry in currentFolder.Children) entry.IsChecked = false;
                foreach (var entry in observed.ToArray()) { entry.IsChecked = false; entry.IsSelected = false; }
                ReplaceChildren(folder, children);
                currentFolder = folder;
                CurrentDirectory = path;
                DirectoryText = path;
                folder.IsExpanded = true;
                folder.IsSelected = true;
                SelectedFile = null;
                Metadata = null;
                MetadataError = null;
                IsMetadataLoading = false;
                NotifyState();
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (request == navigationGeneration) await ReportAsync(() => DirectoryError = error.Message); }
        finally { if (request == navigationGeneration) await ReportAsync(() => { IsNavigating = false; NotifyState(); }); }
    }

    private async Task<SampleBrowserEntry> MaterializeDirectoryAsync(string path, CancellationToken token)
    {
        var root = Roots.Where(entry => IsAncestor(entry.FullPath, path)).OrderByDescending(entry => entry.FullPath.Length).FirstOrDefault();
        if (root is null)
        {
            var rootPath = Path.GetPathRoot(path) ?? path;
            root = Roots.FirstOrDefault(entry => SamePath(entry.FullPath, rootPath));
            if (root is null)
            {
                root = new(rootPath, rootPath, true);
                await uiScheduler.InvokeAsync(() => { Roots.Add(root); Observe(root); }, token);
            }
        }
        var node = root;
        var relative = Path.GetRelativePath(root.FullPath, path);
        foreach (var part in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            token.ThrowIfCancellationRequested();
            if (!node.IsLoaded || !node.Children.Any(entry => entry.IsDirectory && string.Equals(entry.Name, part, StringComparison.OrdinalIgnoreCase)))
            {
                var children = await browser.GetEntriesAsync(node.FullPath, token);
                token.ThrowIfCancellationRequested();
                var parent = node;
                await uiScheduler.InvokeAsync(() => ReplaceChildren(parent, children), token);
            }
            var parentNode = node;
            await uiScheduler.InvokeAsync(() => parentNode.IsExpanded = true, token);
            node = node.Children.FirstOrDefault(entry => entry.IsDirectory && string.Equals(entry.Name, part, StringComparison.OrdinalIgnoreCase))
                ?? throw new DirectoryNotFoundException("The folder is no longer available.");
        }
        return node;
    }

    public Task SelectAsync(SampleBrowserEntry entry) => OwnAsync(() => SelectCoreAsync(entry));

    private async Task SelectCoreAsync(SampleBrowserEntry entry)
    {
        if (deactivated || IsConfirming || entry.IsPlaceholder) return;
        if (entry.IsDirectory) { await NavigateToAsync(entry.FullPath); return; }
        var parent = Path.GetDirectoryName(entry.FullPath)!;
        if (!SamePath(parent, CurrentDirectory)) await NavigateToAsync(parent);
        if (deactivated || !SamePath(parent, CurrentDirectory)) return;
        var actual = currentFolder?.Children.FirstOrDefault(child => SamePath(child.FullPath, entry.FullPath));
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
    }

    public Task SetCheckedAsync(SampleBrowserEntry entry, bool value) => OwnAsync(async () =>
    {
        if (entry.IsDirectory || entry.IsPlaceholder || deactivated || IsConfirming) return;
        await SelectCoreAsync(entry);
        await UpdateAsync(() =>
        {
            var actual = currentFolder?.Children.FirstOrDefault(child => SamePath(child.FullPath, entry.FullPath));
            if (actual is not null && !actual.IsDirectory) actual.IsChecked = value;
            NotifyState();
        });
    });

    [RelayCommand(CanExecute = nameof(CanPreview))]
    private Task TogglePreviewAsync() => OwnAsync(async () =>
    {
        if (IsPreviewPlaying) { await StopPreviewAsync(); return; }
        if (!CanPreview || SelectedFile is not { } file) return;
        playCancellation?.Cancel();
        playCancellation?.Dispose();
        var cancellation = playCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var request = selectionGeneration;
        try
        {
            await UpdateAsync(() => { PreviewError = null; PreviewWarning = null; });
            await preview.PlayAsync(file.FullPath, volumes.ApplicationVolume, cancellation.Token);
            if (request != selectionGeneration || deactivated || cancellation.IsCancellationRequested) { await preview.StopAsync(CancellationToken.None); return; }
            await UpdateAsync(() => { IsPreviewPlaying = true; NotifyState(); });
            previewMonitor = MonitorPreviewAsync(request, cancellation.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { await ReportAsync(() => { PreviewError = error.Message; IsPreviewPlaying = false; NotifyState(); }); }
    });

    private async Task MonitorPreviewAsync(int request, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var state = await preview.GetSnapshotAsync(token);
                if (request != selectionGeneration || deactivated) return;
                await UpdateAsync(() => { IsPreviewPlaying = state.IsPlaying; PreviewError = state.Error; PreviewWarning = state.Warning; NotifyState(); });
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
        await preview.StopAsync(CancellationToken.None);
        await ReportAsync(() => { IsPreviewPlaying = false; PreviewError = null; PreviewWarning = null; NotifyState(); });
    }

    public async Task<IReadOnlyList<string>> ConfirmAsync()
    {
        if (!CanImport) return Array.Empty<string>();
        IsConfirming = true;
        NotifyState();
        var result = currentFolder!.Children.Where(entry => !entry.IsDirectory && entry.IsChecked).Select(entry => entry.FullPath).ToArray();
        try { await StopPreviewAsync(); return deactivated ? Array.Empty<string>() : Array.AsReadOnly(result); }
        catch (Exception error) { PreviewError = error.Message; IsConfirming = false; NotifyState(); return Array.Empty<string>(); }
    }

    private void ReplaceChildren(SampleBrowserEntry entry, IReadOnlyList<SampleBrowserEntry> children)
    {
        foreach (var child in entry.Children) Forget(child);
        entry.Children.Clear();
        foreach (var child in children) { entry.Children.Add(child); Observe(child); }
        entry.IsLoaded = true;
        entry.Error = null;
    }
    private void Observe(SampleBrowserEntry entry)
    {
        if (observed.Add(entry)) entry.PropertyChanged += EntryChanged;
        foreach (var child in entry.Children) Observe(child);
    }
    private void Forget(SampleBrowserEntry entry)
    {
        if (observed.Remove(entry)) entry.PropertyChanged -= EntryChanged;
        foreach (var child in entry.Children) Forget(child);
    }
    private void EntryChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SampleBrowserEntry.IsChecked)) NotifyState();
    }
    private void NotifyState()
    {
        foreach (var name in new[] { nameof(CheckedCount), nameof(CheckedCountText), nameof(CanImport), nameof(CanPreview), nameof(BrowserStatusText),
            nameof(PreviewActionText), nameof(MetadataStatusText), nameof(SizeText), nameof(DurationText), nameof(SampleRateText), nameof(ChannelsText) }) OnPropertyChanged(name);
        TogglePreviewCommand.NotifyCanExecuteChanged();
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
    private static bool IsAncestor(string parent, string path) => SamePath(parent, path) ||
        path.StartsWith(Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

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
            foreach (var root in Roots) Forget(root);
            await uiScheduler.InvokeAsync(() => { IsPreviewPlaying = false; IsNavigating = false; IsMetadataLoading = false; NotifyState(); }, CancellationToken.None);
            externalCancellation.Dispose();
            navigationCancellation?.Dispose(); metadataCancellation?.Dispose(); playCancellation?.Dispose(); lifetime.Dispose();
        }
    }
}
