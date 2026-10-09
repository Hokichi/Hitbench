using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using osuHitsounder.Core.Entities;
using osuHitsounder.Services;

namespace osuHitsounder.ViewModels.Shell;

public partial class WorkstationPageVM
{
    private IReadOnlyCollection<Sample> selectedNodes = Array.AsReadOnly(Array.Empty<Sample>());
    private CopiedSampleNode[] copiedNodes = [];
    private NodeHistoryEntry[] dragNodes = [];
    private Sample? dragAnchor;
    private long dragDelta;
    private IReadOnlyDictionary<Sample, int> nodePreview = new ReadOnlyDictionary<Sample, int>(new Dictionary<Sample, int>());

    public IReadOnlyCollection<Sample> SelectedSamples => selectedNodes;
    public IReadOnlyDictionary<Sample, int> NodePreviewTimestamps => nodePreview;
    public bool IsNodeDragActive => dragAnchor is not null;
    public bool CanCopyNodes => CanPlaceSamples && selectedNodes.Count > 0;
    public bool CanPasteNodes => CanPlaceSamples && copiedNodes.Length > 0;
    public bool CanDeleteNodes => CanCopyNodes;

    public bool SelectNode(Track track, Sample sample, bool toggle)
    {
        if (!CanPlaceSamples || !tracks.Contains(track) || !track.Samples.Contains(sample)) return false;
        SetNodeSelection(toggle ? selectedNodes.Contains(sample) ? selectedNodes.Where(node => !ReferenceEquals(node, sample))
            : selectedNodes.Append(sample) : [sample]);
        return true;
    }

    public void SetNodeSelection(IEnumerable<Sample> samples)
    {
        if (!CanPlaceSamples) return;
        CancelNodeDrag();
        SetNodeSelectionCore(samples);
    }

    private void SetNodeSelectionCore(IEnumerable<Sample> samples)
    {
        var live = tracks.SelectMany(track => track.Samples).ToHashSet();
        var next = samples.Where(live.Contains).Distinct().ToArray();
        if (selectedNodes.SequenceEqual(next)) return;
        selectedNodes = Array.AsReadOnly(next);
        OnPropertyChanged(nameof(SelectedSamples));
        NotifyNodeEditingState();
    }

    public void ClearNodeSelection() => SetNodeSelectionCore([]);

    internal void RestoreNodeSelection(IEnumerable<Sample> samples, int generation)
    {
        if (!deactivated && generation == loadGeneration) SetNodeSelectionCore(samples);
    }

    private NodeHistoryEntry[] SelectedNodeEntries(NodeHistoryOperation operation) => tracks
        .SelectMany(track => track.Samples.Select((sample, index) => new NodeHistoryEntry(track, sample, index, operation,
            sample.Timestamp, sample.Timestamp))).Where(entry => selectedNodes.Contains(entry.Sample)).ToArray();

    public bool CopySelectedNodes()
    {
        if (!CanCopyNodes) return false;
        var entries = SelectedNodeEntries(NodeHistoryOperation.Placement);
        if (entries.Length == 0) return false;
        var first = entries.Min(entry => entry.Sample.Timestamp);
        copiedNodes = entries.Select(entry => new CopiedSampleNode(entry.Track, entry.Sample.FileName, entry.Sample.Volume,
            (long)entry.Sample.Timestamp - first)).ToArray();
        NotifyNodeEditingState();
        return true;
    }

