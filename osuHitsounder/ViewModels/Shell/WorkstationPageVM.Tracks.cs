using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using osuHitsounder.Core.Entities;
using osuHitsounder.Services;
using osuHitsounder.Services.FileSystem;
using osuHitsounder.Services.Ui;
using osuHitsounder.Services.Dialogs;
using osuHitsounder.Services.ProjectFiles;
using osuHitsounder.Resources.Messages;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using System.IO;

using osuHitsounder.Core.Constants;
using osuHitsounder.Resources.DataModels;

namespace osuHitsounder.ViewModels.Shell;

public partial class WorkstationPageVM
{
    [RelayCommand(CanExecute = nameof(CanAddTrack))]
    private void AddSampleTrack()
    {
        if (!CanAddTrack) return;
        var track = new Track { SelectedSampleFileName = sampleTrackFiles[0] };
        Project!.Tracks.Add(track);
        tracks.Add(track);
        SelectedTrack = track;
        QueueSave();
    }

    [RelayCommand(CanExecute = nameof(CanAddInstrumentalTrack))]
    private void AddPianoTrack()
    {
        if (!CanAddInstrumentalTrack) return;
        var track=new InstrumentalTrack();
        Project!.Tracks.Add(track); tracks.Add(track); SelectedTrack=track; QueueSave();
    }

    [RelayCommand]
    private Task RemoveTrackAsync(Track track)
    {
        if (!CanRemoveTrack || !tracks.Contains(track)) return Task.CompletedTask;
        activeRemoval = RemoveTrackCoreAsync(track);
        return activeRemoval;
    }

