using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hitbench.Core.Entities;
using Hitbench.Services;
using Hitbench.Services.FileSystem;
using Hitbench.Services.Ui;
using Hitbench.Services.Dialogs;
using Hitbench.Services.ProjectFiles;
using Hitbench.Resources.Messages;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using System.IO;

using Hitbench.Core.Constants;
using Hitbench.Resources.DataModels;

namespace Hitbench.ViewModels.Shell;

public partial class WorkstationPageVM
{
    private long instrumentTimelineRevision;
    private long instrumentTimelineRequestSequence;
    private Guid instrumentTimelineEditor;
    private Task instrumentTimelineNotifications = Task.CompletedTask;
    private OHsTimingSnapshot instrumentTiming = new([], []);

    private OHsInstrumentalTimelineSnapshot InstrumentTimeline(InstrumentalTrackContext context) =>
        new(context, ++instrumentTimelineRevision, Waveform?.Duration ?? TimeSpan.Zero, Position,
            IsPlaying, ActiveInstrument(context) is not null && markerClock.IsValid && Waveform?.Duration > TimeSpan.Zero,
            CanUseTransport(), instrumentTiming, ZoomFactor,
            SelectedSubdivisionIndex, ScrollWithMarker, PlaybackWarning, IsAdditionMode);

    private void PublishInstrumentTimeline()
    {
        if (!deactivated && activeInstrumentContext is { } context && ActiveInstrument(context) is not null)
            messenger?.Send(new OHsInstrumentalTimelineChangedMessage(InstrumentTimeline(context)));
    }

    private void ReceiveInstrumentTimelineEdit(OHsInstrumentalTimelineEditRequestedMessage message)
    {
        instrumentTimelineNotifications = ApplyInstrumentTimelineEditAsync(instrumentTimelineNotifications, message);
    }

    private async Task ApplyInstrumentTimelineEditAsync(Task previous, OHsInstrumentalTimelineEditRequestedMessage message)
    {
        try
        {
            await previous;
            await uiScheduler.InvokeAsync(() =>
            {
                if (deactivated || ActiveInstrument(message.Context) is null || message is { ZoomFactor: null, SelectedSubdivisionIndex: null, ScrollWithMarker: null, IsAdditionMode: null }) return;
                if (instrumentTimelineEditor != message.Context.EditorSessionId)
                { instrumentTimelineEditor = message.Context.EditorSessionId; instrumentTimelineRequestSequence = 0; }
                if (message.Sequence <= instrumentTimelineRequestSequence ||
                    message.ZoomFactor is { } zoom && !double.IsFinite(zoom) ||
                    message.SelectedSubdivisionIndex is < 0 or > 8) return;
                instrumentTimelineRequestSequence = message.Sequence;
                if (message.ZoomFactor is { } value) SetZoom((int)Math.Clamp(value, 8, 128));
                if (message.SelectedSubdivisionIndex is { } index) SelectedSubdivisionIndex = index;
                if (message.ScrollWithMarker is { } follow) ScrollWithMarker = follow;
                if (message.IsAdditionMode is { } mode) IsAdditionMode = mode;
                PublishInstrumentTimeline();
            }, CancellationToken.None);
        }
        catch (Exception error) { System.Diagnostics.Trace.TraceError("Instrument timeline update failed: {0}", error); }
    }

    partial void OnScrollWithMarkerChanged(bool value) => PublishInstrumentTimeline();
    public void RefreshPlayback()
    {
        if (deactivated) return;
        var current = playback.Latest;
        if (presentedPlayback == current) return;
        presentedPlayback = current;
        var snapshot = current.Playback;
        PlaybackWarning = snapshot.Warning;
        if (markerClock.IsValid)
            SetProperty(ref playbackInterval, markerClock.GetInterval(snapshot.Position), nameof(PlaybackInterval));
        Position = snapshot.Position;
        IsPlaying = snapshot.IsPlaying;
        ElapsedText = $"{(int)Position.TotalMinutes:00}:{Position.Seconds:00}.{Position.Milliseconds:000}";
        if (!string.IsNullOrWhiteSpace(snapshot.Error)) ErrorMessage = snapshot.Error;
        PublishInstrumentTimeline();
    }

