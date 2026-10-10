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
    public Track? DragDestinationTrack { get; private set; }
    private Task<bool>? activeNodeEdit;
    private CancellationTokenSource? nodeEditCancellation;
    private int nodeEditGeneration = -1;
    public bool IsNodeEditBusy => nodeEditGeneration == loadGeneration && activeNodeEdit is { IsCompleted: false };
    private async Task<bool> CollisionAsync(CancellationToken token)
    {
        ErrorMessage = "Existing sample nodes found.";
        if (nodePasteDialog is not null) await nodePasteDialog.ShowCollisionAsync(token);
        return false;
    }
    private bool HasNodeCollisions(IEnumerable<(Track Track, Sample Sample, int Timestamp)> proposed, IEnumerable<Sample> moving)
    {
        var ignored = moving.ToHashSet();
        var used = new HashSet<(Track, int)>();
        foreach (var node in proposed)
            if (!used.Add((node.Track, node.Timestamp)) || node.Track.Samples.Any(other => !ignored.Contains(other) && other.Timestamp == node.Timestamp)) return true;
        return false;
    }
    private IReadOnlyDictionary<Sample, int> nodePreview = new ReadOnlyDictionary<Sample, int>(new Dictionary<Sample, int>());

    public IReadOnlyCollection<Sample> SelectedSamples => selectedNodes;
    public IReadOnlyDictionary<Sample, int> NodePreviewTimestamps => nodePreview;
    public bool IsNodeDragActive => dragAnchor is not null;
    public bool CanCopyNodes => CanPlaceSamples && selectedNodes.Count > 0;
    public bool CanPasteNodes => CanPlaceSamples && SelectedTrack is not null && tracks.Contains(SelectedTrack) && copiedNodes.Length > 0 &&
        (nodePasteDialog is not null || copiedNodes.All(node => ReferenceEquals(node.Track, SelectedTrack)));
    public bool CanDeleteNodes => CanCopyNodes;

    public bool SelectNode(Track track, Sample sample, bool toggle)
    {
        if (!CanPlaceSamples || !tracks.Contains(track) || !track.Samples.Contains(sample)) return false;
        SelectedTrack = track;
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
            (long)entry.Sample.Timestamp - first, entry.Sample.Timestamp)).ToArray();
        NotifyNodeEditingState();
        return true;
    }

    public Task<bool> PasteCopiedNodesAsync()
    {
        if (!CanPasteNodes || IsNodeEditBusy) return Task.FromResult(false);
        return StartNodeEdit(PasteNodesCoreAsync);
    }

    private Task<bool> StartNodeEdit(Func<Task<bool>> action)
    {
        activeNodeEdit = null;
        nodeEditGeneration = loadGeneration;
        nodeEditCancellation?.Dispose();
        nodeEditCancellation = CancellationTokenSource.CreateLinkedTokenSource(loadCancellation?.Token ?? CancellationToken.None);
        activeNodeEdit = action();
        var observed = ObserveNodeEditAsync(activeNodeEdit, nodeEditGeneration);
        activeNodeEdit = observed;
        return observed;
    }

    private async Task<bool> ObserveNodeEditAsync(Task<bool> operation, int generation)
    {
        NotifyNodeEditingState(); OnPropertyChanged(nameof(IsNodeEditBusy)); OnPropertyChanged(nameof(CanPlaceSamples)); OnPropertyChanged(nameof(CanImportSample)); OnPropertyChanged(nameof(CanRemoveTrack)); OnPropertyChanged(nameof(HasPendingSaves));
        try { return await operation; }
        catch (OperationCanceledException) { return false; }
        catch (Exception error) { if (generation == loadGeneration && !deactivated) ErrorMessage = error.Message; return false; }
        finally { if (generation == loadGeneration) { activeNodeEdit = null; nodeEditCancellation?.Dispose(); nodeEditCancellation = null; NotifyNodeEditingState(); OnPropertyChanged(nameof(IsNodeEditBusy)); OnPropertyChanged(nameof(CanPlaceSamples)); OnPropertyChanged(nameof(CanImportSample)); OnPropertyChanged(nameof(CanRemoveTrack)); OnPropertyChanged(nameof(HasPendingSaves)); } }
    }

    private async Task<bool> PasteNodesCoreAsync()
    {
        var destination = SelectedTrack!;
        var copied = copiedNodes.ToArray();
        var project = Project!; var path = projectPath!; var generation = loadGeneration;
        var marker = Position.TotalMilliseconds;
        var token = nodeEditCancellation?.Token ?? loadCancellation?.Token ?? CancellationToken.None;
        NodePasteTiming? timing = NodePasteTiming.PlaybackMarker;
        if (copied.Any(node => !ReferenceEquals(node.Track, destination)))
        {
            if (nodePasteDialog is null) return false;
            timing = await nodePasteDialog.ChooseTimingAsync(token);
        }
        if (timing is not (NodePasteTiming.SourceTimestamps or NodePasteTiming.PlaybackMarker) || !IsCurrentProject(path, project, generation) || !tracks.Contains(destination) || !CanEditTracks || importBusy || removalBusy) return false;
        var file = availableSamples.FirstOrDefault(name => string.Equals(name, destination.SelectedSampleFileName, StringComparison.OrdinalIgnoreCase));
        if (file is null) return RejectPaste("The destination sample file is unavailable.");
        var anchor = BeatGridCalculator.GetNearestTimestamp(project.Offsets, project.BPMs, Subdivision, marker, Waveform!.Duration.TotalMilliseconds);
        if (anchor is null) return RejectPaste("No usable grid beat for pasting samples.");
        var batch = new List<NodeHistoryEntry>();
        foreach (var node in copied)
        {
            var candidate = timing == NodePasteTiming.SourceTimestamps ? node.Timestamp : (long)anchor!.Value + node.RelativeTimestamp;
            if (!ValidNodeTimestamp(candidate)) return RejectPaste("The copied formation does not fit within the song.");
            var timestamp = timing == NodePasteTiming.SourceTimestamps ? node.Timestamp : BeatGridCalculator.GetNearestTimestamp(project.Offsets, project.BPMs, Subdivision, candidate, Waveform.Duration.TotalMilliseconds);
            if (timestamp is null) return RejectPaste("No usable grid beat for copied samples.");
            var sample = new Sample { FileName = file, Volume = node.Volume, Timestamp = timestamp.Value };
            batch.Add(new(destination, sample, destination.Samples.Count + batch.Count, NodeHistoryOperation.Placement));
        }
        CancelNodeDrag();
        if (HasNodeCollisions(batch.Select(entry => (entry.Track, entry.Sample, entry.Sample.Timestamp)), [])) return await CollisionAsync(token);
        foreach (var entry in batch) destination.Samples.Add(entry.Sample);
        history.RecordBatch(batch); SetNodeSelectionCore(batch.Select(entry => entry.Sample));
        PublishNodeChange(); ErrorMessage = null; return true;
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

    public void PreviewNodeDrag(double deltaMilliseconds, Track? destinationTrack = null)
    {
        if (!IsNodeDragActive) return;
        DragDestinationTrack = destinationTrack;
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

    public Task<bool> CommitNodeDragAsync()
    {
        if (!IsNodeDragActive || IsNodeEditBusy) return Task.FromResult(false);
        return StartNodeEdit(CommitNodeDragCoreAsync);
    }

    private async Task<bool> CommitNodeDragCoreAsync()
    {
        if (!IsNodeDragActive) return false;
        var destination = DragDestinationTrack;
        var entries = dragNodes.Select(entry =>
        {
            var candidate = entry.PreviousTimestamp + dragDelta;
            var next = destination is null ? (int?)candidate : BeatGridCalculator.GetNearestTimestamp(Project!.Offsets, Project.BPMs, Subdivision, candidate, Waveform!.Duration.TotalMilliseconds);
            return entry with { NextTimestamp = next ?? -1 };
        }).ToArray();
        var valid = CanPlaceSamples && (dragDelta != 0 || destination is not null && entries.Any(entry => !ReferenceEquals(entry.Track, destination))) &&
            entries.All(entry => tracks.Contains(entry.Track) && entry.Track.Samples.Contains(entry.Sample) && entry.Sample.Timestamp == entry.PreviousTimestamp && ValidNodeTimestamp(entry.NextTimestamp));
        CancelNodeDrag();
        if (!valid || destination is not null && !tracks.Contains(destination)) return false;
        var destinationFile = destination is null ? null : availableSamples.FirstOrDefault(file => string.Equals(file, destination.SelectedSampleFileName, StringComparison.OrdinalIgnoreCase));
        if (destination is not null && destinationFile is null) return RejectPaste("The destination sample file is unavailable.");
        if (HasNodeCollisions(entries.Select(entry => (destination ?? entry.Track, entry.Sample, entry.NextTimestamp)), entries.Select(entry => entry.Sample)))
            return await CollisionAsync(nodeEditCancellation?.Token ?? loadCancellation?.Token ?? CancellationToken.None);
        var batch = new List<NodeHistoryEntry>();
        foreach (var entry in entries)
        {
            if (destination is not null && !ReferenceEquals(entry.Track, destination))
            {
                var sourceIndex = entry.Track.Samples.IndexOf(entry.Sample);
                var targetIndex = destination.Samples.Count;
                batch.Add(entry with { Index = sourceIndex, DestinationTrack = destination, DestinationIndex = targetIndex,
                    PreviousFileName = entry.Sample.FileName, NextFileName = destinationFile });
                entry.Track.Samples.Remove(entry.Sample); destination.Samples.Add(entry.Sample);
                entry.Sample.FileName = destinationFile!;
            }
            else batch.Add(entry);
            entry.Sample.Timestamp = entry.NextTimestamp;
        }
        history.RecordBatch(batch); if (destination is not null) SelectedTrack = destination;
        PublishNodeChange(); return true;
    }

    public void CancelNodeDrag()
    {
        var active = IsNodeDragActive;
        dragAnchor = null; dragNodes = []; dragDelta = 0; DragDestinationTrack = null;
        if (nodePreview.Count > 0)
        {
            nodePreview = new ReadOnlyDictionary<Sample, int>(new Dictionary<Sample, int>());
            OnPropertyChanged(nameof(NodePreviewTimestamps));
        }
        if (active) OnPropertyChanged(nameof(IsNodeDragActive));
    }

    public async Task<bool> MoveSelectedNodesAsync(bool forward)
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
        return await CommitNodeDragAsync();
    }

    public async Task<bool> TransferSelectedNodesAsync(bool forward)
    {
        if (!CanCopyNodes) return false;
        var entry = SelectedNodeEntries(NodeHistoryOperation.Movement).FirstOrDefault();
        if (entry is null) return false;
        var index = tracks.IndexOf(entry.Track) + (forward ? 1 : -1);
        if (index < 0 || index >= tracks.Count || !BeginNodeDrag(entry.Track, entry.Sample)) return false;
        PreviewNodeDrag(0, tracks[index]);
        return await CommitNodeDragAsync();
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
    [RelayCommand(CanExecute = nameof(CanPasteNodes))] private Task PasteNodesAsync() => PasteCopiedNodesAsync();
    [RelayCommand(CanExecute = nameof(CanDeleteNodes))] private void DeleteSelectedNodes() => RemoveSelectedNodes();
    [RelayCommand(CanExecute = nameof(CanPlaceSamples))] private void SelectAllNodes() => SetNodeSelection(tracks.SelectMany(track => track.Samples));

    private void PruneNodeSelection() => SetNodeSelectionCore(selectedNodes);
    private void ResetNodeEditing() { nodeEditCancellation?.Cancel(); SelectedTrack = null; CancelNodeDrag(); copiedNodes = []; ClearNodeSelection(); NotifyNodeEditingState(); }
    private void NotifyNodeEditingState()
    {
        if (!CanPlaceSamples) CancelNodeDrag();
        OnPropertyChanged(nameof(CanCopyNodes)); OnPropertyChanged(nameof(CanPasteNodes)); OnPropertyChanged(nameof(CanDeleteNodes));
        CopyNodesCommand.NotifyCanExecuteChanged(); PasteNodesCommand.NotifyCanExecuteChanged();
        DeleteSelectedNodesCommand.NotifyCanExecuteChanged(); SelectAllNodesCommand.NotifyCanExecuteChanged();
    }
}
