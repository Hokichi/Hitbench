using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.DataModels;
using Hitbench.Resources.Messages;
using System.Collections.ObjectModel;

namespace Hitbench.ViewModels.Popups;

public sealed partial class ManageSamplesPopupVM
{
    public ObservableCollection<SampleManagementEntry> SkinDefaultEntries { get; } = [];
    private bool allowDefaults;
    private bool skinDefaultsBusy;
    private Task skinStateOperation = Task.CompletedTask;
    private bool skinDefaultsRegistered;
    public async Task PrepareSkinDefaultsRefreshAsync(CancellationToken token)
    {
        InvalidateWaveforms(appliesTo: SkinDefaultEntries.Contains);
        await StopPreviewAsync();
        await Task.WhenAll(waveformOperations.Where(operation => operation.IsSkinDefault).Select(operation => operation.Task)).WaitAsync(token);
    }
    public bool CanChangeAllowDefaults => CanAct() && !skinDefaultsBusy;
    public bool AllowDefaults
    {
        get => allowDefaults;
        set
        {
            if (value == allowDefaults || !CanChangeAllowDefaults) return;
            SetProperty(ref allowDefaults, value);
            messenger.Send(new OHsSetAllowDefaultsRequestedMessage(Context, value));
        }
    }
    private void RegisterSkinDefaults()
    {
        if (skinDefaultsRegistered) return;
        skinDefaultsRegistered = true;
        messenger.Register<OHsSkinDefaultsStateChangedMessage>(this, (_, message) =>
        {
            if (!deactivated && SameContext(message.Context)) skinStateOperation = ApplySkinStateAsync(skinStateOperation, message);
        });
        messenger.Send(new OHsSkinDefaultsStateRequestedMessage(Context));
    }
    private async Task ApplySkinStateAsync(Task previous, OHsSkinDefaultsStateChangedMessage message)
    {
        try
        {
            await previous;
            if (deactivated) return;
            await StopPreviewAsync();
            await scheduler.InvokeAsync(() =>
            {
                if (deactivated) return;
                InvalidateWaveforms(appliesTo: SkinDefaultEntries.Contains); SkinDefaultEntries.Clear();
                foreach (var file in message.Files) SkinDefaultEntries.Add(new(file));
                SetProperty(ref allowDefaults, message.AllowDefaults, nameof(AllowDefaults));
                skinDefaultsBusy = message.IsBusy; NotifyEditingState();
            }, lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { ErrorMessage = error.Message; }
    }
    private bool IsKnownRow(SampleManagementEntry entry) => IsManagedRow(entry) || SkinDefaultEntries.Contains(entry);
}