    private void OnPlaybackStateChanged()
    {
        lock (notificationLock) notifications = Task.WhenAll(notifications, ApplyPlaybackStateAsync());
    }

    private async Task ApplyPlaybackStateAsync()
    {
        try { await uiScheduler.InvokeAsync(() => { if (!deactivated) RefreshPlayback(); }, CancellationToken.None); }
        catch (Exception error) { System.Diagnostics.Trace.TraceError("Playback presentation failed: {0}", error); }
    }

    [RelayCommand(CanExecute = nameof(CanUseTransport))]
    private async Task PlayPauseAsync()
    {
        try
        {
            if (!CanUseTransport()) return;
            if (audioService.GetSnapshot().IsPlaying)
            {
                try { await playback.PauseAsync(CancellationToken.None); }
                catch (Exception error) { if (!deactivated) ErrorMessage = error.Message; return; }
                // Intentional pause preserves consumed position, unlike a device-stopped session.
                IsPlaying = false;
            }
            else
            {
                if (!markerClock.IsValid)
                {
                    ErrorMessage = "Playback requires valid BPM timing data. Reload a beatmap with timing points.";
                    return;
                }
                pendingPlayCancellation?.Cancel();
                using var cancellation = new CancellationTokenSource();
                pendingPlayCancellation = cancellation;
                var generation = loadGeneration;
                try
                {
                    Task pending;
                    do
                    {
                        pending = playbackUpdates;
                        await pending.WaitAsync(cancellation.Token);
                    } while (!ReferenceEquals(pending, playbackUpdates));
                    if (cancellation.IsCancellationRequested || deactivated || generation != loadGeneration ||
                        !CanPlay || playbackPreparationFailed) return;
                    if (Waveform is not null && audioService.GetSnapshot().Position >= Waveform.Duration)
                    {
                        await playback.StopAsync(cancellation.Token);
                    }
                    await playback.PlayAsync(cancellation.Token);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return; }
                catch (Exception error) { ErrorMessage = error.Message; return; }
                finally
                {
                    if (ReferenceEquals(pendingPlayCancellation, cancellation)) pendingPlayCancellation = null;
                }
            }
            RefreshPlayback();
        }

        catch (Exception failure) { if (!deactivated) ErrorMessage = failure.Message; }
    }

    [RelayCommand(CanExecute = nameof(CanUseTransport))]
    private async Task StopAsync()
    {
        if (!CanUseTransport()) return;
        pendingPlayCancellation?.Cancel();
        lastGridTarget = null;
        lastGridAudioPosition = null;
        try { await playback.StopAsync(CancellationToken.None); }
        catch (Exception error) { if (!deactivated) ErrorMessage = error.Message; return; }
        RefreshPlayback();
    }

    [RelayCommand(CanExecute = nameof(CanUseTransport))]
    private async Task SeekAsync(TimeSpan position)
    {
        if (!CanUseTransport()) return;
        pendingPlayCancellation?.Cancel();
        lastGridTarget = null;
        lastGridAudioPosition = null;
        try { await playback.SeekAsync(position, CancellationToken.None); }
        catch (Exception error) { if (!deactivated) ErrorMessage = error.Message; return; }
        RefreshPlayback();
    }

    [RelayCommand(CanExecute = nameof(CanUseTransport))]
    private Task NextGridLineAsync() => SeekAdjacentGridLineAsync(true, Subdivision);

    [RelayCommand(CanExecute = nameof(CanUseTransport))]
    private Task PreviousGridLineAsync() => SeekAdjacentGridLineAsync(false, Subdivision);

    [RelayCommand(CanExecute = nameof(CanUseTransport))]
    private Task NextWholeBeatAsync() => SeekAdjacentGridLineAsync(true, 1);

    [RelayCommand(CanExecute = nameof(CanUseTransport))]
    private Task PreviousWholeBeatAsync() => SeekAdjacentGridLineAsync(false, 1);

