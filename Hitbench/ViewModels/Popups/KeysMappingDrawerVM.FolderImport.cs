using System.IO;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.DataModels;
using Hitbench.Resources.Messages;
using Hitbench.Services.FileSystem;

namespace Hitbench.ViewModels.Popups;

public sealed partial class KeysMappingDrawerVM
{
    public bool CanOpenFolder => CanOpenKey && conflicts is not null;
    [RelayCommand(CanExecute = nameof(CanOpenFolder))]
    public Task OpenFolderAsync(CancellationToken token) => CanOpenFolder ? OwnAsync(() => ImportFolderAsync(token)) : Task.CompletedTask;

    private async Task ImportFolderAsync(CancellationToken token)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        var mappings = new Dictionary<string, string>();
        var failures = new List<string>();
        var folder = ""; var created = false; var ignored = 0;
        IsBusy = true; ErrorMessage = null; ImportStatus = null;
        try
        {
            await StopPreviewAsync(); IReadOnlyList<string> selected;
            pickerActive = true; OnPropertyChanged(nameof(CanClose));
            try { selected = await picker.SelectAsync(new SamplePickerContext(snapshot.Context.ProjectPath, snapshot.Context.InitialDirectory, SampleSelectionMode.Folder), cancel.Token); }
            finally { pickerActive = false; OnPropertyChanged(nameof(CanClose)); }
            cancel.Token.ThrowIfCancellationRequested(); if (selected.Count == 0) return;
            if (selected.Count != 1) throw new InvalidOperationException("Select one folder.");
            var discovery = await files.FindNoteFilesAsync(selected[0], cancel.Token);
            cancel.Token.ThrowIfCancellationRequested(); ignored = discovery.IgnoredPaths.Count;
            if (discovery.Candidates.Count == 0) { ImportStatus = "No matching note files found (C1–B9)."; return; }
            var proposed = new Dictionary<string, string>();
            var collisionRows = new List<MappingConflict>();
            foreach (var pair in discovery.Candidates)
            {
                snapshot.KeyFiles.TryGetValue(pair.Key, out var old);
                var existing = old is null ? null : SamplePaths.Resolve(snapshot.Context.ProjectPath, old);
                if (pair.Value.Count == 1 && existing is not null && SameFile(existing, pair.Value[0])) continue;
                if (existing is not null || pair.Value.Count > 1)
                    collisionRows.Add(new(pair.Key, existing, pair.Value, old is not null && snapshot.AvailableFiles is not null && !snapshot.AvailableFiles.Contains(old, StringComparer.OrdinalIgnoreCase)));
                else proposed[pair.Key] = pair.Value[0];
            }
            if (collisionRows.Count > 0)
            {
                pickerActive = true; OnPropertyChanged(nameof(CanClose));
                IReadOnlyDictionary<string, string>? choices;
                try { choices = await conflicts!.SelectAsync(collisionRows, cancel.Token); }
                finally { pickerActive = false; OnPropertyChanged(nameof(CanClose)); }
                cancel.Token.ThrowIfCancellationRequested(); if (choices is null) return;
                foreach (var row in collisionRows)
                {
                    if (!choices.TryGetValue(row.Key, out var choice)) throw new InvalidOperationException("Choose one file for each key.");
                    if (row.ExistingPath is { } existing && SameFile(choice, existing)) continue;
                    var candidate = row.IncomingPaths.FirstOrDefault(path => SameFile(path, choice)) ?? throw new InvalidOperationException("Choose a listed file for each key.");
                    proposed[row.Key] = candidate;
                }
            }
            if (proposed.Count == 0) { ImportStatus = "Existing mappings retained; no files copied."; return; }
            cancel.Token.ThrowIfCancellationRequested();
            folder = await files.EnsureFolderAsync(snapshot.Context.ProjectPath, snapshot.Name, snapshot.FolderName, snapshot.ReservedFolderNames, cancel.Token);
            created = snapshot.FolderName.Length == 0;
            foreach (var pair in proposed)
            {
                cancel.Token.ThrowIfCancellationRequested();
                try
                {
                    var path = await files.ImportAsync(snapshot.Context.ProjectPath, pair.Value, cancel.Token, folder);
                    cancel.Token.ThrowIfCancellationRequested();
                    mappings[pair.Key] = path;
                }
                catch (OperationCanceledException) when (cancel.IsCancellationRequested) { throw; }
                catch (Exception error) { failures.Add(Path.GetFileName(pair.Value) + ": " + error.Message); }
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        catch (Exception error) { ErrorMessage = error.Message; }
        finally
        {
            try
            {
                if (mappings.Count > 0 && !closed)
                {
                    await RefreshFilesAsync(mappings.Values.ToArray(), CancellationToken.None);
                    await scheduler.InvokeAsync(() =>
                    {
                        if (closed) return;
                        pendingId = Guid.NewGuid(); pendingKey = null; accepted = false;
                        messenger.Send(new SetInstrumentalKeyMappingsRequestedMessage(snapshot.Context, pendingId, folder, mappings));
                        if (!accepted) ErrorMessage ??= "Track is no longer available; previous mappings retained.";
                    }, CancellationToken.None);
                    ImportStatus = string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0:N0} mapped · {1:N0} ignored · {2:N0} failed{3}", accepted ? mappings.Count : 0, ignored, failures.Count, cancel.IsCancellationRequested ? " · Import interrupted" : "");
                }
                else if (failures.Count > 0) ImportStatus = string.Format(System.Globalization.CultureInfo.CurrentCulture, "0 mapped · {0:N0} ignored · {1:N0} failed", ignored, failures.Count);
                if (failures.Count > 0) ErrorMessage = string.Join(Environment.NewLine, failures);
                if (created && snapshot.FolderName.Length == 0) await CleanupFolderAsync(folder);
            }
            catch (Exception error) { ErrorMessage = error.Message; }
            finally { IsBusy = false; OnPropertyChanged(nameof(CanClose)); }
        }
    }
    private static bool SameFile(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}
