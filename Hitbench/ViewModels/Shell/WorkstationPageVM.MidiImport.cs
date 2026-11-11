using CommunityToolkit.Mvvm.Input;
using Hitbench.Core.Entities;
using Hitbench.Resources.DataModels;
using Hitbench.Services;
using System.IO;

namespace Hitbench.ViewModels.Shell;

public partial class WorkstationPageVM
{
    private CancellationTokenSource? midiImportCancellation;
    private Task midiImportOperation = Task.CompletedTask;
    private bool IsMidiImportBusy => midiImportCancellation is not null;
    public bool CanImportMidi => midiReader is not null && midiDialogs is not null && projectPath is not null &&
        CanEditTracks && Waveform?.Duration > TimeSpan.Zero && !folderRenamesActive && !instrumentBusy && !managingSamples &&
        !IsNodeEditBusy && !IsNodeDragActive && !importBusy && !removalBusy && !settlingDrawers && !trackAppearancePending;

    [RelayCommand(CanExecute = nameof(CanImportMidi))]
    private Task ImportMidiAsync()
    {
        if (!CanImportMidi) return Task.CompletedTask;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(loadCancellation?.Token ?? default);
        midiImportCancellation = cancellation;
        importBusy = true;
        NotifyMidiImportState();
        midiImportOperation = RunMidiImportAsync(cancellation, Project!, projectPath!, loadGeneration, sampleSession,
            Project!.UninheritedPoints.Select(p => new UninheritedPoint { Offset = p.Offset, BPM = p.BPM }).ToArray(),
            Subdivision, Waveform!.Duration.TotalMilliseconds);
        return midiImportOperation;
    }

    private void NotifyMidiImportState()
    {
        OnPropertyChanged(nameof(IsInteractionReady));
        NotifyEditingState(); NotifyCommandState(); PublishInstrumentTimeline();
    }

    private async Task CancelMidiImportAsync()
    {
        midiImportCancellation?.Cancel();
        await midiImportOperation;
    }

    private async Task RunMidiImportAsync(CancellationTokenSource cancellation, ProjectData project, string path,
        int generation, Guid session, UninheritedPoint[] points, int subdivision, double duration)
    {
        var token = cancellation.Token;
        bool IsCurrent() => !token.IsCancellationRequested && !deactivated && !IsReturning &&
            ReferenceEquals(midiImportCancellation, cancellation) && IsCurrentProject(path, project, generation) && session == sampleSession &&
            Waveform?.Duration.TotalMilliseconds == duration && Subdivision == subdivision &&
            project.UninheritedPoints.Select(p => (p.Offset, p.BPM)).SequenceEqual(points.Select(p => (p.Offset, p.BPM)));
        try
        {
            if (points.Length == 0 || points.Any(p => p.BPM <= 0)) throw new InvalidDataException("MIDI import requires valid project timing points.");
            var file = await midiDialogs!.PickFileAsync(token);
            if (file is null || !IsCurrent()) return;
            pendingPlayCancellation?.Cancel();
            await playback.PauseAsync(token); RefreshPlayback();
            await SettleWorkstationDrawersAsync();
            if (!IsCurrent()) return;
            var source = await midiReader!.ReadAsync(file, token);
            if (!IsCurrent()) return;
            if ((source.ExcludedPitchCount > 0 || source.ExcludedPercussionParts.Count > 0) &&
                !await midiDialogs.ConfirmExclusionsAsync(source, token)) return;
            if (!IsCurrent()) return;
            var mapper = new OHsMidiImportMapper();
            OHsMidiImportMode? mode = !source.HasExplicitTempo ? OHsMidiImportMode.ProjectBpm :
                mapper.MatchesTiming(source, points) ? OHsMidiImportMode.AsIs : await midiDialogs.ChooseTimingAsync(source, points, token);
            if (mode is null || !IsCurrent()) return;
            var existingNames = project.Tracks.Select(t => t.Name).ToArray();
            var proposals = await Task.Run(() => mapper.Map(source, points, subdivision, duration, mode.Value, existingNames), token);
            if (!IsCurrent()) return;
            var batch = proposals.Select(p => new InstrumentalTrack { Name = p.Name,
                Notes = p.Notes.Select(n => new InstrumentalNote { Key = n.Key, Timestamp = n.Timestamp, IsReadOnly = n.IsReadOnly }).ToList() }).ToArray();
            await uiScheduler.InvokeAsync(() =>
            {
                if (!IsCurrent()) return;
                var oldSelection = SelectedTrack;
                try
                {
                    project.Tracks.AddRange(batch);
                    foreach (var track in batch) tracks.Add(track);
                    SelectedTrack = batch[0];
                }
                catch
                {
                    foreach (var track in batch) { project.Tracks.Remove(track); tracks.Remove(track); }
                    SelectedTrack = oldSelection;
                    throw;
                }
                NotifyTrackPresentation(); NotifyTrackAuditionState(); NotifyHistoryState();
                QueuePlaybackUpdate(); QueueSave();
            }, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (IsCurrent())
            {
                try { await midiDialogs!.ShowErrorAsync(error is NotSupportedException ? "Unsupported MIDI format" : "Cannot import MIDI", error.Message, token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception presentationError) { ErrorMessage = error.Message; System.Diagnostics.Trace.TraceError("MIDI error presentation failed: {0}", presentationError); }
            }
        }
        finally
        {
            if (ReferenceEquals(midiImportCancellation, cancellation))
            {
                midiImportCancellation = null; importBusy = false; NotifyMidiImportState();
            }
            cancellation.Dispose();
        }
    }
}
