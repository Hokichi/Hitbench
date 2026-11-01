using System.Windows;
using Hitbench.Resources.DataModels;
using Hitbench.Resources.Helpers;
using Hitbench.Resources.Messages;

namespace Hitbench.Views.Pages;

public partial class OHsControlPointLane
{
    private Guid? volumeDragPointId;
    private ControlPointsContext? volumeContext;
    private long volumeRevision;
    private int originalVolume;
    private double startPointerY, lastPointerY, volumeHeight;
    private OHsControlPointZone? volumeInvoker;
    internal bool IsVolumePreviewActive { get; private set; }
    internal int? PreviewVolume { get; private set; }

    internal void BeginVolumeDrag(Guid id, double pointerY, double usableHeight)
    {
        CancelVolumeDrag(); CancelPointDrag(); suppressTagClick = false;
        if (!IsMenuOwnerActive || State is not { CanEdit: true } state || !double.IsFinite(pointerY) ||
            !double.IsFinite(usableHeight) || usableHeight <= 0 || state.Points.FirstOrDefault(p => p.Id == id) is not { } point) return;
        volumeDragPointId = id; volumeContext = state.Context; volumeRevision = state.Revision; originalVolume = point.Volume;
        startPointerY = lastPointerY = pointerY; volumeHeight = usableHeight; volumeInvoker = FindRealizedTag(id);
    }

    internal void UpdateVolumeDrag(double pointerY)
    {
        if (volumeDragPointId is not { } id) return;
        if (State is not { CanEdit: true } state || state.Context != volumeContext || state.Revision != volumeRevision ||
            !state.Points.Any(p => p.Id == id) || !double.IsFinite(pointerY)) { CancelVolumeDrag(); return; }
        lastPointerY = pointerY;
        if (!IsVolumePreviewActive)
        {
            if (Math.Abs(pointerY - startPointerY) < SystemParameters.MinimumVerticalDragDistance) return;
            DismissEditor(false);
            if (volumeDragPointId != id) return;
            if (!(CapturePointer?.Invoke() ?? CaptureMouse())) { CancelVolumeDrag(); return; }
            IsVolumePreviewActive = true; suppressTagClick = true;
        }
        PreviewVolume = OHsControlPointLayout.GetDraggedVolume(originalVolume, startPointerY, pointerY, volumeHeight);
        if (volumeInvoker is { } zone) { zone.PreviewVolume = PreviewVolume; zone.IsVolumeDragging = true; }
        if (panel is not null) { panel.PreviewVolumePointId = id; panel.PreviewVolume = PreviewVolume; panel.InvalidateGeometry(); }
    }

    internal bool CompleteVolumeDrag()
    {
        if (!IsVolumePreviewActive || volumeDragPointId is not { } id) { CancelVolumeDrag(); return false; }
        UpdateVolumeDrag(lastPointerY);
        var request = IsVolumePreviewActive && PreviewVolume is { } next && next != originalVolume && volumeContext is { } context
            ? new SetControlPointRequestedMessage(context, id, next, null, volumeRevision) : null;
        // Clear capture before the synchronous command publishes replacement snapshots.
        CancelVolumeDrag();
        if (request is null || SetPointCommand?.CanExecute(request) != true) return false;
        SetPointCommand.Execute(request);
        return true;
    }

    internal void CancelVolumeDrag()
    {
        var wasActive = IsVolumePreviewActive;
        volumeDragPointId = null; volumeContext = null; IsVolumePreviewActive = false; PreviewVolume = null;
        if (volumeInvoker is { } zone) { zone.PreviewVolume = null; zone.IsVolumeDragging = false; }
        volumeInvoker = null;
        if (panel is not null) { panel.PreviewVolumePointId = null; panel.PreviewVolume = null; panel.InvalidateGeometry(); }
        if (wasActive) { if (ReleasePointer is not null) ReleasePointer(); else if (IsMouseCaptured) ReleaseMouseCapture(); }
    }

    private void RefreshVolumeInteraction()
    {
        if (volumeDragPointId is { } id && (State is not { CanEdit: true } state || state.Context != volumeContext ||
            state.Revision != volumeRevision || !state.Points.Any(p => p.Id == id))) CancelVolumeDrag();
    }

    private bool EditVolumeFromKeyboard(OHsControlPointZone zone, int direction)
    {
        if (!IsMenuOwnerActive || State is not { CanEdit: true } state || zone.Point is not { } point) return false;
        var next = Math.Clamp(point.Volume + direction, Core.Entities.ControlPoint.MinimumEditableVolume, Core.Entities.ControlPoint.MaximumVolume);
        var request = new SetControlPointRequestedMessage(state.Context, point.Id, next, null, state.Revision);
        if (next != point.Volume && SetPointCommand?.CanExecute(request) == true) SetPointCommand.Execute(request);
        return true;
    }
}