    private async Task RemoveTrackCoreAsync(Track track)
    {
        var project = Project;
        var generation = loadGeneration;
        removalBusy = true;
        NotifyHistoryState();
        OnPropertyChanged(nameof(CanRemoveTrack));
        OnPropertyChanged(nameof(CanImportSample));
        OnPropertyChanged(nameof(CanPlaceSamples));
        try
        {
            if ((track.Samples.Count > 0 || track is InstrumentalTrack { Notes.Count: >0 }) && !await trackRemovalDialog!.ConfirmAsync(loadCancellation?.Token ?? CancellationToken.None)) return;
            if (deactivated || generation != loadGeneration || !ReferenceEquals(Project, project) ||
                project is null || !tracks.Contains(track)) return;
            var removedIndex = tracks.IndexOf(track);
            var wasSelected = ReferenceEquals(SelectedTrack, track);
            project.Tracks.Remove(track);
            tracks.Remove(track);
            if (wasSelected) SelectedTrack = tracks.Count == 0 ? null : tracks[Math.Min(removedIndex, tracks.Count - 1)];
            history.RemoveTrack(track); if (track is InstrumentalTrack instrument) instrumentHistories.Remove(instrument);
            PruneNodeSelection();
            NotifyHistoryState();
            QueuePlaybackUpdate();
            QueueSave();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { ErrorMessage = error.Message; }
        finally
        {
            removalBusy = false;
            NotifyHistoryState();
            OnPropertyChanged(nameof(CanRemoveTrack));
            OnPropertyChanged(nameof(CanImportSample));
            OnPropertyChanged(nameof(CanPlaceSamples));
        }
    }

    public void SelectSample(Track track, string fileName)
    {
        if (track is InstrumentalTrack) return;
        if (!CanEditTracks || !tracks.Contains(track) || string.IsNullOrWhiteSpace(fileName) ||
            sampleFiles is not null && !sampleTrackFiles.Any(name => string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase))) return;
        if (string.Equals(track.SelectedSampleFileName, fileName, StringComparison.OrdinalIgnoreCase)) return;
        track.SelectedSampleFileName = sampleTrackFiles.FirstOrDefault(name =>
            string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase)) ?? fileName;
        foreach (var sample in track.Samples) sample.FileName = track.SelectedSampleFileName;
        samplesRevision++;
        OnPropertyChanged(nameof(SamplesRevision));
        QueuePlaybackUpdate();
        QueueSave();
    }

    public bool PlaceSample(Track track, double leftBoundaryMilliseconds)
    {
        if (!CanPlaceSamples || !CanEditTrackGrid(track) || !double.IsFinite(leftBoundaryMilliseconds) ||
            leftBoundaryMilliseconds < 0 || leftBoundaryMilliseconds >= Waveform!.Duration.TotalMilliseconds ||
            leftBoundaryMilliseconds > int.MaxValue) return false;
        var timestamp = (int)Math.Round(leftBoundaryMilliseconds, MidpointRounding.AwayFromZero);
        var existing = track.Samples.LastOrDefault(node => node.Timestamp == timestamp);
        if (existing is not null) return SelectNode(track, existing, false);
        if (!IsAdditionMode) return false;
        var fileName = sampleTrackFiles.FirstOrDefault(name =>
            string.Equals(name, track.SelectedSampleFileName, StringComparison.OrdinalIgnoreCase));
        if (fileName is null) return false;
        var sample = new Sample
        {
            FileName = fileName,
            Volume = 100,
            Timestamp = (int)Math.Round(leftBoundaryMilliseconds, MidpointRounding.AwayFromZero)
        };
        track.Samples.Add(sample);
        history.RecordPlacement(track, sample, track.Samples.Count - 1);
        PublishNodeChange();
        SelectNode(track, sample, false);
        return true;
    }

    public bool RemoveSample(Track track, Sample sample)
    {
        if (!CanPlaceSamples || !CanEditTrackGrid(track)) return false;
        var index = track.Samples.FindIndex(node => ReferenceEquals(node, sample));
        if (index < 0) return false;
        track.Samples.RemoveAt(index);
        history.RecordRemoval(track, sample, index);
        PublishNodeChange();
        return true;
    }

    public void SetTrackName(Track track, string name)
    {
        if (!CanEditTracks || !tracks.Contains(track)) return;
        if (track is InstrumentalTrack instrument && instrument.FolderName.Length > 0 && sampleFiles is not null)
        { QueueInstrumentFolderRename(instrument, name); return; }
        if (string.Equals(track.Name, name, StringComparison.Ordinal)) return;
        track.Name = name;
        QueueSave();
        instrumentRevision++; PublishInstrumentSnapshot();
    }

    public void SetTrackSampleType(Track track, osuHitsounder.Core.Enums.SampleType value)
    {
        if (track is InstrumentalTrack) return;
        if (!CanEditTracks || !tracks.Contains(track) || !Enum.IsDefined(value) || track.SampleType == value) return;
        track.SampleType = value;
        QueueSave();
    }

    public void SetTrackAddition(Track track, string value)
    {
        if (track is InstrumentalTrack) return;
        if (!CanEditTracks || !tracks.Contains(track) || !Track.AdditionOptions.Contains(value) || track.Addition == value) return;
        track.Addition = value;
        QueueSave();
    }

    public void SetTrackVolume(Track track, int volume)
    {
        if (!CanEditTracks || !tracks.Contains(track)) return;
        var next = Math.Clamp(volume, 5, 100);
        if (track.Volume == next) return;
        track.Volume = next;
        QueuePlaybackUpdate();
        QueueSave();
        instrumentRevision++; PublishInstrumentSnapshot();
    }

    private Track? selectedTrack;
    public Track? SelectedTrack
    {
        get => selectedTrack;
        set
        {
            if (value is not null && !tracks.Contains(value)) return;
            if (SetProperty(ref selectedTrack, value)) NotifyNodeEditingState();
        }
    }
    public ObservableCollection<Track> Tracks => tracks;
    private readonly ObservableCollection<Track> tracks = [];
    private bool removalBusy;
    private Task? activeRemoval;
    private void InitializeLoadedTracks(ProjectData project)
    {
                tracks.Clear();
                foreach (var track in project.Tracks)
                {
                    if(track is not InstrumentalTrack) track.SelectedSampleFileName ??= track.Samples.FirstOrDefault()?.FileName;
                    tracks.Add(track);
                }
    }
    private void NotifyTrackCommands()
    {
        IncreaseTrackHeightCommand.NotifyCanExecuteChanged();
        DecreaseTrackHeightCommand.NotifyCanExecuteChanged();
        ToggleTracksCollapsedCommand.NotifyCanExecuteChanged();
        AddSampleTrackCommand.NotifyCanExecuteChanged();
        AddPianoTrackCommand.NotifyCanExecuteChanged();
    }
}
