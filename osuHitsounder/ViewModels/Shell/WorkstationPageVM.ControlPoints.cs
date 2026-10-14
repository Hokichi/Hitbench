using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Resources.Messages;
using osuHitsounder.Services;

namespace osuHitsounder.ViewModels.Shell;

public partial class WorkstationPageVM
{
    private readonly Dictionary<ControlPoint, Guid> controlPointIds = new(ReferenceEqualityComparer.Instance);
    private ControlPointsContext? controlPointsContext;
    private CancellationTokenSource? controlPointsCancellation;
    private Task? controlPointsWorkflow;
    private Task controlPointNotifications = Task.CompletedTask;
    private bool controlPointsBusy;
    private long controlPointsVersion;
    public bool CanOpenControlPoints => CanEditTracks && controlPointsDialog is not null && messenger is not null &&
        !managingSamples && !instrumentBusy && !folderRenamesActive && !importBusy && !removalBusy && !IsNodeEditBusy;
    private Guid ControlPointId(ControlPoint point)
    {
        if (!controlPointIds.TryGetValue(point, out var id)) controlPointIds[point] = id = Guid.NewGuid();
        return id;
    }
    private int? ControlPointTimestamp() => Project is not null && Waveform is not null && IsInteractionReady
        ? BeatGridCalculator.GetNearestTimestamp(Project.Offsets, Project.BPMs, Subdivision,
            IsPlaying ? playback.Latest.Playback.Position.TotalMilliseconds : Position.TotalMilliseconds, Waveform.Duration.TotalMilliseconds) : null;
    private bool IsActiveControlPoints(ControlPointsContext context) => !deactivated && controlPointsCancellation?.IsCancellationRequested == false && controlPointsContext == context &&
        context.SessionId == sampleSession && context.ProjectPath == projectPath && Project is not null;
    private ControlPointsSnapshot ControlPointsSnapshot(ControlPointsContext context)
    {
        var canAdd = ControlPointTimestamp() is not null;
        return new(context, Project!.ControlPoints.OrderBy(p => p.Timestamp).Select(p =>
                new ControlPointSnapshot(ControlPointId(p), p.Timestamp, p.Volume, p.Sampleset)).ToArray(),
            ++controlPointsVersion, canAdd, canAdd ? null : "Adding control points requires valid BPM timing and a song duration.",
            pendingProjectWrites > 0, SaveError);
    }
    private void PublishControlPointsSnapshot()
    {
        if (controlPointsContext is { } context && IsActiveControlPoints(context))
            messenger?.Send(new ControlPointsChangedMessage(ControlPointsSnapshot(context)));
    }
    private void PublishControlPointTimeline() => audioService.SetControlPoints(Project?.ControlPoints
        .Select(p => new ControlPointVolumeSnapshot(p.Timestamp, p.Volume)).ToArray() ?? []);
    [RelayCommand(CanExecute = nameof(CanOpenControlPoints))]
    private async Task AddControlPointAsync()
    {
        if (!CanOpenControlPoints || projectPath is null) return;
        controlPointsBusy = true;
        controlPointsCancellation?.Dispose();
        controlPointsCancellation = CancellationTokenSource.CreateLinkedTokenSource(loadCancellation?.Token ?? default);
        controlPointsContext = new(projectPath, sampleSession, Guid.NewGuid());
        NotifyEditingState();
        await (controlPointsWorkflow = RunControlPointsAsync(controlPointsContext, controlPointsCancellation.Token));
    }
    private async Task RunControlPointsAsync(ControlPointsContext context, CancellationToken token)
    {
        try { await controlPointsDialog!.ShowAsync(ControlPointsSnapshot(context), token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { ErrorMessage = error.Message; }
        finally
        {
            await controlPointNotifications;
            controlPointsContext = null;
            await saveTail;
            controlPointsBusy = false; NotifyEditingState();
        }
    }
    public async Task SettleControlPointsDrawerAsync()
    {
        controlPointsCancellation?.Cancel();
        if (controlPointsWorkflow is { } operation) await operation;
        await controlPointNotifications;
        controlPointsWorkflow = null;
        controlPointsCancellation?.Dispose(); controlPointsCancellation = null;
    }
    private void RegisterControlPointMessages()
    {
        if (messenger is null) return;
        messenger.Register<ControlPointsReadyMessage>(this, (_, m) => QueueControlPointRequest(m.Context, PublishControlPointsSnapshot));
        messenger.Register<AddControlPointRequestedMessage>(this, (_, m) => QueueControlPointRequest(m.Context, () => AddPoint(m.Context)));
        messenger.Register<SetControlPointRequestedMessage>(this, (_, m) => QueueControlPointRequest(m.Context, () => SetPoint(m)));
        messenger.Register<RetryControlPointsSaveRequestedMessage>(this, (_, m) => QueueControlPointRequest(m.Context, QueueSave));
    }
    private void QueueControlPointRequest(ControlPointsContext context, Action mutation) =>
        controlPointNotifications = ApplyControlPointRequestAsync(controlPointNotifications, context, mutation);
    private async Task ApplyControlPointRequestAsync(Task previous, ControlPointsContext context, Action mutation)
    {
        await previous;
        try { await uiScheduler.InvokeAsync(() => { if (IsActiveControlPoints(context)) mutation(); }, CancellationToken.None); }
        catch (Exception error) { ErrorMessage = error.Message; }
    }
    private void AddPoint(ControlPointsContext context)
    {
        if (!IsActiveControlPoints(context) || ControlPointTimestamp() is not { } timestamp) return;
        if (Project!.ControlPoints.Any(p => p.Timestamp == timestamp)) return;
        var predecessor = Project.ControlPoints.Where(p => p.Timestamp < timestamp).OrderBy(p => p.Timestamp).LastOrDefault();
        Project.ControlPoints.Add(new() { Timestamp = timestamp, Volume = predecessor?.Volume ?? 100 });
        PublishControlPointTimeline(); QueueSave(); PublishControlPointsSnapshot();
    }
    private void SetPoint(SetControlPointRequestedMessage request)
    {
        if (!IsActiveControlPoints(request.Context) || !IsInteractionReady || request.Volume is < 0 or > 100 ||
            decimal.Truncate(request.Volume) != request.Volume || !Track.SamplesetOptions.Contains(request.Sampleset)) return;
        var point = Project!.ControlPoints.FirstOrDefault(p => ControlPointId(p) == request.PointId);
        if (point is null || point.Volume == request.Volume && point.Sampleset == request.Sampleset) return;
        var volumeChanged = point.Volume != request.Volume;
        point.Volume = (int)request.Volume; point.Sampleset = request.Sampleset;
        if (volumeChanged) PublishControlPointTimeline();
        QueueSave(); PublishControlPointsSnapshot();
    }
}
