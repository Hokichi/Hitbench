using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.DataModels;
using Hitbench.Resources.Messages;
namespace Hitbench.ViewModels.Popups;
public sealed partial class KeysMappingDrawerVM
{
    public KeyMappingEntry? SelectedEntry { get; private set; }
    public bool HasSelection => SelectedEntry is not null;
    public bool CanEditSelectedMapping => CanEditMapping && HasSelection;
    public bool CanDeleteSelected => CanEditSelectedMapping && SelectedEntry!.FileName is not null;
    public bool CanClearAllMappings => CanEditMapping && confirmClearMappings is not null && Rows.Any(r => r.FileName is not null);
    public string SelectionStatus => HasSelection ? "" : "Select a piano key to edit its sample mapping.";
    private bool selecting;
    public Task ActivateKeyAsync(string key, CancellationToken token = default) => OwnAsync(async () =>
    {
        if (token.IsCancellationRequested || !await SelectKeyAsync(key, token)) return;
        if (SelectedEntry is not { } entry || entry.Key != key || !await CommitMappingAsync(entry.Key, entry.DraftFileName, token)) return;
        await PlaySelectedAsync(token);
    });
    public Task<bool> SelectKeyAsync(string key, CancellationToken token = default) => OwnAsync(async () =>
    {
        if (!CanEditMapping || selecting) return false;
        var next = Rows.FirstOrDefault(r => r.Key == key);
        if (next is null) return false;
        if (ReferenceEquals(next, SelectedEntry)) return true;
        selecting = true; NotifySelection();
        try
        {
            if (SelectedEntry is { } previous && !await CommitMappingAsync(previous.Key, previous.DraftFileName, token)) return false;
            if (closed || closing || token.IsCancellationRequested) return false;
            await StopPreviewAsync();
            if (closed || closing || token.IsCancellationRequested) return false;
            SelectedEntry = next; NotifySelection(); return true;
        }
        finally { selecting = false; NotifySelection(); }
    });
    private void NotifySelection()
    {
        OnPropertyChanged(nameof(SelectedEntry)); OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanEditSelectedMapping)); OnPropertyChanged(nameof(SelectionStatus));
        OnPropertyChanged(nameof(CanEditMapping));
        OpenKeyCommand.NotifyCanExecuteChanged(); OpenFolderCommand.NotifyCanExecuteChanged();
        PlaySelectedCommand.NotifyCanExecuteChanged(); DeleteSelectedCommand.NotifyCanExecuteChanged(); ClearAllMappingsCommand.NotifyCanExecuteChanged();
    }
    partial void OnIsBusyChanged(bool value) => NotifySelection();
    private readonly Func<string, CancellationToken, Task<bool>>? confirmClearMappings;
    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    public Task DeleteSelectedAsync(CancellationToken token) => OwnAsync(async () =>
    {
        if (token.IsCancellationRequested || !CanDeleteSelected || SelectedEntry is not { } entry) return;
        await StopPreviewAsync(); if (await CommitMappingAsync(entry.Key, "", token)) { entry.DraftFileName = entry.FileName ?? ""; entry.Error = null; NotifySelection(); }
    });
    [RelayCommand(CanExecute = nameof(CanClearAllMappings))]
    public Task ClearAllMappingsAsync(CancellationToken token) => OwnAsync(async () =>
    {
        if (!CanClearAllMappings) return;
        var context = snapshot.Context; var revision = snapshot.Revision;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        IsBusy = true; pickerActive = true; OnPropertyChanged(nameof(CanClose));
        try
        {
            if (!await confirmClearMappings!(snapshot.Name, cancel.Token) || cancel.IsCancellationRequested || closed || closing) return;
            await StopPreviewAsync();
            if (cancel.IsCancellationRequested || closed || closing) return;
            pendingId = Guid.NewGuid(); pendingKey = null; accepted = false;
            messenger.Send(new OHsClearInstrumentalKeyMappingsRequestedMessage(context, pendingId, revision));
            if (accepted) foreach (var row in Rows) { row.DraftFileName = row.FileName ?? ""; row.Error = null; }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        catch (Exception error) { ErrorMessage = error.Message; }
        finally { pickerActive = false; IsBusy = false; OnPropertyChanged(nameof(CanClose)); NotifySelection(); }
    });
}
