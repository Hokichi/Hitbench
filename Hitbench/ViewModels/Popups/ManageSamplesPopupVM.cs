using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.DataModels;
using Hitbench.Resources.Messages;
using Hitbench.Services.FileSystem;
using Hitbench.Services.Dialogs;
using Hitbench.Services.Ui;
using Hitbench.Services;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.IO;

namespace Hitbench.ViewModels.Popups;

public sealed partial class ManageSamplesPopupVM(SampleManagementContext context, ISampleFileService files,
    SampleImportBatch imports, ISampleFileDialogService picker, ISampleRemovalDialogService removal,
    IMessenger messenger, IUiScheduler scheduler, ISamplePreviewService? preview = null,
    PlaybackVolumeSettings? playbackVolumes = null, ISampleBrowserService? waveformReader = null, bool ownsWaveformReader = false) : ObservableValidator
{
    private CancellationTokenSource lifetime = new();
    private Task operation = Task.CompletedTask;
    private bool deactivated;
    private CancellationTokenSource? previewCancellation;
    private Task previewOperation = Task.CompletedTask;
    private Task previewMonitor = Task.CompletedTask;
    private int previewGeneration;
    public SampleManagementContext Context { get; } = context;
    public ObservableCollection<SampleManagementEntry> Entries { get; } = [];
    public bool IsEmpty => !ManagedEntries.Any();
    public string EditExtension => ActiveEdit?.Extension ?? string.Empty;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(EditExtension))] private SampleManagementEntry? activeEdit;
    [ObservableProperty] [CustomValidation(typeof(SampleFileNames), nameof(SampleFileNames.ValidateStem))]
    private string draftStem = string.Empty;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private bool isBusy;
    private bool CanInteract() => !deactivated && !lifetime.IsCancellationRequested && !IsBusy && !skinDefaultsBusy;
    private bool CanAct() => CanInteract() && ActiveEdit is null;
    public bool CanClose => CanAct();
    public bool CanEditDraft => CanInteract() && ActiveEdit is not null;
    public bool CanCommitRename => CanEditDraft && !HasErrors && SampleFileNames.GetStemError(DraftStem) is null;
    private bool CanEdit(SampleManagementEntry? entry) => CanInteract() && entry is not null && IsManagedRow(entry) &&
        (ActiveEdit is null || ReferenceEquals(ActiveEdit, entry) && CanCommitRename);
    private bool CanDelete(SampleManagementEntry? entry) => CanAct() && entry is not null && IsManagedRow(entry);
    private bool CanPlay(SampleManagementEntry? entry) => CanAct() && preview is not null && entry is not null && (IsManagedRow(entry) || AllowDefaults && SkinDefaultEntries.Contains(entry));
    partial void OnIsBusyChanged(bool value) => NotifyEditingState();
    partial void OnActiveEditChanged(SampleManagementEntry? value)
    {
        if (value is not null) ValidateDraft();
        NotifyEditingState();
    }
    partial void OnDraftStemChanged(string value)
    {
        if (ActiveEdit is not null) ValidateDraft();
        NotifyEditingState();
    }
    private void ValidateDraft()
    {
        ValidateProperty(DraftStem, nameof(DraftStem));
        ErrorMessage = SampleFileNames.GetStemError(DraftStem);
    }
    private void NotifyEditingState()
    {
        foreach (var row in ManagedEntries.Concat(SkinDefaultEntries))
        {
            row.IsEditing = ReferenceEquals(row, ActiveEdit);
            row.IsInteractionEnabled = CanInteract() && (IsManagedRow(row) || AllowDefaults) && (ActiveEdit is null || row.IsEditing);
        }
        OnPropertyChanged(nameof(CanChangeAllowDefaults));
        OnPropertyChanged(nameof(CanClose)); OnPropertyChanged(nameof(CanEditDraft)); OnPropertyChanged(nameof(CanCommitRename));
        CreateTrackCommand.NotifyCanExecuteChanged();
        PlayCommand.NotifyCanExecuteChanged(); EditCommand.NotifyCanExecuteChanged(); DeleteCommand.NotifyCanExecuteChanged(); SaveCommand.NotifyCanExecuteChanged();
        AddNewSampleCommand.NotifyCanExecuteChanged(); CancelEditCommand.NotifyCanExecuteChanged(); CommitRenameCommand.NotifyCanExecuteChanged();
    }
    [RelayCommand(CanExecute = nameof(CanPlay), AllowConcurrentExecutions = true)]
    private async Task PlayAsync(SampleManagementEntry entry)
    {
        if (!CanPlay(entry)) return;
        var play = !entry.IsPlaying;
        var generation = ++previewGeneration;
        previewCancellation?.Cancel(); previewCancellation?.Dispose();
        previewCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        foreach (var row in ManagedEntries.Concat(SkinDefaultEntries)) row.IsPlaying = play && ReferenceEquals(row, entry);
        ErrorMessage = null;
        previewOperation = ChangePreviewAsync(previewOperation, entry, play, generation, previewCancellation.Token);
        await previewOperation;
    }
    private async Task ChangePreviewAsync(Task previous, SampleManagementEntry entry, bool play, int generation, CancellationToken token)
    {
        try
        {
            await previous; await previewMonitor;
            if (generation != previewGeneration || token.IsCancellationRequested) return;
            await preview!.StopAsync(CancellationToken.None);
            if (!play) return;
            await preview.PlayAsync(SamplePaths.Resolve(Context.ProjectPath, entry.FileName),
                playbackVolumes?.ApplicationVolume ?? 100, token);
            if (generation != previewGeneration || token.IsCancellationRequested) { await preview.StopAsync(CancellationToken.None); return; }
            previewMonitor = MonitorPreviewAsync(entry, generation, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            await scheduler.InvokeAsync(() => { if (generation == previewGeneration) { entry.IsPlaying = false; ErrorMessage = error.Message; } }, CancellationToken.None);
        }
    }
    private async Task MonitorPreviewAsync(SampleManagementEntry entry, int generation, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var state = await preview!.GetSnapshotAsync(token);
                await scheduler.InvokeAsync(() =>
                {
                    if (generation != previewGeneration || token.IsCancellationRequested) return;
                    entry.IsPlaying = state.IsPlaying;
                    if (state.Error is not null) ErrorMessage = state.Error;
                }, token);
                if (!state.IsPlaying) return;
                await Task.Delay(50, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            await scheduler.InvokeAsync(() => { if (generation == previewGeneration) { entry.IsPlaying = false; ErrorMessage = error.Message; } }, CancellationToken.None);
        }
    }
    private async Task StopPreviewAsync()
    {
        ++previewGeneration; previewCancellation?.Cancel();
        await previewOperation; await previewMonitor;
        if (preview is not null) await preview.StopAsync(CancellationToken.None);
        await scheduler.InvokeAsync(() => { foreach (var entry in ManagedEntries.Concat(SkinDefaultEntries)) entry.IsPlaying = false; }, CancellationToken.None);
    }
    public async Task InitializeAsync(CancellationToken token)
    {
        lifetime.Dispose(); lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        RegisterExternalImport();
        RegisterSkinDefaults();
        await skinStateOperation;
        try { var names = await files.ListAsync(Context.ProjectPath, lifetime.Token, includeSubfolders: true); lifetime.Token.ThrowIfCancellationRequested(); Replace(names); await PublishAsync(); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { ErrorMessage = error.Message; }
    }
    private void Replace(IReadOnlyList<string> names)
    {
        allSampleFiles = names.ToArray();
        var current = ManagedEntries.ToDictionary(e => e.FileName, StringComparer.OrdinalIgnoreCase);
        Entries.Clear(); MapsetEntries.Clear();
        foreach (var name in names.Where(IsManagedFile).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ThenBy(n => n, StringComparer.Ordinal))
        {
            var entry = current.GetValueOrDefault(name) ?? new(name); entry.FileName = name;
            if (Services.FileSystem.OHsMapsetSamples.IsMapsetSamplePath(name)) MapsetEntries.Add(entry); else Entries.Add(entry);
        }
        OnPropertyChanged(nameof(IsEmpty)); NotifyEditingState();
    }
    private Task PublishAsync(string? oldName = null, string? newName = null) => scheduler.InvokeAsync(() =>
    {
        allSampleFiles = allSampleFiles.Where(n => !IsManagedFile(n)).Concat(ManagedEntries.Select(e => e.FileName)).ToArray();
        messenger.Send(new ProjectSamplesChangedMessage(Context, allSampleFiles, oldName, newName));
    }, CancellationToken.None);
    private async Task RefreshAsync(SampleManagementEntry? preserveWaveformFor = null)
    {
        var requestedWaveforms = waveformLoads.Keys.ToHashSet();
        InvalidateWaveforms(preserveWaveformFor);
        try { lifetime.Token.ThrowIfCancellationRequested(); var names = await files.ListAsync(Context.ProjectPath, lifetime.Token, includeSubfolders: true); var changed = !allSampleFiles.SequenceEqual(names); Replace(names); foreach (var row in ManagedEntries.Where(requestedWaveforms.Contains)) { _ = LoadWaveformAsync(row); } // Tasks are owned by waveformOperations.
            if (changed) await PublishAsync(); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { ErrorMessage = $"Could not refresh samples: {error.Message}"; }
    }
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task EditAsync(SampleManagementEntry entry)
    {
        if (!CanEdit(entry)) return;
        if (ReferenceEquals(ActiveEdit, entry)) { await CommitDraftAsync(); return; }
        DraftStem = Path.GetFileNameWithoutExtension(entry.FileName); ActiveEdit = entry;
    }
    [RelayCommand(CanExecute = nameof(CanEditDraft))]
    private void CancelEdit()
    {
        if (!CanEditDraft) return;
        if (ActiveEdit is { } entry) { DraftStem = Path.GetFileNameWithoutExtension(entry.FileName); entry.IsEditing = false; }
        ActiveEdit = null; ClearErrors(); ErrorMessage = null;
    }
    [RelayCommand(CanExecute = nameof(CanCommitRename))]
    private Task CommitRenameAsync() => CanCommitRename ? CommitDraftAsync() : Task.CompletedTask;
    private async Task<bool> CommitDraftAsync()
    {
        if (!CanCommitRename || ActiveEdit is not { } entry) return false;
        if (Path.GetFileNameWithoutExtension(entry.FileName) == DraftStem) { CancelEdit(); return true; }
        var stem = DraftStem;
        operation = MutateAsync(async () =>
        {
            var old = entry.FileName;
            var renamed = await files.RenameAsync(Context.ProjectPath, old, stem, lifetime.Token);
            entry.FileName = renamed; entry.IsEditing = false; ActiveEdit = null;
            await PublishAsync(old, renamed); await RefreshAsync(entry);
        });
        await operation; return ActiveEdit is null;
    }
    private async Task MutateAsync(Func<Task> action)
    {
        IsBusy = true; ErrorMessage = null;
        try { await StopPreviewAsync(); await action(); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { ErrorMessage = error.Message; }
        finally { IsBusy = false; }
    }
    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task SaveAsync(SampleManagementEntry entry)
    {
        if (!CanDelete(entry)) return;
        operation = MutateAsync(async () => { await files.SaveAsync(Context.ProjectPath, entry.FileName, lifetime.Token); });
        await operation;
    }
    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync(SampleManagementEntry entry)
    {
        if (!CanDelete(entry)) return;
        operation = MutateAsync(async () =>
        {
            if (!await removal.ConfirmAsync(entry.FileName, lifetime.Token)) return;
            lifetime.Token.ThrowIfCancellationRequested();
            await files.DeleteAsync(Context.ProjectPath, entry.FileName, lifetime.Token);
            Entries.Remove(entry); MapsetEntries.Remove(entry); OnPropertyChanged(nameof(IsEmpty));
            await PublishAsync(entry.FileName); await RefreshAsync();
        });
        await operation;
    }
    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task AddNewSampleAsync()
    {
        if (!CanAct()) return;
        operation = MutateAsync(async () =>
        {
            var selected = await picker.SelectAsync(new SamplePickerContext(Context.ProjectPath, Context.InitialDirectory), lifetime.Token);
            var result = await imports.ImportAsync(Context.ProjectPath, selected, lifetime.Token);
            foreach (var name in result.ImportedFiles)
                if (!Entries.Any(e => string.Equals(e.FileName, name, StringComparison.OrdinalIgnoreCase))) Entries.Add(new(name));
            OnPropertyChanged(nameof(IsEmpty));
            if (result.ImportedFiles.Count > 0) await PublishAsync();
            await RefreshAsync();
            if (result.Failures.Count > 0) ErrorMessage = string.Join(Environment.NewLine, result.Failures);
        });
        await operation;
    }
    public OHsSampleTrackDragData? GetTrackDragData(SampleManagementEntry entry) => CanAct() && IsManagedRow(entry) ? new(Context,entry.FileName) : null;
    private bool CanCreateTrack(SampleManagementEntry? entry)=>entry is not null && GetTrackDragData(entry) is not null;
    [RelayCommand(CanExecute=nameof(CanCreateTrack))]
    private void CreateTrack(SampleManagementEntry entry) {if(GetTrackDragData(entry) is {} data)messenger.Send(new AddSampleTrackRequestedMessage(data));}
    public Task<bool> PrepareToCloseAsync() => Task.FromResult(CanClose);
    public async Task DeactivateAsync()
    {
        if (deactivated) return;
        deactivated = true; messenger.UnregisterAll(this); lifetime.Cancel(); NotifyEditingState(); await operation; await skinStateOperation; await StopWaveformsAsync();
        if (ownsWaveformReader && waveformReader is IDisposable ownedReader) ownedReader.Dispose();
        ActiveEdit = null; ClearErrors();
        try { await StopPreviewAsync(); if (preview is not null) await preview.DisposeAsync(); }
        finally { previewCancellation?.Dispose(); lifetime.Dispose(); await scheduler.InvokeAsync(NotifyEditingState, CancellationToken.None); }
    }
}


