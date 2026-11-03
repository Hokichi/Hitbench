using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hitbench.Resources.DataModels;
using Hitbench.Services.Dialogs;
using Hitbench.Services.FileSystem;
using Hitbench.Services.Ui;

namespace Hitbench.ViewModels.Shell;

public sealed partial class OHsUpdateVM(IOHsUpdateService updates, IOHsUpdateDialogService dialog, IOHsUpdateRestartService restart) : ObservableObject
{
    private readonly CancellationTokenSource lifetime = new();
    private Task? check;
    private OHsUpdateRelease? candidate;
    private bool inactive;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanDownload)), NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    private bool isBusy;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanDownload)), NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    private bool isInteractionAllowed = true;
    [ObservableProperty] private string toolTip = "Download update";
    public bool IsUpdateAvailable => candidate is not null && !inactive;
    public bool CanDownload => IsUpdateAvailable && !IsBusy && IsInteractionAllowed;

    public Task CheckAsync(CancellationToken token) => inactive ? Task.CompletedTask : check ??= CheckCoreAsync(token);
    private async Task CheckCoreAsync(CancellationToken token)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        try
        {
            var release = await updates.CheckAsync(cancellation.Token);
            if (cancellation.IsCancellationRequested || inactive) return;
            candidate = release;
            if (release is not null) ToolTip = $"Download version {release.Version.Tag}";
            Refresh();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) { Trace.TraceWarning("Update check failed: {0}", error); }
    }

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadAsync()
    {
        if (!CanDownload || candidate is null) return;
        IsBusy = true;
        OHsStagedUpdate? staged = null;
        var handedOff = false;
        try
        {
            staged = await dialog.ShowAsync(candidate, lifetime.Token);
            if (staged is null || lifetime.IsCancellationRequested) return;
            handedOff = await restart.TryRestartAsync(staged, lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { ToolTip = error.Message; Trace.TraceError("Update failed: {0}", error); }
        finally
        {
            if (staged is not null && !handedOff)
                try { updates.Discard(staged); } catch (Exception error) { Trace.TraceWarning("Update cleanup: {0}", error); }
            IsBusy = false;
        }
    }
    private void Refresh()
    {
        OnPropertyChanged(nameof(IsUpdateAvailable)); OnPropertyChanged(nameof(CanDownload)); DownloadCommand.NotifyCanExecuteChanged();
    }
    public async Task DeactivateAsync()
    {
        if (inactive) return;
        inactive = true; lifetime.Cancel(); Refresh();
        if (check is not null) await check;
        if (DownloadCommand.ExecutionTask is { } download) await download;
        lifetime.Dispose();
    }
}
