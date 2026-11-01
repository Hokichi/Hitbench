using CommunityToolkit.Mvvm.Input;
using Hitbench.Core.Entities;
using Hitbench.Resources.DataModels;
using Hitbench.Resources.Messages;
using Hitbench.Services;

namespace Hitbench.ViewModels.Shell;

public partial class WorkstationPageVM
{
    private IReadOnlyList<int> controlPointTimestamps = Array.Empty<int>();
    public IReadOnlyList<int> ControlPointTimestamps => controlPointTimestamps;
    private readonly Dictionary<ControlPoint, Guid> controlPointIds = new(ReferenceEqualityComparer.Instance);
    private ControlPointsContext? controlPointsContext;
    private ControlPointsSnapshot? controlPointsState;
    public ControlPointsSnapshot? ControlPointsState => controlPointsState;
    private AddControlPointRequestedMessage? pendingControlPointAdd;
    public AddControlPointRequestedMessage? PendingControlPointAdd => pendingControlPointAdd;
    private string? controlPointError;
    public string? ControlPointError => controlPointError;
    private long controlPointsVersion;
    private long controlPointsRevision;
    public bool CanEditControlPoints => controlPointsContext is not null && CanEditTracks &&
        !instrumentBusy && !folderRenamesActive && !importBusy && !removalBusy && !IsNodeEditBusy;
    public bool CanOpenControlPoints => CanEditControlPoints && ControlPointTimestamp() is not null;

    private Guid ControlPointId(ControlPoint point)
    {
        if (!controlPointIds.TryGetValue(point, out var id)) controlPointIds[point] = id = Guid.NewGuid();
        return id;
    }

    private int? ControlPointTimestamp()
    {
        if (Project is null || Waveform is null || !IsInteractionReady) return null;
        var current = playback.Latest.Playback;
        return BeatGridCalculator.GetNearestTimestamp(Project.Offsets, Project.BPMs, Subdivision,
            current.IsPlaying ? current.Position.TotalMilliseconds : Position.TotalMilliseconds, Waveform.Duration.TotalMilliseconds);
    }

    private bool IsActiveControlPoints(ControlPointsContext context) => !deactivated &&
        controlPointsContext == context && context.ProjectPath == projectPath && Project is not null;

    private void InitializeControlPoints()
    {
        if (Project is null || projectPath is null || deactivated) return;
        controlPointsContext = new(projectPath, Guid.NewGuid());
        SetProperty(ref pendingControlPointAdd, null, nameof(PendingControlPointAdd));
        SetProperty(ref controlPointError, null, nameof(ControlPointError));
        PublishControlPointsSnapshot();
    }

    private void PublishControlPointsSnapshot()
    {
        if (controlPointsContext is not { } context || !IsActiveControlPoints(context)) return;
        var canAdd = CanOpenControlPoints;
        SetProperty(ref controlPointsState, new ControlPointsSnapshot(context,
            Project!.ControlPoints.OrderBy(p => p.Timestamp).Select(p =>
                new ControlPointSnapshot(ControlPointId(p), p.Timestamp, p.Volume, p.Sampleset, p.IsDisabled, Project.IsOffsetControlPoint(p), p.IsRamp)).ToArray(),
            ++controlPointsVersion, canAdd, canAdd ? null : "Adding control points requires an active project, valid BPM timing, and a song duration.",
            pendingProjectWrites > 0, SaveError, controlPointsRevision, CanEditControlPoints), nameof(ControlPointsState));
        NotifyControlPointCommands();
    }

    private void PublishControlPointTimeline()
    {
        audioService.SetControlPoints(Project?.ControlPoints
            .OrderBy(p => p.Timestamp).Select(p => new ControlPointVolumeSnapshot(p.Timestamp, p.Volume, p.IsDisabled, p.IsRamp)).ToArray() ?? []);
        RefreshControlPointTimestamps();
    }

    private void RefreshControlPointTimestamps()
    {
        OnPropertyChanged(nameof(ControlPointCount));
        var timestamps = Project?.ControlPoints.Select(p => p.Timestamp).Distinct().Order().ToArray() ?? [];
        if (controlPointTimestamps.SequenceEqual(timestamps)) return;
        controlPointTimestamps = Array.AsReadOnly(timestamps);
        OnPropertyChanged(nameof(ControlPointTimestamps));
    }

    [RelayCommand(CanExecute = nameof(CanOpenControlPoints))]
    private Task AddControlPointAsync()
    {
        if (!CanOpenControlPoints || controlPointsContext is not { } context || ControlPointTimestamp() is not { } timestamp)
            return Task.CompletedTask;
        SetProperty(ref controlPointError, null, nameof(ControlPointError));
        var predecessor = Project!.ControlPoints.Where(p => p.Timestamp < timestamp).OrderBy(p => p.Timestamp).LastOrDefault();
        SetProperty(ref pendingControlPointAdd, new AddControlPointRequestedMessage(context, Guid.NewGuid(),
            controlPointsRevision, timestamp, predecessor?.Volume ?? 100, predecessor?.Sampleset ?? "Normal"), nameof(PendingControlPointAdd));
        return Task.CompletedTask;
    }

