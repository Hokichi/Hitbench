using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Hitbench.Resources.DataModels;
using Hitbench.Services.FileSystem;

namespace Hitbench.ViewModels.Popups;

public sealed partial class OHsUpdatePopupVM(OHsUpdateRelease release, IOHsUpdateService updates) : ObservableObject
{
    private CancellationTokenSource? cancellation;
    private Task<OHsStagedUpdate?>? active;
    private bool closeRequested, completed;
    [ObservableProperty] private double progress;
    public string DownloadingText => string.Format(CultureInfo.CurrentCulture, "Downloading version {0}", release.Version.Tag);
    public string? Error { get; private set; }
    public OHsStagedUpdate? Result { get; private set; }
    public event Action? Completed;
    public Task<OHsStagedUpdate?> DownloadAsync(CancellationToken token) => active ??= RunAsync(token);
    private async Task<OHsStagedUpdate?> RunAsync(CancellationToken token)
    {
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (closeRequested) cancellation.Cancel();
        var report = new Progress<double>(value =>
        {
            if (!completed && !closeRequested && cancellation?.IsCancellationRequested == false && double.IsFinite(value))
                Progress = Math.Max(Progress, Math.Clamp(value, 0, 100));
        });
        try
        {
            var staged = await updates.StageAsync(release, report, cancellation.Token);
            if (closeRequested || cancellation.IsCancellationRequested) { updates.Discard(staged); return null; }
            Progress = 100;
            return Result = staged;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return null; }
        catch (Exception error) { Error = error.Message; Trace.TraceError("Update download: {0}", error); return null; }
        finally { completed = true; cancellation.Dispose(); cancellation = null; Completed?.Invoke(); }
    }
    public void RequestClose() { closeRequested = true; cancellation?.Cancel(); }
    public async Task DeactivateAsync() { RequestClose(); if (active is not null) await active; }
}
