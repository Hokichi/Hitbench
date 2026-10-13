using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Core.Constants;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Resources.Messages;
using osuHitsounder.Services.Dialogs;
using osuHitsounder.Services.FileSystem;
using osuHitsounder.Services.Ui;

namespace osuHitsounder.ViewModels.Popups;

public sealed partial class KeysMappingDrawerVM : ObservableObject
{
    private InstrumentalTrackSnapshot snapshot;
    private readonly IMessenger messenger;
    private readonly IUiScheduler scheduler;
    private readonly ISampleFileService files;
    private readonly ISampleFileDialogService picker;
    private readonly IMappingConflictDialogService? conflicts;
    private readonly CancellationTokenSource lifetime = new();
    private readonly HashSet<Task> operations = [];
    private readonly SemaphoreSlim edits = new(1, 1);
    private Task? deactivation, closeFlush;
    private bool closed, closing, pickerActive;
    private long saveVersion = -1;
    private Guid pendingId;
    private string? pendingKey;
    private bool accepted;
    public ReadOnlyObservableCollection<KeyMappingEntry> Rows { get; }
    public string TrackName => snapshot.Name;
    public bool CanOpenKey => !closed && !closing && !IsBusy;
    public bool CanClose => !closed && !pickerActive;
    public bool CanEditMapping => !closed && !closing && !IsBusy;
    public string SaveStatus => saveVersion < 0 ? "Checking save status…" : SaveError is not null ? "Save failed" : IsSaving ? "Saving…" : "Autosaved";
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(OpenKeyCommand))][NotifyCanExecuteChangedFor(nameof(OpenFolderCommand))][NotifyPropertyChangedFor(nameof(CanEditMapping))] private bool isBusy;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(SaveStatus))] private bool isSaving;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(SaveStatus))] private string? saveError;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private string? importStatus;
    public KeysMappingDrawerVM(InstrumentalTrackSnapshot initial, IMessenger messenger, IUiScheduler scheduler, ISampleFileService files, ISampleFileDialogService picker, IMappingConflictDialogService? conflicts = null)
    {
        snapshot = initial; this.messenger = messenger; this.scheduler = scheduler; this.files = files; this.picker = picker; this.conflicts = conflicts;
        Rows = new(new ObservableCollection<KeyMappingEntry>(InstrumentalKeys.All.Select(k => new KeyMappingEntry(k))));
        ApplySnapshot(initial);
        messenger.Register<InstrumentalTrackChangedMessage>(this, (_, m) => { if (!closed && m.Snapshot.Context == snapshot.Context && m.Snapshot.Revision >= snapshot.Revision) ApplySnapshot(m.Snapshot); });
        messenger.Register<InstrumentalEditResultMessage>(this, (_, m) =>
        {
            if (closed || m.Context != snapshot.Context || m.RequestId != pendingId) return;
            accepted = m.Error is null;
            var row = Rows.FirstOrDefault(r => r.Key == pendingKey);
            if (row is not null) { row.Error = m.Error; if (accepted) row.DraftFileName = row.FileName ?? ""; }
            else if (m.Error is not null) ErrorMessage = m.Error;
        });
        messenger.Register<InstrumentalSaveStateChangedMessage>(this, (_, m) =>
        { if (closed || m.Context != snapshot.Context || m.Version < saveVersion) return; saveVersion = m.Version; IsSaving = m.IsSaving; SaveError = m.Error; OnPropertyChanged(nameof(SaveStatus)); });
        messenger.Send(new InstrumentalEditorReadyMessage(snapshot.Context));
    }
    private void ApplySnapshot(InstrumentalTrackSnapshot next)
    {
        snapshot = next; OnPropertyChanged(nameof(TrackName));
        foreach (var row in Rows)
        {
            var dirty = row.DraftFileName != (row.FileName ?? ""); next.KeyFiles.TryGetValue(row.Key, out var name); row.FileName = name;
            if (!dirty) row.DraftFileName = name ?? "";
            row.IsMissing = name is not null && next.AvailableFiles is not null && !next.AvailableFiles.Contains(name, StringComparer.OrdinalIgnoreCase);
        }
    }
    private bool ApplyMapping(string key, string? name, string? folderName)
    {
        pendingId = Guid.NewGuid(); pendingKey = key; accepted = false;
        messenger.Send(new SetInstrumentalKeyMappingRequestedMessage(snapshot.Context, pendingId, key, name, folderName));
        return accepted;
    }
    public Task<bool> CommitMappingAsync(string key, string draft, CancellationToken token = default) => OwnAsync(() => CommitCoreAsync(key, draft, token));
    private async Task<bool> CommitCoreAsync(string key, string draft, CancellationToken token, bool allowClosing = false)
    {
        if (closed || closing && !allowClosing) return false;
        var row = Rows.FirstOrDefault(r => r.Key == key); if (row is null) return false;
        var name = string.IsNullOrWhiteSpace(draft) ? null : draft.Replace('\\', '/');
        var error = name is null ? null : SamplePaths.GetRelativePathError(name);
        if (error is not null) { row.Error = error + " Previous mapping is retained."; return false; }
        if (name == row.FileName) return true;
        if (name is null) return ApplyMapping(key, null, null);
        using var cancellation = allowClosing ? CancellationTokenSource.CreateLinkedTokenSource(token) : CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        var created = "";
        await edits.WaitAsync(cancellation.Token);
        IsBusy = true;
        try
        {
            var folder = await files.EnsureFolderAsync(snapshot.Context.ProjectPath, snapshot.Name, snapshot.FolderName, snapshot.ReservedFolderNames, cancellation.Token);
            if (snapshot.FolderName.Length == 0) created = folder;
            var target = folder + "/" + Path.GetFileName(name);
            var known = (snapshot.AvailableFiles ?? []).FirstOrDefault(f => string.Equals(f, target, StringComparison.OrdinalIgnoreCase))
                ?? (snapshot.AvailableFiles ?? []).FirstOrDefault(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase));
            if (known is not null && !string.Equals(known, target, StringComparison.OrdinalIgnoreCase))
                target = await files.ImportAsync(snapshot.Context.ProjectPath, SamplePaths.Resolve(snapshot.Context.ProjectPath, known), cancellation.Token, folder);
            else if (known is not null) target = known;
            cancellation.Token.ThrowIfCancellationRequested();
            await RefreshFilesAsync([target], cancellation.Token);
            return await OnUiAsync(() => !closed && (!closing || allowClosing) && ApplyMapping(key, target, folder), cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return false; }
        catch (Exception failure) { row.Error = failure.Message + " Previous mapping is retained."; return false; }
        finally
        {
            if (created.Length > 0 && snapshot.FolderName.Length == 0) await CleanupFolderAsync(created);
            IsBusy = false; edits.Release(); OnPropertyChanged(nameof(CanClose));
        }
    }
    [RelayCommand(CanExecute = nameof(CanOpenKey))]
    public Task OpenKeyAsync(string key, CancellationToken token) => CanOpenKey ? OwnAsync(() => ImportKeyAsync(key, token)) : Task.CompletedTask;
    private async Task ImportKeyAsync(string key, CancellationToken token)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        var created = ""; IsBusy = true; ErrorMessage = null;
        try
        {
            pickerActive = true; OnPropertyChanged(nameof(CanClose));
            IReadOnlyList<string> selected;
            try { selected = await picker.SelectAsync(snapshot.Context.InitialDirectory, cancel.Token, SampleSelectionMode.Single); }
            finally { pickerActive = false; OnPropertyChanged(nameof(CanClose)); }
            cancel.Token.ThrowIfCancellationRequested(); if (selected.Count == 0) return;
            if (selected.Count != 1) throw new InvalidOperationException("Select one file for this key.");
            var folder = await files.EnsureFolderAsync(snapshot.Context.ProjectPath, snapshot.Name, snapshot.FolderName, snapshot.ReservedFolderNames, cancel.Token);
            if (snapshot.FolderName.Length == 0) created = folder;
            var name = await files.ImportAsync(snapshot.Context.ProjectPath, selected[0], cancel.Token, folder);
            cancel.Token.ThrowIfCancellationRequested();
            await RefreshFilesAsync([name], cancel.Token);
            await scheduler.InvokeAsync(() => { if (!closed && !closing && !cancel.IsCancellationRequested) ApplyMapping(key, name, folder); }, cancel.Token);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        catch (Exception error) { ErrorMessage = error.Message; }
        finally
        {
            if (created.Length > 0 && snapshot.FolderName.Length == 0) await CleanupFolderAsync(created);
            IsBusy = false; OnPropertyChanged(nameof(CanClose));
        }
    }
    private async Task RefreshFilesAsync(IReadOnlyList<string> imported, CancellationToken token)
    {
        IReadOnlyList<string> known;
        try { known = await files.ListAsync(snapshot.Context.ProjectPath, token); }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) { ErrorMessage = "Imported sample; could not refresh folder: " + error.Message; known = (snapshot.AvailableFiles ?? []).Concat(imported).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(); }
        await scheduler.InvokeAsync(() => { if (!closed) messenger.Send(new ProjectSamplesChangedMessage(new(snapshot.Context.ProjectPath, snapshot.Context.InitialDirectory, snapshot.Context.ProjectSessionId), known)); }, token);
    }
    private async Task CleanupFolderAsync(string folder)
    { try { await files.DeleteEmptyFolderAsync(snapshot.Context.ProjectPath, folder, CancellationToken.None); } catch (Exception error) { ErrorMessage = "Could not remove empty sample folder: " + error.Message; } }
    private async Task<bool> OnUiAsync(Func<bool> action, CancellationToken token)
    { var result = false; await scheduler.InvokeAsync(() => result = action(), token); return result; }
    private async Task<T> OwnAsync<T>(Func<Task<T>> action)
    {
        if (closed || closing) return default!;
        var task = action(); operations.Add(task);
        try { return await task; } finally { operations.Remove(task); }
    }
    private async Task OwnAsync(Func<Task> action)
    {
        if (closed || closing) return;
        var task = action(); operations.Add(task);
        try { await task; } finally { operations.Remove(task); }
    }
    public void PrepareClose()
    {
        if (closed || closing) return; closing = true; lifetime.Cancel(); OnPropertyChanged(nameof(CanEditMapping));
        closeFlush = FlushCloseAsync();
    }
    private async Task FlushCloseAsync()
    {
        await Task.WhenAll(operations.ToArray());
        foreach (var row in Rows.Where(r => r.DraftFileName != (r.FileName ?? "")).ToArray()) await CommitCoreAsync(row.Key, row.DraftFileName, default, true);
    }
    public async Task PrepareCloseAsync() { PrepareClose(); if (closeFlush is not null) await closeFlush; }
    public Task DeactivateAsync() => deactivation ??= DeactivateCoreAsync();
    private async Task DeactivateCoreAsync()
    {
        try { await PrepareCloseAsync(); }
        finally { closed = true; messenger.UnregisterAll(this); edits.Dispose(); lifetime.Dispose(); }
    }
}
