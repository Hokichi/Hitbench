using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Resources.Messages;
using osuHitsounder.Services.FileSystem;
using osuHitsounder.Services.Dialogs;
using osuHitsounder.Services.Ui;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.IO;

namespace osuHitsounder.ViewModels.Popups;

public sealed partial class ManageSamplesPopupVM(SampleManagementContext context, ISampleFileService files,
    SampleImportBatch imports, ISampleFileDialogService picker, ISampleRemovalDialogService removal,
    IMessenger messenger, IUiScheduler scheduler) : ObservableValidator
{
    private CancellationTokenSource lifetime = new();
    private Task operation = Task.CompletedTask;
    private bool deactivated;
    public SampleManagementContext Context { get; } = context;
    public ObservableCollection<SampleManagementEntry> Entries { get; } = [];
    public bool IsEmpty => Entries.Count == 0;
    public string EditExtension => ActiveEdit?.Extension ?? string.Empty;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(EditExtension))] private SampleManagementEntry? activeEdit;
    [ObservableProperty] [NotifyDataErrorInfo] [CustomValidation(typeof(SampleFileNames), nameof(SampleFileNames.ValidateStem))]
    private string draftStem = string.Empty;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private bool isBusy;
    private bool CanAct() => !deactivated && !IsBusy;
    partial void OnIsBusyChanged(bool value)
    { EditCommand.NotifyCanExecuteChanged(); DeleteCommand.NotifyCanExecuteChanged(); AddNewSampleCommand.NotifyCanExecuteChanged(); CancelEditCommand.NotifyCanExecuteChanged(); }
    public async Task InitializeAsync(CancellationToken token)
    {
        lifetime.Dispose(); lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        try { Replace(await files.ListAsync(Context.ProjectPath, lifetime.Token)); await PublishAsync(); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { ErrorMessage = error.Message; }
    }
    private void Replace(IReadOnlyList<string> names)
    {
        var current = Entries.ToDictionary(e => e.FileName, StringComparer.OrdinalIgnoreCase);
        Entries.Clear();
        foreach (var name in names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ThenBy(n => n, StringComparer.Ordinal))
        { var entry = current.GetValueOrDefault(name) ?? new(name); entry.FileName = name; Entries.Add(entry); }
        OnPropertyChanged(nameof(IsEmpty));
    }
    private Task PublishAsync(string? oldName = null, string? newName = null) => scheduler.InvokeAsync(() =>
        messenger.Send(new ProjectSamplesChangedMessage(Context, Entries.Select(e => e.FileName).ToArray(), oldName, newName)), CancellationToken.None);
    private async Task RefreshAsync()
    {
        try { lifetime.Token.ThrowIfCancellationRequested(); var names = await files.ListAsync(Context.ProjectPath, lifetime.Token); var changed = !Entries.Select(e => e.FileName).SequenceEqual(names); Replace(names); if (changed) await PublishAsync(); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { ErrorMessage = $"Could not refresh samples: {error.Message}"; }
    }
    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task EditAsync(SampleManagementEntry entry)
    {
        if (!CanAct() || !Entries.Contains(entry) || !await CommitDraftAsync()) return;
        ActiveEdit = entry; DraftStem = Path.GetFileNameWithoutExtension(entry.FileName); entry.IsEditing = true; ErrorMessage = null;
    }
    [RelayCommand(CanExecute = nameof(CanAct))]
    private void CancelEdit()
    {
        if (ActiveEdit is { } entry) { DraftStem = Path.GetFileNameWithoutExtension(entry.FileName); entry.IsEditing = false; }
        ActiveEdit = null; ClearErrors(); ErrorMessage = null;
    }
    [RelayCommand]
    private Task CommitRenameAsync() => CommitDraftAsync();
    private async Task<bool> CommitDraftAsync()
    {
        if (IsBusy) { await operation; return ActiveEdit is null; }
        if (ActiveEdit is not { } entry) return true;
        ValidateProperty(DraftStem, nameof(DraftStem));
        if (HasErrors) { ErrorMessage = SampleFileNames.GetStemError(DraftStem); return false; }
        if (Path.GetFileNameWithoutExtension(entry.FileName) == DraftStem) { CancelEdit(); return true; }
        operation = MutateAsync(async () =>
        {
            var old = entry.FileName;
            var renamed = await files.RenameAsync(Context.ProjectPath, old, DraftStem, lifetime.Token);
            entry.FileName = renamed; entry.IsEditing = false; ActiveEdit = null;
            await PublishAsync(old, renamed); await RefreshAsync();
        });
        await operation; return ActiveEdit is null;
    }
    private async Task MutateAsync(Func<Task> action)
    {
        IsBusy = true; ErrorMessage = null;
        try { await action(); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { ErrorMessage = error.Message; }
        finally { IsBusy = false; }
    }
    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task DeleteAsync(SampleManagementEntry entry)
    {
        if (!CanAct() || !Entries.Contains(entry) || !await CommitDraftAsync()) return;
        operation = MutateAsync(async () =>
        {
            if (!await removal.ConfirmAsync(entry.FileName, lifetime.Token)) return;
            lifetime.Token.ThrowIfCancellationRequested();
            await files.DeleteAsync(Context.ProjectPath, entry.FileName, lifetime.Token);
            Entries.Remove(entry); OnPropertyChanged(nameof(IsEmpty));
            await PublishAsync(entry.FileName); await RefreshAsync();
        });
        await operation;
    }
    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task AddNewSampleAsync()
    {
        if (!CanAct() || !await CommitDraftAsync()) return;
        operation = MutateAsync(async () =>
        {
            var selected = await picker.SelectAsync(Context.InitialDirectory, lifetime.Token);
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
    public async Task<bool> PrepareToCloseAsync()
    {
        if (IsBusy) { lifetime.Cancel(); await operation; }
        return await CommitDraftAsync();
    }
    public async Task DeactivateAsync()
    {
        if (deactivated) return;
        lifetime.Cancel(); await operation; deactivated = true; lifetime.Dispose();
    }
}


