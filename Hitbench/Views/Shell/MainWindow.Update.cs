using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.Input;
using Hitbench.Views.Pages;
using Hitbench.Views.Shell;
using Hitbench.ViewModels.Shell;

namespace Hitbench;

public partial class MainWindow
{
    private OHsUpdateVM? updates;
    private Task updateCheck = Task.CompletedTask;
    private Task updateShutdown = Task.CompletedTask;
    private Action? updateAcceptedClose;
    private bool updateClosePrepared;
    private bool updateInputFrozen, updateInputWasEnabled;
    internal void InitializeUpdates(OHsUpdateVM viewModel)
    {
        updates = viewModel;
        ShellHeader.SetBinding(OHsShellHeader.IsUpdateAvailableProperty, new Binding(nameof(OHsUpdateVM.IsUpdateAvailable)) { Source = updates, Mode = BindingMode.OneWay });
        ShellHeader.SetBinding(OHsShellHeader.CanDownloadUpdateProperty, new Binding(nameof(OHsUpdateVM.CanDownload)) { Source = updates, Mode = BindingMode.OneWay });
        ShellHeader.SetBinding(OHsShellHeader.DownloadUpdateToolTipProperty, new Binding(nameof(OHsUpdateVM.ToolTip)) { Source = updates, Mode = BindingMode.OneWay });
        RefreshUpdateInteraction();
    }
    internal void StartUpdateCheck() { if (updates is not null) updateCheck = updates.CheckAsync(lifetime.Token); }
    private void RefreshUpdateInteraction()
    {
        if (updates is not null) updates.IsInteractionAllowed = !closed && !closeRequested && !closingForSave &&
            !IsAudioBusy && !IsPopupOverlayVisible && !IsBlockingDrawerVisible && !viewModel.IsInteractionBlocked && returnHome.IsCompleted;
    }
    private async void OnDownloadUpdateRequested(object sender, RoutedEventArgs e)
    {
        try
        {
            RefreshUpdateInteraction();
            if (updates?.DownloadCommand is IAsyncRelayCommand command && command.CanExecute(null)) await command.ExecuteAsync(null);
        }
        catch (Exception error) { Trace.TraceError("Update action: {0}", error); }
    }

    internal async Task<bool> PrepareForUpdateCloseAsync(CancellationToken token)
    {
        if (closed || closeRequested || closingForSave || !returnHome.IsCompleted) return false;
        closeRequested = true;
        updateInputWasEnabled = ShellRoot.IsEnabled;
        updateInputFrozen = true;
        ShellRoot.SetCurrentValue(IsEnabledProperty, false);
        RefreshUpdateInteraction();
        placementTimer.Stop();
        var success = false;
        try
        {
            await SettleShellDrawersAsync();
            if (PageHost.Content is WorkstationPage workstation)
            {
                await workstation.SettleWorkstationDrawersAsync();
                await DrawerInputCompletion;
                if (!await workstation.PrepareToReturnAsync(token)) return false;
                await workstation.FlushSavesAsync();
            }
            await placementSave;
            token.ThrowIfCancellationRequested();
            // Normal placement saves are best-effort; an update must verify the current snapshot.
            await settings.SaveWindowPlacementAsync(lastIsMaximized, restoredWidth, restoredHeight, token);
            token.ThrowIfCancellationRequested();
            placementDirty = false;
            updateClosePrepared = success = true;
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return false; }
        catch (Exception error) { Trace.TraceError("Update save preparation: {0}", error); return false; }
        finally { if (!success) CancelUpdateClose(); }
    }

    internal Task<bool> RequestUpdateCloseAsync(Action onAcceptedClose, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!updateClosePrepared || closed) return Task.FromResult(false);
        updateAcceptedClose = onAcceptedClose;
        Close();
        // OnClosed is the sole commit point. A cancelled close must never authorize a helper.
        var accepted = closed;
        if (!accepted) updateAcceptedClose = null;
        return Task.FromResult(accepted);
    }
    internal void CancelUpdateClose()
    {
        if (closed) return;
        updateClosePrepared = false; updateAcceptedClose = null; closeRequested = false;
        if (updateInputFrozen)
        {
            updateInputFrozen = false;
            ShellRoot.SetCurrentValue(IsEnabledProperty, updateInputWasEnabled);
        }
        if (PageHost.Content is WorkstationPage workstation) workstation.CancelReturn();
        RefreshUpdateInteraction();
        if (placementDirty) placementTimer.Start();
    }
    private void AcceptUpdateClose()
    {
        var accept = updateAcceptedClose;
        updateAcceptedClose = null;
        if (accept is not null) accept();
    }
    private void StopUpdates()
    {
        if (updates is not null) updateShutdown = updates.DeactivateAsync();
    }
}
