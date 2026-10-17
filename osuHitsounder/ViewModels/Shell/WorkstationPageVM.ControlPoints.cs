using CommunityToolkit.Mvvm.Input;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Resources.Messages;
using osuHitsounder.Services;

namespace osuHitsounder.ViewModels.Shell;

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
        !managingSamples && !instrumentBusy && !folderRenamesActive && !importBusy && !removalBusy && !IsNodeEditBusy;
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
                new ControlPointSnapshot(ControlPointId(p), p.Timestamp, p.Volume, p.Sampleset)).ToArray(),
            ++controlPointsVersion, canAdd, canAdd ? null : "Adding control points requires an active project, valid BPM timing, and a song duration.",
            pendingProjectWrites > 0, SaveError, controlPointsRevision, CanEditControlPoints), nameof(ControlPointsState));
    }

    private void PublishControlPointTimeline()
    {
        audioService.SetControlPoints(Project?.ControlPoints
            .Select(p => new ControlPointVolumeSnapshot(p.Timestamp, p.Volume)).ToArray() ?? []);
        RefreshControlPointTimestamps();
    }

    private void RefreshControlPointTimestamps()
    {
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
        return Task.CompletedTask;
    }

    private ControlPoint? FindControlPoint(ControlPointsContext context, Guid id) =>
        CanEditControlPoints && IsActiveControlPoints(context)
            ? Project!.ControlPoints.FirstOrDefault(p => ControlPointId(p) == id) : null;

    private void FinishControlPointMutation(bool volumeTimelineChanged)
    {
        controlPointsRevision++;
        SetProperty(ref controlPointError, null, nameof(ControlPointError));
        if (volumeTimelineChanged) PublishControlPointTimeline();
        QueueSave();
        PublishControlPointsSnapshot();
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

    [RelayCommand]
    private void MoveControlPoint(MoveControlPointRequestedMessage? request)
    {
        if (request is null || FindControlPoint(request.Context, request.PointId) is not { } point ||
            point.Timestamp == request.Timestamp ||
            !CanPlaceControlPoint(request.Context, request.ExpectedRevision, request.PointId, request.Timestamp)) return;
        point.Timestamp = request.Timestamp;
        FinishControlPointMutation(true);
    }

    [RelayCommand]
    private void DeleteControlPoint(DeleteControlPointRequestedMessage? request)
    {
        if (request is null || FindControlPoint(request.Context, request.PointId) is not { } point) return;
        Project!.ControlPoints.Remove(point);
        controlPointIds.Remove(point);
        FinishControlPointMutation(true);
    }

    [RelayCommand]
    private void SetControlPoint(SetControlPointRequestedMessage? request)
    {
        if (request is null || FindControlPoint(request.Context, request.PointId) is not { } point) return;
        if (request.Volume is { } volume && (volume is < 5 or > 100 || decimal.Truncate(volume) != volume))
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
        if (!volumeChanged && !samplesetChanged) return;
        if (volumeChanged) point.Volume = (int)request.Volume!.Value;
        if (samplesetChanged) point.Sampleset = request.Sampleset!;
        FinishControlPointMutation(volumeChanged);
    }

    [RelayCommand]
    private void RetryControlPointsSave(RetryControlPointsSaveRequestedMessage? request)
    {
        if (request is not null && CanEditControlPoints && IsActiveControlPoints(request.Context)) QueueSave();
    }
}