    public bool PasteCopiedNodes()
    {
        if (!CanPasteNodes) return false;
        var anchor = BeatGridCalculator.GetNearestTimestamp(Project!.Offsets, Project.BPMs, Subdivision,
            Position.TotalMilliseconds, Waveform!.Duration.TotalMilliseconds);
        if (anchor is null) return RejectPaste("No usable grid beat near the playback marker.");
        foreach (var node in copiedNodes)
        {
            if (!tracks.Contains(node.Track)) return RejectPaste("An original sample track was removed.");
            if (!availableSamples.Any(file => string.Equals(file, node.FileName, StringComparison.OrdinalIgnoreCase)))
                return RejectPaste("A copied sample file is unavailable.");
            var timestamp = anchor.Value + node.RelativeTimestamp;
            if (!ValidNodeTimestamp(timestamp)) return RejectPaste("The copied formation does not fit within the song.");
        }
        CancelNodeDrag();
        var batch = new List<NodeHistoryEntry>();
        foreach (var node in copiedNodes)
        {
            var sample = new Sample { FileName = node.FileName, Volume = node.Volume, Timestamp = (int)(anchor.Value + node.RelativeTimestamp) };
            var index = node.Track.Samples.Count;
            node.Track.Samples.Add(sample);
            batch.Add(new(node.Track, sample, index, NodeHistoryOperation.Placement));
        }
        history.RecordBatch(batch);
        SetNodeSelectionCore(batch.Select(entry => entry.Sample));
        PublishNodeChange();
        ErrorMessage = null;
        return true;
    }

    private bool RejectPaste(string reason) { ErrorMessage = reason; return false; }
    private bool ValidNodeTimestamp(long timestamp) => timestamp >= 0 && timestamp <= int.MaxValue &&
        Waveform is not null && timestamp < Waveform.Duration.TotalMilliseconds;

    public bool RemoveSelectedNodes()
    {
        if (!CanDeleteNodes) return false;
        var entries = SelectedNodeEntries(NodeHistoryOperation.Removal).GroupBy(entry => entry.Track)
            .SelectMany(group => group.OrderByDescending(entry => entry.Index)).ToArray();
        if (entries.Length == 0) return false;
        CancelNodeDrag();
        foreach (var entry in entries) entry.Track.Samples.RemoveAt(entry.Index);
        history.RecordBatch(entries);
        PublishNodeChange();
        return true;
    }

    public bool BeginNodeDrag(Track track, Sample anchor)
    {
        CancelNodeDrag();
        if (!CanPlaceSamples || !tracks.Contains(track) || !track.Samples.Contains(anchor)) return false;
        if (!selectedNodes.Contains(anchor)) SelectNode(track, anchor, false);
        var entries = SelectedNodeEntries(NodeHistoryOperation.Movement);
        if (entries.Length == 0 || entries.Any(entry => !ValidNodeTimestamp(entry.PreviousTimestamp))) return false;
        dragNodes = entries;
        dragAnchor = anchor;
        dragDelta = 0;
        OnPropertyChanged(nameof(IsNodeDragActive));
        return true;
    }

    public void PreviewNodeDrag(double deltaMilliseconds)
    {
        if (!IsNodeDragActive) return;
        if (!CanPlaceSamples || !double.IsFinite(deltaMilliseconds)) { CancelNodeDrag(); return; }
        var anchor = dragNodes.Single(entry => ReferenceEquals(entry.Sample, dragAnchor)).PreviousTimestamp;
        var minimum = anchor - dragNodes.Min(entry => entry.PreviousTimestamp);
        var last = (int)Math.Min(int.MaxValue, Math.Ceiling(Waveform!.Duration.TotalMilliseconds) - 1);
        var maximum = last - (dragNodes.Max(entry => entry.PreviousTimestamp) - anchor);
        var snapped = BeatGridCalculator.GetNearestTimestamp(Project!.Offsets, Project.BPMs, Subdivision,
            anchor + deltaMilliseconds, Waveform.Duration.TotalMilliseconds, minimum, maximum);
        dragDelta = snapped is null ? 0 : (long)snapped.Value - anchor;
        nodePreview = new ReadOnlyDictionary<Sample, int>(dragNodes.ToDictionary(entry => entry.Sample,
            entry => (int)(entry.PreviousTimestamp + dragDelta)));
        OnPropertyChanged(nameof(NodePreviewTimestamps));
    }

