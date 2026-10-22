using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Soundie.Resources.DataModels;
using Soundie.Resources.Messages;
using Soundie.Services.ProjectFiles;
namespace Soundie.ViewModels.Shell;

public partial class WorkstationPageVM
{
    private string? exportBeatmapPath, exportAudioPath;
    private Task activeExport = Task.CompletedTask;
    private CancellationTokenSource? exportCancellation;
    private Guid exportOperation;
    private int exportGeneration;
    [ObservableProperty] private bool isExporting;
    public bool CanExport => exportDialog is not null && Project is not null && projectPath is not null &&
        exportBeatmapPath is not null && exportAudioPath is not null && IsInteractionReady &&
        !folderRenamesActive && !instrumentBusy && !managingSamples && !IsNodeEditBusy && !importBusy && !removalBusy && lastSaveError is null;
    private void NotifyExportState() { OnPropertyChanged(nameof(CanExport)); ExportCommand.NotifyCanExecuteChanged(); }
    partial void OnSaveErrorChanged(string? value) => NotifyExportState();
    partial void OnIsExportingChanged(bool value)
    { OnPropertyChanged(nameof(IsInteractionReady)); NotifyEditingState(); NotifyCommandState(); }
    [RelayCommand(CanExecute = nameof(CanExport))]
    private Task ExportAsync()
    {
        if (!CanExport) return Task.CompletedTask;
        IsExporting = true; exportCancellation = new(); exportOperation = Guid.NewGuid(); exportGeneration = loadGeneration;
        activeExport = ExportCoreAsync(exportCancellation.Token); return activeExport;
    }
    private async Task ExportCoreAsync(CancellationToken token)
    {
        var path = projectPath!; var project = Project!; var generation = loadGeneration;
        try
        {
            await FlushSavesAsync(); token.ThrowIfCancellationRequested();
            if (!IsCurrentProject(path, project, generation)) return;
            pendingPlayCancellation?.Cancel(); await playback.PauseAsync(token);
            token.ThrowIfCancellationRequested();
            var request = new OHsExportRequest(exportOperation, path, exportBeatmapPath!, exportAudioPath!,
                ProjectDataSerializer.Serialize(project), new("Hitsounds", 1, ".ogg", ".ogg", false));
            await exportDialog!.ShowAsync(request, token);
            Task pending; lock (notificationLock) pending = notifications;
            await pending;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        { if (!deactivated && !IsReturning && generation == loadGeneration && projectPath == path) ErrorMessage = error.Message; }
        finally
        {
            exportCancellation?.Dispose(); exportCancellation = null; exportOperation = Guid.Empty; IsExporting = false;
        }
    }
    private async Task SettleExportAsync() { exportCancellation?.Cancel(); await activeExport; }
    private bool IsCurrentExport(OHsExportResultMessage result) => IsExporting && !deactivated && !IsReturning &&
        exportCancellation?.IsCancellationRequested == false && result.OperationId == exportOperation &&
        exportGeneration == loadGeneration && string.Equals(result.ProjectPath, projectPath, StringComparison.OrdinalIgnoreCase);
    private void ReceiveExportResult(OHsExportResultMessage result)
    {
        if (!IsCurrentExport(result)) return;
        lock (notificationLock) notifications = Task.WhenAll(notifications, ApplyExportResultAsync(result));
    }
    private async Task ApplyExportResultAsync(OHsExportResultMessage result)
    {
        try { await uiScheduler.InvokeAsync(() => { if (IsCurrentExport(result)) ErrorMessage = result.Error; }, CancellationToken.None); }
        catch (Exception error) { System.Diagnostics.Trace.TraceError("Export result presentation failed: {0}", error); }
    }
}