    private async Task SeekAdjacentGridLineAsync(bool forward, int subdivision)
    {
        try
        {
            if (!CanUseTransport() || Project is null || Waveform is null) return;
            var position = audioService.GetSnapshot().Position;
            if (lastGridTarget is { } lastTarget && lastGridAudioPosition is { } lastAudioPosition &&
                position >= TimeSpan.FromTicks(Math.Min(lastAudioPosition.Ticks, lastTarget.Ticks)) &&
                position <= TimeSpan.FromTicks(Math.Max(lastAudioPosition.Ticks, lastTarget.Ticks)))
                position = lastTarget;
            var target = BeatGridCalculator.GetAdjacentPosition(Project.Offsets, Project.BPMs,
                subdivision, position, Waveform.Duration, forward);
            if (target is not null)
            {
                await SeekAsync(target.Value);
                if (audioService.GetSnapshot().Error is null)
                {
                    lastGridTarget = target.Value;
                    lastGridAudioPosition = Position;
                }
            }
        }
        catch (Exception failure) { if (!deactivated) ErrorMessage = failure.Message; }
    }

    [RelayCommand(CanExecute = nameof(CanIncreaseSubdivision))]
    private void IncreaseSubdivision()
    {
        if (!CanIncreaseSubdivision()) return;
        SelectedSubdivisionIndex++;
    }

    [RelayCommand(CanExecute = nameof(CanDecreaseSubdivision))]
    private void DecreaseSubdivision()
    {
        if (!CanDecreaseSubdivision()) return;
        SelectedSubdivisionIndex--;
    }

    public int SongVolume
    {
        get => songVolume;
        set
        {
            var next = Math.Clamp(value, 0, 100);
            if (!SetProperty(ref songVolume, next)) return;
            audioService.SetSongVolume(next);
        }
    }
    public int ApplicationVolume
    {
        get => playbackVolumes.ApplicationVolume;
        set
        {
            var previous = playbackVolumes.ApplicationVolume;
            playbackVolumes.ApplicationVolume = value;
            if (previous == playbackVolumes.ApplicationVolume) return;
            OnPropertyChanged();
            audioService.SetApplicationVolume(playbackVolumes.ApplicationVolume);
            OnPropertyChanged(nameof(HasPendingSaves));
        }
    }
    public int SamplesVolume
    {
        get => playbackVolumes.SamplesVolume;
        set
        {
            var previous = playbackVolumes.SamplesVolume;
            playbackVolumes.SamplesVolume = value;
            if (previous == playbackVolumes.SamplesVolume) return;
            OnPropertyChanged();
            audioService.SetSamplesVolume(playbackVolumes.SamplesVolume);
        }
    }
    public int SelectedSubdivisionIndex
    {
        get => selectedSubdivisionIndex;
        set
        {
            var next = Math.Clamp(value, 0, 8);
            if (!SetProperty(ref selectedSubdivisionIndex, next)) return;
            IncreaseSubdivisionCommand.NotifyCanExecuteChanged();
            DecreaseSubdivisionCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(Subdivision));
            PublishInstrumentTimeline();
        }
    }
    private bool CanUseTransport() => CanPlay;
    private bool CanIncreaseSubdivision() => IsInteractionReady && SelectedSubdivisionIndex < 8;
    private bool CanDecreaseSubdivision() => IsInteractionReady && SelectedSubdivisionIndex > 0;

    private int songVolume = 100;
    private int selectedSubdivisionIndex = 3;
    public int Subdivision => new[] { 1, 2, 3, 4, 5, 6, 8, 12, 16 }[SelectedSubdivisionIndex];
    [ObservableProperty] private bool scrollWithMarker = true;
    [ObservableProperty] private TimeSpan position;
    [ObservableProperty] private string elapsedText = "00:00.000";
    [ObservableProperty] private bool isPlaying;
    partial void OnPositionChanged(TimeSpan value) { NotifyControlPointCommands(); OnPropertyChanged(nameof(CurrentBpmText)); }
    partial void OnIsPlayingChanged(bool value) => NotifyControlPointCommands();
    private TimeSpan? lastGridTarget;
    private TimeSpan? lastGridAudioPosition;
    private TimeSpan playbackInterval;
    public TimeSpan PlaybackInterval => playbackInterval;
}
