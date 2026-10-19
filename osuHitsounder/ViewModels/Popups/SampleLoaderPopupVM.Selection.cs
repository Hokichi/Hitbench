using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Services.FileSystem;

namespace osuHitsounder.ViewModels.Popups;

public sealed partial class SampleLoaderPopupVM
{
    private readonly ObservableCollection<SampleBrowserEntry> selectedSamples = [];
    private readonly ObservableCollection<SampleBrowserEntry> visibleEntries = [];
    private ReadOnlyObservableCollection<SampleBrowserEntry>? selectedView, visibleView;
    private SampleBrowserEntry? rangeAnchor;
    private bool synchronizing;
    private readonly SemaphoreSlim metadataGate = new(2, 2);
    private Task listingMetadata = Task.CompletedTask;

    private async Task<SampleFileMetadata> ReadMetadataBoundedAsync(string path, CancellationToken token)
    {
        await metadataGate.WaitAsync(token);
        try { return await browser.ReadMetadataAsync(path, token); }
        finally { metadataGate.Release(); }
    }

    private void StartListingMetadata(int generation, CancellationToken token)
    {
        var files = Entries.Where(x => !x.IsDirectory).ToArray();
        listingMetadata = OwnAsync(async () =>
        {
            try
            {
                await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = token }, async (entry, cancellation) =>
                {
                    try
                    {
                        await UpdateAsync(() => { if (generation == navigationGeneration) entry.IsMetadataLoading = true; });
                        var metadata = await ReadMetadataBoundedAsync(entry.FullPath, cancellation);
                        if (deactivated || cancellation.IsCancellationRequested || generation != navigationGeneration) return;
                        await UpdateAsync(() =>
                        {
                            if (generation != navigationGeneration || cancellation.IsCancellationRequested) return;
                            entry.Metadata = metadata; entry.IsMetadataUnavailable = false; entry.IsMetadataLoading = false; NotifyState();
                        });
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception error)
                    {
                        if (!deactivated && generation == navigationGeneration)
                            await ReportAsync(() => { if (generation != navigationGeneration) return; entry.Metadata = null; entry.IsMetadataUnavailable = true; entry.IsMetadataLoading = false; NotifyState(); });
                        System.Diagnostics.Trace.TraceWarning("Sample metadata unavailable: {0}", error.Message);
                    }
                    finally
                    {
                        if (!deactivated && generation == navigationGeneration)
                            await ReportAsync(() => { if (generation == navigationGeneration) { entry.IsMetadataLoading = false; NotifyState(); } });
                    }
                });
            }
            catch (OperationCanceledException) { }
        });
    }
    [ObservableProperty] private string searchText = "";
    public ReadOnlyObservableCollection<SampleBrowserEntry> SelectedSamples => selectedView ??= new(selectedSamples);
    public ReadOnlyObservableCollection<SampleBrowserEntry> VisibleEntries => visibleView ??= new(visibleEntries);
    public string SelectionAnnouncement => string.Format(CultureInfo.CurrentCulture, CheckedCount == 1 ? "{0} sample selected" : "{0} samples selected", CheckedCount);
    public string SelectionSummary => string.Format(CultureInfo.CurrentCulture, CheckedCount == 1 ? "{0} sample" : "{0} samples", CheckedCount);
    public string SelectedDurationText => selectedSamples.Any(x => x.Metadata is null || x.IsMetadataLoading || x.IsMetadataUnavailable) ? "—" :
        string.Format(CultureInfo.CurrentCulture, "{0:0.000} s", selectedSamples.Sum(x => x.Metadata!.Duration.TotalSeconds));
    partial void OnSearchTextChanged(string value) { rangeAnchor = null; RefreshVisibleEntries(); }
    private void RefreshVisibleEntries()
    {
        visibleEntries.Clear();
        foreach (var entry in Entries.Where(x => x.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase))) visibleEntries.Add(entry);
    }
    private void SynchronizeChecked(SampleBrowserEntry entry)
    {
        if (synchronizing || IsFolderSelection || entry.IsDirectory) return;
        synchronizing = true;
        try
        {
            var existing = selectedSamples.FirstOrDefault(x => SamePath(x.FullPath, entry.FullPath));
            if (entry.IsChecked)
            {
                if (SelectionMode == SampleSelectionMode.Single)
                    foreach (var other in selectedSamples.Where(x => !SamePath(x.FullPath, entry.FullPath)).ToArray())
                    { other.IsChecked = false; selectedSamples.Remove(other); }
                if (existing is null) { selectedSamples.Add(entry); Observe(entry); }
            }
            else if (existing is not null) { existing.IsChecked = false; selectedSamples.Remove(existing); }
            foreach (var visible in Entries.Where(x => SamePath(x.FullPath, entry.FullPath))) visible.IsChecked = entry.IsChecked;
        }
        finally { synchronizing = false; }
        NotifyState();
    }
    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private void RemoveSelected(SampleBrowserEntry entry) { entry.IsChecked = false; SynchronizeChecked(entry); }
    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private void ClearSelected()
    {
        foreach (var entry in selectedSamples.ToArray()) { entry.IsChecked = false; SynchronizeChecked(entry); }
    }
    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private Task ActivateSelectedAsync(SampleBrowserEntry entry) => OwnAsync(async () =>
    {
        SearchText = "";
        await SelectCoreAsync(entry);
    });
    public void SetRangeAnchor(SampleBrowserEntry entry) => rangeAnchor = entry;
    public Task SetRangeCheckedAsync(SampleBrowserEntry entry, bool value) => OwnAsync(async () =>
    {
        if (IsFolderSelection || SelectionMode == SampleSelectionMode.Single || !CanNavigate) return;
        var start = rangeAnchor is null ? -1 : visibleEntries.IndexOf(rangeAnchor);
        var end = visibleEntries.IndexOf(entry);
        if (end < 0) return;
        if (start < 0) start = end;
        foreach (var item in visibleEntries.Skip(Math.Min(start, end)).Take(Math.Abs(start - end) + 1).Where(x => !x.IsDirectory))
        { item.IsChecked = value; SynchronizeChecked(item); }
        await SelectCoreAsync(entry);
    });
    private bool IsImportedPath(string path)
    {
        if (Context?.ProjectPath is not { } project) return false;
        try { return Path.GetFullPath(path).StartsWith(SamplePaths.Root(project) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase); }
        catch (ArgumentException) { return false; }
    }
}
