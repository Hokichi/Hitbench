using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Hitbench.Core.Entities;
using Hitbench.Services;

namespace Hitbench.ViewModels.Shell;

public partial class WorkstationPageVM
{
    private IReadOnlyCollection<Sample> selectedNodes = Array.AsReadOnly(Array.Empty<Sample>());
    private CopiedSampleNode[] copiedNodes = [];
    private NodeHistoryEntry[] dragNodes = [];
    private Track[] dragTrackOrder = [];
    private bool dragPreviewValid;
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
        var occupied = new Dictionary<Track, HashSet<int>>();
        foreach (var node in proposed)
        {
            if (!occupied.TryGetValue(node.Track, out var timestamps))
                occupied[node.Track] = timestamps = node.Track.Samples.Where(other => !ignored.Contains(other)).Select(other => other.Timestamp).ToHashSet();
            if (!used.Add((node.Track, node.Timestamp)) || timestamps.Contains(node.Timestamp)) return true;
        }
        return false;
    }
    private IReadOnlyDictionary<Sample, int> nodePreview = new ReadOnlyDictionary<Sample, int>(new Dictionary<Sample, int>());
    private IReadOnlyDictionary<Track, IReadOnlyList<int>> nodePreviewByTrack = new ReadOnlyDictionary<Track, IReadOnlyList<int>>(new Dictionary<Track, IReadOnlyList<int>>());

    public IReadOnlyCollection<Sample> SelectedSamples => selectedNodes;
    public IReadOnlyDictionary<Sample, int> NodePreviewTimestamps => nodePreview;
    public IReadOnlyDictionary<Track, IReadOnlyList<int>> NodePreviewByTrack => nodePreviewByTrack;
    public bool IsNodeDragActive => dragAnchor is not null;
    public bool IsNodeDragCopy { get; private set; }
    public void SetNodeDragCopy(bool value)
    {
        if (!IsNodeDragActive || IsNodeDragCopy == value) return;
        IsNodeDragCopy = value;
        OnPropertyChanged(nameof(IsNodeDragCopy));
    }
    public bool CanCopyNodes => CanPlaceSamples && SelectedNodeEntries(NodeHistoryOperation.Movement).Length > 0;
    public bool CanPasteNodes => CanPlaceSamples && copiedNodes.Length > 0 && SelectedTrack is { } track &&
        CanEditTrackGrid(track) && NodeFileForTrack(track) is not null;
    public bool CanDeleteNodes => CanCopyNodes;

    public bool SelectNode(Track track, Sample sample, bool toggle)
    {
        if (!CanPlaceSamples || !CanEditTrackGrid(track) || !track.Samples.Contains(sample)) return false;
        if (!IsStaticMarker) ScrollWithMarker = false;
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
        var live = tracks.Where(CanEditTrackGrid).SelectMany(track => track.Samples).ToHashSet();
        var next = samples.Where(live.Contains).Distinct().ToArray();
        if (next.Length > 0 && !IsStaticMarker) ScrollWithMarker = false;
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

    private NodeHistoryEntry[] SelectedNodeEntries(NodeHistoryOperation operation) => tracks.Where(CanEditTrackGrid)
        .SelectMany(track => track.Samples.Select((sample, index) => new NodeHistoryEntry(track, sample, index, operation,
            sample.Timestamp, sample.Timestamp))).Where(entry => selectedNodes.Contains(entry.Sample)).ToArray();

    public bool CopySelectedNodes()
    {
        if (!CanCopyNodes) return false;
        var entries = SelectedNodeEntries(NodeHistoryOperation.Placement);
        if (entries.Length == 0) return false;
        var first = entries.Min(entry => entry.Sample.Timestamp);
        copiedNodes = entries.Select(entry => new CopiedSampleNode((long)entry.Sample.Timestamp - first)).ToArray();
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
        var destination = SelectedTrack;
        if (destination is null || !CanEditTrackGrid(destination)) return false;
        var fileName = NodeFileForTrack(destination);
        if (fileName is null) return false;
        var copied = copiedNodes.ToArray();
        var project = Project!; var path = projectPath!; var generation = loadGeneration;
        var marker = Position.TotalMilliseconds;
        var token = nodeEditCancellation?.Token ?? loadCancellation?.Token ?? CancellationToken.None;
        if (!IsCurrentProject(path, project, generation) || !CanEditTracks || importBusy || removalBusy) return false;
        var anchor = BeatGridCalculator.GetNearestTimestamp(project.UninheritedPoints, Subdivision, marker, Waveform!.Duration.TotalMilliseconds);
        if (anchor is null) return RejectPaste("No usable grid beat for pasting samples.");
        var batch = new List<NodeHistoryEntry>();
        foreach (var node in copied)
        {
            var candidate = (long)anchor.Value + node.RelativeTimestamp;
            if (candidate >= Waveform.Duration.TotalMilliseconds) continue;
            if (!ValidNodeTimestamp(candidate)) return RejectPaste("The copied formation does not fit within the song.");
            var sample = new Sample { FileName = fileName, Timestamp = (int)candidate };
            batch.Add(new(destination, sample, destination.Samples.Count + batch.Count, NodeHistoryOperation.Placement));
        }
        if (batch.Count == 0) return false;
        CancelNodeDrag();
        if (HasNodeCollisions(batch.Select(entry => (entry.Track, entry.Sample, entry.Sample.Timestamp)), [])) return await CollisionAsync(token);
        foreach (var entry in batch) entry.Track.Samples.Add(entry.Sample);
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
        if (!CanPlaceSamples || !CanEditTrackGrid(track) || !track.Samples.Contains(anchor)) return false;
        if (!selectedNodes.Contains(anchor)) SelectNode(track, anchor, false);
        var entries = SelectedNodeEntries(NodeHistoryOperation.Movement);
        if (entries.Length == 0 || entries.Any(entry => !ValidNodeTimestamp(entry.PreviousTimestamp))) return false;
        dragNodes = entries;
        dragTrackOrder = tracks.ToArray();
        dragAnchor = anchor;
        dragDelta = 0;
        OnPropertyChanged(nameof(IsNodeDragActive));
        NotifySampleReloadAvailability();
        return true;
    }

    private bool TryGetNodeDragDestinations(out Track[] destinations)
    {
        destinations = [];
        if (!IsNodeDragActive || !tracks.SequenceEqual(dragTrackOrder)) return false;
        var anchorTrack = dragNodes.Single(entry => ReferenceEquals(entry.Sample, dragAnchor)).Track;
        var shift = DragDestinationTrack is null ? 0 : Array.IndexOf(dragTrackOrder, DragDestinationTrack) - Array.IndexOf(dragTrackOrder, anchorTrack);
        if (DragDestinationTrack is not null && !tracks.Contains(DragDestinationTrack)) return false;
        var targets = new Track[dragNodes.Length];
        for (var i = 0; i < dragNodes.Length; i++)
        {
            var entry = dragNodes[i];
            var index = Array.IndexOf(dragTrackOrder, entry.Track) + shift;
            if (index < 0 || index >= dragTrackOrder.Length || !CanEditTrackGrid(entry.Track) ||
                !entry.Track.Samples.Contains(entry.Sample) || entry.Sample.Timestamp != entry.PreviousTimestamp ||
                !CanEditTrackGrid(dragTrackOrder[index])) return false;
            targets[i] = dragTrackOrder[index];
        }
        destinations = targets;
        return true;
    }

    public bool PreviewNodeDrag(double deltaMilliseconds, Track? destinationTrack = null)
    {
        if (!IsNodeDragActive) return false;
        DragDestinationTrack = destinationTrack;
        dragPreviewValid = false;
        if (!CanPlaceSamples || !double.IsFinite(deltaMilliseconds)) { CancelNodeDrag(); return false; }
        if (!TryGetNodeDragDestinations(out var destinations))
        {
            ClearNodeDragPreview();
            return false;
        }
        var anchor = dragNodes.Single(entry => ReferenceEquals(entry.Sample, dragAnchor)).PreviousTimestamp;
        var minimum = anchor - dragNodes.Min(entry => entry.PreviousTimestamp);
        var last = (int)Math.Min(int.MaxValue, Math.Ceiling(Waveform!.Duration.TotalMilliseconds) - 1);
        var maximum = last - (dragNodes.Max(entry => entry.PreviousTimestamp) - anchor);
        var snapped = BeatGridCalculator.GetNearestTimestamp(Project!.UninheritedPoints, Subdivision,
            anchor + deltaMilliseconds, Waveform.Duration.TotalMilliseconds, minimum, maximum);
        if (snapped is null) { ClearNodeDragPreview(); return false; }
        dragDelta = (long)snapped.Value - anchor;
        dragPreviewValid = true;
        nodePreview = new ReadOnlyDictionary<Sample, int>(dragNodes.ToDictionary(entry => entry.Sample,
            entry => (int)(entry.PreviousTimestamp + dragDelta)));
        nodePreviewByTrack = new ReadOnlyDictionary<Track, IReadOnlyList<int>>(dragNodes
            .Select((entry, i) => (Track: destinations[i], Timestamp: nodePreview[entry.Sample]))
            .GroupBy(node => node.Track).ToDictionary(group => group.Key,
                group => (IReadOnlyList<int>)Array.AsReadOnly(group.Select(node => node.Timestamp).ToArray())));
        OnPropertyChanged(nameof(NodePreviewTimestamps));
        OnPropertyChanged(nameof(NodePreviewByTrack));
        return dragPreviewValid;
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
        var copy = IsNodeDragCopy;
        if (!CanPlaceSamples || !dragPreviewValid || !TryGetNodeDragDestinations(out var destinations)) { CancelNodeDrag(); return false; }
        var entries = dragNodes.Select(entry =>
        {
            var candidate = entry.PreviousTimestamp + dragDelta;
            var next = destination is null ? (int?)candidate : BeatGridCalculator.GetNearestTimestamp(Project!.UninheritedPoints, Subdivision, candidate, Waveform!.Duration.TotalMilliseconds);
            return entry with { NextTimestamp = next ?? -1 };
        }).ToArray();
        var valid = (copy || dragDelta != 0 || entries.Where((entry, i) => !ReferenceEquals(entry.Track, destinations[i])).Any()) &&
            entries.All(entry => ValidNodeTimestamp(entry.NextTimestamp));
        var completeOverlap = entries.Select((entry, i) => ReferenceEquals(entry.Track, destinations[i]) &&
            entry.NextTimestamp == entry.PreviousTimestamp).All(unchanged => unchanged);
        CancelNodeDrag();
        if (!valid || (copy && completeOverlap)) return false;
        var destinationFiles = destinations.Select(NodeFileForTrack).ToArray();
        if (destinationFiles.Any(file => file is null))
            return RejectPaste("The destination sample file is unavailable.");
        if (HasNodeCollisions(entries.Select((entry, i) => (destinations[i], entry.Sample, entry.NextTimestamp)), copy ? [] : entries.Select(entry => entry.Sample)))
            return await CollisionAsync(nodeEditCancellation?.Token ?? loadCancellation?.Token ?? CancellationToken.None);
        var batch = new List<NodeHistoryEntry>();
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            var target = destinations[i];
            if (copy)
            {
                var sample = new Sample { Timestamp = entry.NextTimestamp, FileName = destinationFiles[i]! };
                batch.Add(new(target, sample, target.Samples.Count, NodeHistoryOperation.Placement));
                target.Samples.Add(sample);
                continue;
            }
            if (!ReferenceEquals(entry.Track, target))
            {
                var sourceIndex = entry.Track.Samples.IndexOf(entry.Sample);
                var targetIndex = target.Samples.Count;
                batch.Add(entry with { Index = sourceIndex, DestinationTrack = target, DestinationIndex = targetIndex,
                    PreviousFileName = entry.Sample.FileName, NextFileName = destinationFiles[i] });
                entry.Track.Samples.Remove(entry.Sample); target.Samples.Add(entry.Sample);
            }
            else batch.Add(entry);
            entry.Sample.Timestamp = entry.NextTimestamp;
            entry.Sample.FileName = destinationFiles[i]!;
        }
        history.RecordBatch(batch); if (destination is not null) SelectedTrack = destination;
        if (copy) SetNodeSelectionCore(batch.Select(entry => entry.Sample));
        PublishNodeChange(); return true;
    }

    public void ClearNodeDragPreview()
    {
        dragPreviewValid = false;
        if (nodePreview.Count == 0 && nodePreviewByTrack.Count == 0) return;
        nodePreview = new ReadOnlyDictionary<Sample, int>(new Dictionary<Sample, int>());
        nodePreviewByTrack = new ReadOnlyDictionary<Track, IReadOnlyList<int>>(new Dictionary<Track, IReadOnlyList<int>>());
        OnPropertyChanged(nameof(NodePreviewTimestamps));
        OnPropertyChanged(nameof(NodePreviewByTrack));
    }

    public void CancelNodeDrag()
    {
        var active = IsNodeDragActive;
        SetNodeDragCopy(false);
        dragAnchor = null; dragNodes = []; dragTrackOrder = []; dragDelta = 0; DragDestinationTrack = null;
        ClearNodeDragPreview();
        if (active)
        {
            OnPropertyChanged(nameof(IsNodeDragActive));
            NotifySampleReloadAvailability();
        }
    }

    public async Task<bool> MoveSelectedNodesAsync(bool forward)
    {
        if (!CanCopyNodes) return false;
        var earliest = SelectedNodeEntries(NodeHistoryOperation.Movement).MinBy(entry => entry.PreviousTimestamp);
        if (earliest is null) return false;
        var time = TimeSpan.FromMilliseconds(earliest.PreviousTimestamp);
        var next = BeatGridCalculator.GetAdjacentPosition(Project!.UninheritedPoints, Subdivision, time, Waveform!.Duration, forward);
        if (next is not null && Math.Round(next.Value.TotalMilliseconds, MidpointRounding.AwayFromZero) == earliest.PreviousTimestamp)
            next = BeatGridCalculator.GetAdjacentPosition(Project.UninheritedPoints, Subdivision,
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

    [RelayCommand(CanExecute = nameof(CanCopyNodes))] private void CopyNodes() => CopySelectedNodes();
    [RelayCommand(CanExecute = nameof(CanPasteNodes))] private Task PasteNodesAsync() => PasteCopiedNodesAsync();
    [RelayCommand(CanExecute = nameof(CanDeleteNodes))] private void DeleteSelectedNodes() => RemoveSelectedNodes();
    [RelayCommand(CanExecute = nameof(CanPlaceSamples))] private void SelectAllNodes() => SetNodeSelection(tracks.SelectMany(track => track.Samples));

    private void PruneNodeSelection() => SetNodeSelectionCore(selectedNodes);
    private void ResetNodeEditing() { nodeEditCancellation?.Cancel(); SelectedTrack = null; CancelNodeDrag(); copiedNodes = []; ClearNodeSelection(); NotifyNodeEditingState(); }
    private void NotifyNodeEditingState()
    {
        NotifyTrackAuditionState();
        if (!CanPlaceSamples) CancelNodeDrag();
        OnPropertyChanged(nameof(CanCopyNodes)); OnPropertyChanged(nameof(CanPasteNodes)); OnPropertyChanged(nameof(CanDeleteNodes));
        CopyNodesCommand.NotifyCanExecuteChanged(); PasteNodesCommand.NotifyCanExecuteChanged();
        DeleteSelectedNodesCommand.NotifyCanExecuteChanged(); SelectAllNodesCommand.NotifyCanExecuteChanged();
    }
}
