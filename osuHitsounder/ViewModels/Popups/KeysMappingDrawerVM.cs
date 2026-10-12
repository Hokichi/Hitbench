using System.Collections.ObjectModel;
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
    private readonly CancellationTokenSource lifetime = new();
    private Task? import;
    private Task? deactivation;
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
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(OpenKeyCommand))][NotifyPropertyChangedFor(nameof(CanEditMapping))] private bool isBusy;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(SaveStatus))] private bool isSaving;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(SaveStatus))] private string? saveError;
    [ObservableProperty] private string? errorMessage;
    public KeysMappingDrawerVM(InstrumentalTrackSnapshot initial, IMessenger messenger, IUiScheduler scheduler, ISampleFileService files, ISampleFileDialogService picker)
    {
        snapshot = initial; this.messenger = messenger; this.scheduler = scheduler; this.files = files; this.picker = picker;
        Rows = new(new ObservableCollection<KeyMappingEntry>(InstrumentalKeys.All.Select(k => new KeyMappingEntry(k))));
        ApplySnapshot(initial);
        messenger.Register<InstrumentalTrackChangedMessage>(this, (_, m) => { if (!closed && m.Snapshot.Context == snapshot.Context && m.Snapshot.Revision >= snapshot.Revision) ApplySnapshot(m.Snapshot); });
        messenger.Register<InstrumentalEditResultMessage>(this, (_, m) =>
        {
            if (closed || m.Context != snapshot.Context || m.RequestId != pendingId) return;
            accepted = m.Error is null; var row = Rows.FirstOrDefault(r => r.Key == pendingKey);
            if (row is not null) { row.Error = m.Error; if (accepted) row.DraftFileName = row.FileName ?? ""; }
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
    public bool CommitMapping(string key, string draft)
    {
        if (closed) return false;
        var row = Rows.FirstOrDefault(r => r.Key == key); if (row is null) return false;
        var name = string.IsNullOrWhiteSpace(draft) ? null : draft;
        var error = name is null ? null : SampleFileNames.GetFileNameError(name);
        if (error is not null) { row.Error = error + " Previous mapping is retained."; return false; }
        pendingId = Guid.NewGuid(); pendingKey = key; accepted = false;
        messenger.Send(new SetInstrumentalKeyMappingRequestedMessage(snapshot.Context, pendingId, key, name));
        return accepted;
    }
    [RelayCommand(CanExecute = nameof(CanOpenKey))]
    public Task OpenKeyAsync(string key, CancellationToken token) => CanOpenKey ? import = ImportKeyAsync(key, token) : Task.CompletedTask;
    private async Task ImportKeyAsync(string key, CancellationToken token)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        IsBusy = true; ErrorMessage = null;
        try
        {
            pickerActive = true; OnPropertyChanged(nameof(CanClose));
            IReadOnlyList<string> selected;
            try { selected = await picker.SelectAsync(snapshot.Context.InitialDirectory, cancel.Token, SampleSelectionMode.Single); }
            finally { pickerActive = false; OnPropertyChanged(nameof(CanClose)); }
            cancel.Token.ThrowIfCancellationRequested(); if (selected.Count == 0) return;
            if (selected.Count != 1) throw new InvalidOperationException("Select one file for this key.");
            var name = await files.ImportAsync(snapshot.Context.ProjectPath, selected[0], cancel.Token);
            cancel.Token.ThrowIfCancellationRequested();
            IReadOnlyList<string>? choices = null;
            try { choices = await files.ListAsync(snapshot.Context.ProjectPath, cancel.Token); }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) { ErrorMessage = "Imported sample; could not refresh folder: " + error.Message; }
            await scheduler.InvokeAsync(() =>
            {
                if (closed || closing || cancel.IsCancellationRequested) return;
                var known = choices ?? (snapshot.AvailableFiles ?? []).Append(name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                messenger.Send(new ProjectSamplesChangedMessage(new(snapshot.Context.ProjectPath, snapshot.Context.InitialDirectory, snapshot.Context.ProjectSessionId), known));
                CommitMapping(key, name);
            }, cancel.Token);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        catch (Exception error) { ErrorMessage = error.Message; }
        finally { IsBusy = false; OnPropertyChanged(nameof(CanClose)); }
    }
    public void PrepareClose()
    {
        if (closed) return; closing = true; lifetime.Cancel(); OnPropertyChanged(nameof(CanEditMapping));
        foreach (var row in Rows.Where(r => r.DraftFileName != (r.FileName ?? ""))) CommitMapping(row.Key, row.DraftFileName);
    }
    public Task DeactivateAsync() => deactivation ??= DeactivateCoreAsync();
    private async Task DeactivateCoreAsync()
    {
        PrepareClose(); closed = true; messenger.UnregisterAll(this);
        try { if (import is not null) await import; }
        finally { lifetime.Dispose(); }
    }
}