    public Task SettleControlPointsAsync()
    {
        controlPointsContext = null;
        SetProperty(ref pendingControlPointAdd, null, nameof(PendingControlPointAdd));
        SetProperty(ref controlPointsState, null, nameof(ControlPointsState));
        SetProperty(ref controlPointError, null, nameof(ControlPointError));
        // Project load, return, and deactivation already drain the shared save queue at their own boundary.
        NotifyControlPointCommands();
        return Task.CompletedTask;
    }

    private ControlPoint? FindControlPoint(ControlPointsContext context, Guid id) =>
        CanEditControlPoints && IsActiveControlPoints(context)
            ? Project!.ControlPoints.FirstOrDefault(p => ControlPointId(p) == id) : null;

    private void FinishControlPointMutation(bool volumeTimelineChanged, bool queueSave = true)
    {
        controlPointsRevision++;
        SetProperty(ref controlPointError, null, nameof(ControlPointError));
        if (volumeTimelineChanged) PublishControlPointTimeline();
        if (queueSave) QueueSave();
        PublishControlPointsSnapshot();
        ResnapAllSamplesCommand.NotifyCanExecuteChanged();
    }

    private bool CanPlaceControlPoint(ControlPointsContext context, long revision, Guid pointId, int timestamp)
    {
        if (!CanEditControlPoints || !IsActiveControlPoints(context)) return false;
        var error = revision != controlPointsRevision ? "Control points changed. Try again."
            : Waveform is null || timestamp < 0 || timestamp >= Waveform.Duration.TotalMilliseconds
                ? "Choose a position within the song."
            : Project!.ControlPoints.Any(p => ControlPointId(p) != pointId && p.Timestamp == timestamp)
                ? "A control point already exists at this timestamp." : null;
        SetProperty(ref controlPointError, error, nameof(ControlPointError));
        return error is null;
    }

    [RelayCommand]
    private void CommitControlPointAdd(AddControlPointRequestedMessage? request)
    {
        if (request is null || request != pendingControlPointAdd) return;
        SetProperty(ref pendingControlPointAdd, null, nameof(PendingControlPointAdd));
        if (!CanPlaceControlPoint(request.Context, request.ExpectedRevision, request.PointId, request.Timestamp)) return;
        var point = new ControlPoint { Timestamp = request.Timestamp, Volume = request.Volume, Sampleset = request.Sampleset };
        controlPointIds[point] = request.PointId;
        Project!.ControlPoints.Add(point);
        FinishControlPointMutation(true);
    }

    private bool CanMoveControlPoint(MoveControlPointRequestedMessage? request) =>
        request is not null && request.ExpectedRevision == controlPointsRevision &&
        FindControlPoint(request.Context, request.PointId) is { } point && !Project!.IsOffsetControlPoint(point);

    [RelayCommand(CanExecute = nameof(CanMoveControlPoint))]
    private void MoveControlPoint(MoveControlPointRequestedMessage? request)
    {
        if (!CanMoveControlPoint(request) || FindControlPoint(request!.Context, request.PointId) is not { } point ||
            point.Timestamp == request.Timestamp ||
            !CanPlaceControlPoint(request.Context, request.ExpectedRevision, request.PointId, request.Timestamp)) return;
        point.Timestamp = request.Timestamp;
        FinishControlPointMutation(true);
    }

    private bool CanDeleteControlPoint(DeleteControlPointRequestedMessage? request) =>
        request is not null && FindControlPoint(request.Context, request.PointId) is { } point && !Project!.IsOffsetControlPoint(point);

    [RelayCommand(CanExecute = nameof(CanDeleteControlPoint))]
    private void DeleteControlPoint(DeleteControlPointRequestedMessage? request)
    {
        if (!CanDeleteControlPoint(request) || FindControlPoint(request!.Context, request.PointId) is not { } point) return;
        Project!.ControlPoints.Remove(point);
        controlPointIds.Remove(point);
        FinishControlPointMutation(true);
    }

    private bool CanSetControlPoint(SetControlPointRequestedMessage? request) =>
        request is not null && (request.IsRamp is null || request.ExpectedRevision == controlPointsRevision) && (request.ExpectedRevision is null || request.ExpectedRevision == controlPointsRevision) &&
        FindControlPoint(request.Context, request.PointId) is not null;

