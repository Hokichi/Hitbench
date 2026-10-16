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
    private bool CanResnapAllSamples() => CanPlaceSamples && tracks.Any(track => track.Samples.Count > 0);

    public bool ResnapAllSampleNodes()
    {
        if (!CanResnapAllSamples()) return false;
        var entries = new List<NodeHistoryEntry>();
        foreach (var track in tracks.Where(CanEditTrackGrid))
        for (var index = 0; index < track.Samples.Count; index++)
        {
            var sample = track.Samples[index];
            var timestamp = BeatGridCalculator.GetNearestTimestamp(Project!.Offsets, Project.BPMs, Subdivision,
                sample.Timestamp, Waveform!.Duration.TotalMilliseconds);
            if (timestamp is null)
            {
                ErrorMessage = "No usable grid beat for resnapping samples.";
                return false;
            }
            if (timestamp.Value != sample.Timestamp)
                entries.Add(new(track, sample, index, NodeHistoryOperation.Movement, sample.Timestamp, timestamp.Value));
        }
        CancelNodeDrag();
        ErrorMessage = null;
        if (entries.Count == 0) return false;
        foreach (var entry in entries) entry.Sample.Timestamp = entry.NextTimestamp;
        history.RecordBatch(entries);
        PublishNodeChange();
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanResnapAllSamples))]
    private void ResnapAllSamples() => ResnapAllSampleNodes();

}