    public bool CommitNodeDrag()
    {
        if (!IsNodeDragActive) return false;
        var entries = dragNodes.Select(entry => entry with { NextTimestamp = (int)(entry.PreviousTimestamp + dragDelta) }).ToArray();
        var valid = CanPlaceSamples && dragDelta != 0 && entries.All(entry => tracks.Contains(entry.Track) &&
            entry.Track.Samples.Contains(entry.Sample) && entry.Sample.Timestamp == entry.PreviousTimestamp && ValidNodeTimestamp(entry.NextTimestamp));
        CancelNodeDrag();
        if (!valid) return false;
        foreach (var entry in entries) entry.Sample.Timestamp = entry.NextTimestamp;
        history.RecordBatch(entries);
        PublishNodeChange();
        return true;
    }

    public void CancelNodeDrag()
    {
        var active = IsNodeDragActive;
        dragAnchor = null; dragNodes = []; dragDelta = 0;
        if (nodePreview.Count > 0)
        {
            nodePreview = new ReadOnlyDictionary<Sample, int>(new Dictionary<Sample, int>());
            OnPropertyChanged(nameof(NodePreviewTimestamps));
        }
        if (active) OnPropertyChanged(nameof(IsNodeDragActive));
    }

    public bool MoveSelectedNodes(bool forward)
    {
        if (!CanCopyNodes) return false;
        var earliest = SelectedNodeEntries(NodeHistoryOperation.Movement).MinBy(entry => entry.PreviousTimestamp);
        if (earliest is null) return false;
        var time = TimeSpan.FromMilliseconds(earliest.PreviousTimestamp);
        var next = BeatGridCalculator.GetAdjacentPosition(Project!.Offsets, Project.BPMs, Subdivision, time, Waveform!.Duration, forward);
        if (next is not null && Math.Round(next.Value.TotalMilliseconds, MidpointRounding.AwayFromZero) == earliest.PreviousTimestamp)
            next = BeatGridCalculator.GetAdjacentPosition(Project.Offsets, Project.BPMs, Subdivision,
                TimeSpan.FromMilliseconds(earliest.PreviousTimestamp + (forward ? 0.4999 : -0.5)), Waveform.Duration, forward);
        if (next is null || !BeginNodeDrag(earliest.Track, earliest.Sample)) return false;
        PreviewNodeDrag(next.Value.TotalMilliseconds - time.TotalMilliseconds);
        return CommitNodeDrag();
    }

    private bool CanResnapAllSamples() => CanPlaceSamples && tracks.Any(track => track.Samples.Count > 0);

    public bool ResnapAllSampleNodes()
    {
        if (!CanResnapAllSamples()) return false;
        var entries = new List<NodeHistoryEntry>();
        foreach (var track in tracks)
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

    [RelayCommand(CanExecute = nameof(CanCopyNodes))] private void CopyNodes() => CopySelectedNodes();
    [RelayCommand(CanExecute = nameof(CanPasteNodes))] private void PasteNodes() => PasteCopiedNodes();
    [RelayCommand(CanExecute = nameof(CanDeleteNodes))] private void DeleteSelectedNodes() => RemoveSelectedNodes();
    [RelayCommand(CanExecute = nameof(CanPlaceSamples))] private void SelectAllNodes() => SetNodeSelection(tracks.SelectMany(track => track.Samples));

    private void PruneNodeSelection() => SetNodeSelectionCore(selectedNodes);
    private void ResetNodeEditing() { CancelNodeDrag(); copiedNodes = []; ClearNodeSelection(); NotifyNodeEditingState(); }
    private void NotifyNodeEditingState()
    {
        if (!CanPlaceSamples) CancelNodeDrag();
        OnPropertyChanged(nameof(CanCopyNodes)); OnPropertyChanged(nameof(CanPasteNodes)); OnPropertyChanged(nameof(CanDeleteNodes));
        CopyNodesCommand.NotifyCanExecuteChanged(); PasteNodesCommand.NotifyCanExecuteChanged();
        DeleteSelectedNodesCommand.NotifyCanExecuteChanged(); SelectAllNodesCommand.NotifyCanExecuteChanged();
    }
}