    [RelayCommand(CanExecute = nameof(CanSetControlPoint))]
    private void SetControlPoint(SetControlPointRequestedMessage? request)
    {
        if (!CanSetControlPoint(request) || FindControlPoint(request!.Context, request.PointId) is not { } point) return;
        if (request.Volume is { } volume && (volume is < ControlPoint.MinimumEditableVolume or > ControlPoint.MaximumVolume || decimal.Truncate(volume) != volume))
        {
            SetProperty(ref controlPointError, "Enter a whole number from 5 to 100.", nameof(ControlPointError));
            return;
        }
        if (request.Sampleset is { } sampleset && !ControlPoint.SamplesetOptions.Contains(sampleset))
        {
            SetProperty(ref controlPointError, "Choose Normal, Soft, or Drum.", nameof(ControlPointError));
            return;
        }
        var volumeChanged = request.Volume is { } nextVolume && nextVolume != point.Volume;
        var samplesetChanged = request.Sampleset is { } nextSampleset && nextSampleset != point.Sampleset;
        var modeChanged = request.IsRamp is { } mode && mode != point.IsRamp;
        if (!volumeChanged && !samplesetChanged && !modeChanged) return;
        if (volumeChanged) point.Volume = (int)request.Volume!.Value;
        if (samplesetChanged) point.Sampleset = request.Sampleset!;
        if (modeChanged) point.IsRamp = request.IsRamp!.Value;
        FinishControlPointMutation(volumeChanged || modeChanged);
    }

    private void NotifyControlPointCommands()
    {
        MoveControlPointCommand.NotifyCanExecuteChanged();
        DeleteControlPointCommand.NotifyCanExecuteChanged();
        SetControlPointCommand.NotifyCanExecuteChanged();
        SetControlPointDisabledCommand.NotifyCanExecuteChanged();
        SplitControlPointCommand.NotifyCanExecuteChanged();
    }

    private bool CanSetControlPointDisabled(SetControlPointDisabledRequestedMessage? request) =>
        request is not null && request.ExpectedRevision == controlPointsRevision &&
        FindControlPoint(request.Context, request.PointId) is { } point && point.IsDisabled != request.IsDisabled;

    [RelayCommand(CanExecute = nameof(CanSetControlPointDisabled))]
    private void SetControlPointDisabled(SetControlPointDisabledRequestedMessage? request)
    {
        if (!CanSetControlPointDisabled(request) || FindControlPoint(request!.Context, request.PointId) is not { } point) return;
        point.IsDisabled = request.IsDisabled;
        FinishControlPointMutation(true);
    }

    private int? GetControlPointSplitTimestamp(Guid pointId)
    {
        if (!CanOpenControlPoints || Waveform is null || Project is null) return null;
        var point = Project.ControlPoints.FirstOrDefault(p => ControlPointId(p) == pointId);
        if (point is null || Project.ControlPoints.Last(p => p.Timestamp == point.Timestamp) != point) return null;
        var end = Math.Min(Waveform.Duration.TotalMilliseconds,
            Project.ControlPoints.Where(p => p.Timestamp > point.Timestamp).Select(p => (double)p.Timestamp).DefaultIfEmpty(double.PositiveInfinity).Min());
        var playbackState = playback.Latest.Playback;
        var marker = (playbackState.IsPlaying ? playbackState.Position : Position).TotalMilliseconds;
        var rounded = Math.Round(marker, MidpointRounding.AwayFromZero);
        if (!double.IsFinite(rounded) || rounded < 0 || rounded > int.MaxValue || rounded <= point.Timestamp || rounded >= end ||
            Project.ControlPoints.Any(p => p.Timestamp == rounded)) return null;
        return (int)rounded;
    }

    private bool CanSplitControlPoint(SplitControlPointRequestedMessage? request) =>
        request is not null && request.ExpectedRevision == controlPointsRevision &&
        FindControlPoint(request.Context, request.PointId) is not null && GetControlPointSplitTimestamp(request.PointId) is not null;

    [RelayCommand(CanExecute = nameof(CanSplitControlPoint))]
    private void SplitControlPoint(SplitControlPointRequestedMessage? request)
    {
        if (!CanSplitControlPoint(request) || FindControlPoint(request!.Context, request.PointId) is not { } source ||
            GetControlPointSplitTimestamp(request.PointId) is not { } timestamp) return;
        var curve = new ControlPointVolumeTimeline(Project!.ControlPoints.Select(p => new ControlPointVolumeSnapshot(p.Timestamp, p.Volume, p.IsDisabled, p.IsRamp)).ToArray());
        var point = new ControlPoint { Timestamp = timestamp, Volume = source.IsDisabled ? source.Volume : (int)Math.Round(curve.GetVolumeAtTimestamp(timestamp), MidpointRounding.AwayFromZero), Sampleset = source.Sampleset, IsDisabled = source.IsDisabled, IsRamp = source.IsRamp };
        controlPointIds[point] = Guid.NewGuid();
        Project!.ControlPoints.Add(point);
        FinishControlPointMutation(true);
    }

    [RelayCommand]
    private void RetryControlPointsSave(RetryControlPointsSaveRequestedMessage? request)
    {
        if (request is not null && CanEditControlPoints && IsActiveControlPoints(request.Context)) QueueSave();
    }
}
