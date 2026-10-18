using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Resources.Helpers;
using osuHitsounder.Resources.Messages;
using osuHitsounder.Services;

namespace osuHitsounder.Views.Pages;

public partial class OHsControlPointLane
{
    private static DependencyProperty RegisterCommand(string name) => DependencyProperty.Register(name, typeof(ICommand), typeof(OHsControlPointLane));
    public static readonly DependencyProperty AddCommandProperty = RegisterCommand(nameof(AddCommand));
    public static readonly DependencyProperty CommitAddCommandProperty = RegisterCommand(nameof(CommitAddCommand));
    public static readonly DependencyProperty MovePointCommandProperty = RegisterCommand(nameof(MovePointCommand));
    public static readonly DependencyProperty DeletePointCommandProperty = RegisterCommand(nameof(DeletePointCommand));
    public static readonly DependencyProperty RetrySaveCommandProperty = RegisterCommand(nameof(RetrySaveCommand));
    public static readonly DependencyProperty SetPointDisabledCommandProperty = RegisterCommand(nameof(SetPointDisabledCommand));
    public static readonly DependencyProperty SplitPointCommandProperty = RegisterCommand(nameof(SplitPointCommand));
    private static readonly DependencyPropertyKey MenuDisableRequestPropertyKey = DependencyProperty.RegisterReadOnly(nameof(MenuDisableRequest), typeof(SetControlPointDisabledRequestedMessage), typeof(OHsControlPointLane), new PropertyMetadata(null));
    public static readonly DependencyProperty MenuDisableRequestProperty = MenuDisableRequestPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey MenuSplitRequestPropertyKey = DependencyProperty.RegisterReadOnly(nameof(MenuSplitRequest), typeof(SplitControlPointRequestedMessage), typeof(OHsControlPointLane), new PropertyMetadata(null));
    public static readonly DependencyProperty MenuSplitRequestProperty = MenuSplitRequestPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey DisableMenuHeaderPropertyKey = DependencyProperty.RegisterReadOnly(nameof(DisableMenuHeader), typeof(string), typeof(OHsControlPointLane), new PropertyMetadata("Disable"));
    public static readonly DependencyProperty DisableMenuHeaderProperty = DisableMenuHeaderPropertyKey.DependencyProperty;
    public static readonly DependencyProperty PendingAddProperty = DependencyProperty.Register(nameof(PendingAdd), typeof(AddControlPointRequestedMessage), typeof(OHsControlPointLane), new PropertyMetadata(null, (d, e) => ((OHsControlPointLane)d).AcceptPendingAdd(e.NewValue as AddControlPointRequestedMessage)));
    public static readonly DependencyProperty OffsetsProperty = DependencyProperty.Register(nameof(Offsets), typeof(IReadOnlyList<int>), typeof(OHsControlPointLane), new PropertyMetadata(Array.Empty<int>()));
    public static readonly DependencyProperty BpmsProperty = DependencyProperty.Register(nameof(Bpms), typeof(IReadOnlyList<int>), typeof(OHsControlPointLane), new PropertyMetadata(Array.Empty<int>()));
    public static readonly DependencyProperty SubdivisionProperty = DependencyProperty.Register(nameof(Subdivision), typeof(int), typeof(OHsControlPointLane), new PropertyMetadata(4));
    public static readonly DependencyProperty OwnerErrorProperty = DependencyProperty.Register(nameof(OwnerError), typeof(string), typeof(OHsControlPointLane));
    private static readonly DependencyPropertyKey RejectionReasonPropertyKey = DependencyProperty.RegisterReadOnly(nameof(RejectionReason), typeof(string), typeof(OHsControlPointLane), new PropertyMetadata(null));
    public static readonly DependencyProperty RejectionReasonProperty = RejectionReasonPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey SaveStatusPropertyKey = DependencyProperty.RegisterReadOnly(nameof(SaveStatus), typeof(string), typeof(OHsControlPointLane), new PropertyMetadata(""));
    public static readonly DependencyProperty SaveStatusProperty = SaveStatusPropertyKey.DependencyProperty;
    public ICommand? AddCommand { get => (ICommand?)GetValue(AddCommandProperty); set => SetValue(AddCommandProperty, value); }
    public ICommand? CommitAddCommand { get => (ICommand?)GetValue(CommitAddCommandProperty); set => SetValue(CommitAddCommandProperty, value); }
    public ICommand? MovePointCommand { get => (ICommand?)GetValue(MovePointCommandProperty); set => SetValue(MovePointCommandProperty, value); }
    public ICommand? DeletePointCommand { get => (ICommand?)GetValue(DeletePointCommandProperty); set => SetValue(DeletePointCommandProperty, value); }
    public ICommand? RetrySaveCommand { get => (ICommand?)GetValue(RetrySaveCommandProperty); set => SetValue(RetrySaveCommandProperty, value); }
    public ICommand? SetPointDisabledCommand { get => (ICommand?)GetValue(SetPointDisabledCommandProperty); set => SetValue(SetPointDisabledCommandProperty, value); }
    public ICommand? SplitPointCommand { get => (ICommand?)GetValue(SplitPointCommandProperty); set => SetValue(SplitPointCommandProperty, value); }
    public SetControlPointDisabledRequestedMessage? MenuDisableRequest => (SetControlPointDisabledRequestedMessage?)GetValue(MenuDisableRequestProperty);
    public SplitControlPointRequestedMessage? MenuSplitRequest => (SplitControlPointRequestedMessage?)GetValue(MenuSplitRequestProperty);
    public string DisableMenuHeader => (string)GetValue(DisableMenuHeaderProperty);
    public AddControlPointRequestedMessage? PendingAdd { get => (AddControlPointRequestedMessage?)GetValue(PendingAddProperty); set => SetValue(PendingAddProperty, value); }
    public IReadOnlyList<int> Offsets { get => (IReadOnlyList<int>)GetValue(OffsetsProperty); set => SetValue(OffsetsProperty, value); }
    public IReadOnlyList<int> Bpms { get => (IReadOnlyList<int>)GetValue(BpmsProperty); set => SetValue(BpmsProperty, value); }
    public int Subdivision { get => (int)GetValue(SubdivisionProperty); set => SetValue(SubdivisionProperty, value); }
    public string? OwnerError { get => (string?)GetValue(OwnerErrorProperty); set => SetValue(OwnerErrorProperty, value); }
    public string? RejectionReason => (string?)GetValue(RejectionReasonProperty);
    public string SaveStatus => (string)GetValue(SaveStatusProperty);
    internal Func<bool>? CapturePointer { get; set; }
    internal Action? ReleasePointer { get; set; }
    internal Action<ContextMenu, FrameworkElement>? PresentMenu { get; set; }
    internal ContextMenu BackgroundMenu => (ContextMenu)Resources["OHsControlPointBackgroundMenu"];
    internal ContextMenu ZoneMenu => (ContextMenu)Resources["OHsControlPointZoneMenu"];
    internal bool IsDragPreviewActive { get; private set; }
    internal bool IsDragCandidateValid { get; private set; }
    internal int? PreviewTimestamp { get; private set; }
    private Guid? dragPointId;
    private ControlPointsContext? dragContext;
    private int originalTimestamp;
    private long dragRevision;
    private double startPointerX, lastPointerX, grabOffset;
    private bool suppressTagClick;
    private Guid? handledAddId;
    private Guid? pendingEditorId;
    private Guid? menuPointId;
    private ControlPointsContext? menuContext;
    private bool dismissingMenus;

    internal void RequestAddAtMarker()
    {
        if (IsMenuOwnerActive && State?.CanAdd == true && AddCommand?.CanExecute(null) == true) AddCommand.Execute(null);
    }
    private void AcceptPendingAdd(AddControlPointRequestedMessage? request)
    {
        if (request is null || handledAddId == request.PointId) return;
        handledAddId = request.PointId;
        if (State is not { CanEdit: true } state || state.Context != request.Context || request.ExpectedRevision != state.Revision) return;
        if (ViewportWidth <= 0 || ContentWidth <= 0 ||
            !OHsControlPointLayout.CanPlace(Guid.Empty, request.Timestamp, DurationMilliseconds, state.Points))
        { SetRejection("Cannot add control point at this timestamp."); return; }
        SetRejection(null);
        pendingEditorId = request.PointId;
        if (CommitAddCommand?.CanExecute(request) == true) CommitAddCommand.Execute(request);
        if (State?.Points.Any(p => p.Id == request.PointId) == true) { OpenEditor(request.PointId); pendingEditorId = null; }
        else pendingEditorId = null;
    }
    internal void DeletePoint(Guid id)
    {
        if (State is not { CanEdit: true } state) return;
        var request = new DeleteControlPointRequestedMessage(state.Context, id);
        if (DeletePointCommand?.CanExecute(request) == true) DeletePointCommand.Execute(request);
    }
    private void OnRetrySaveClick(object sender, RoutedEventArgs e)
    {
        if (State is not { CanEdit: true } state) return;
        var request = new RetryControlPointsSaveRequestedMessage(state.Context);
        if (RetrySaveCommand?.CanExecute(request) == true) RetrySaveCommand.Execute(request);
    }
    private void OnAddPointClick(object sender, RoutedEventArgs e) => RequestAddAtMarker();
    private void OnDeletePointClick(object sender, RoutedEventArgs e)
    {
        if (menuContext == State?.Context && menuPointId is { } id) DeletePoint(id);
    }
    private void UpdateSaveStatus() => SetValue(SaveStatusPropertyKey, State is null ? "" : State.IsSaving ? "Saving…" : "Autosaved");
    private void SetRejection(string? text)
    {
        SetValue(RejectionReasonPropertyKey, text);
        if (dragPointId is { } id && FindRealizedTag(id) is { } tag)
        {
            tag.IsInvalid = text is not null;
            AutomationProperties.SetHelpText(tag, text ?? "Click to edit. Drag horizontally to move. Use the context menu to delete.");
        }
    }
    private void RefreshInteraction(ControlPointsSnapshot? previous)
    {
        RefreshVolumeInteraction();
        if (State?.Context != previous?.Context || State?.CanEdit != true) { CancelPointDrag(); DismissMenus(); handledAddId = null; }
        else if (dragPointId is { } id && (!State.Points.Any(p => p.Id == id) || State.Revision != dragRevision)) CancelPointDrag();
        if (pendingEditorId is { } added && State?.Points.Any(p => p.Id == added) == true) { pendingEditorId = null; OpenEditor(added); }
    }

    internal void BeginPointDrag(Guid id, double pointerX)
    {
        CancelVolumeDrag();
        CancelPointDrag(); suppressTagClick = false;
        if (State is not { CanEdit: true } state || !IsMenuOwnerActive || state.Points.FirstOrDefault(p => p.Id == id) is not { } point) return;
        dragPointId = id; dragContext = state.Context; originalTimestamp = point.Timestamp; dragRevision = state.Revision;
        startPointerX = lastPointerX = pointerX;
        grabOffset = pointerX + HorizontalOffset - OHsControlPointLayout.GetLeft(point.Timestamp, DurationMilliseconds, ContentWidth);
    }
    internal void UpdatePointDrag(double pointerX)
    {
        if (dragPointId is not { } id || State is not { CanEdit: true } state || state.Context != dragContext) { CancelPointDrag(); return; }
        lastPointerX = pointerX;
        if (!IsDragPreviewActive)
        {
            if (Math.Abs(pointerX - startPointerX) < SystemParameters.MinimumHorizontalDragDistance) return;
            if (!(CapturePointer?.Invoke() ?? CaptureMouse())) { CancelPointDrag(); return; }
            DismissEditor(false); IsDragPreviewActive = true; suppressTagClick = true;
        }
        var point = state.Points.FirstOrDefault(p => p.Id == id);
        if (point is null || point.Timestamp != originalTimestamp || ContentWidth <= 0) { CancelPointDrag(); return; }
        PreviewTimestamp = BeatGridCalculator.GetNearestTimestamp(Offsets, Bpms, Subdivision,
            (pointerX + HorizontalOffset - grabOffset) / ContentWidth * DurationMilliseconds, DurationMilliseconds);
        var extents = GetExtents();
        var left = PreviewTimestamp is { } timestamp ? OHsControlPointLayout.GetLeft(timestamp, DurationMilliseconds, ContentWidth) : double.NaN;
        IsDragCandidateValid = ViewportWidth > 0 && extents.Count == state.Points.Count && PreviewTimestamp is { } target &&
            state.Revision == dragRevision && OHsControlPointLayout.CanPlace(id, target, DurationMilliseconds, state.Points);
        if (panel is not null)
        {
            panel.PreviewPointId = id; panel.PreviewLeft = double.IsFinite(left) ? left : OHsControlPointLayout.GetLeft(originalTimestamp, DurationMilliseconds, ContentWidth);
            panel.InvalidateMeasure();
        }
        SetRejection(IsDragCandidateValid ? null : "Cannot move control point: timestamp is occupied or timing is unavailable.");
    }
    internal bool CompletePointDrag()
    {
        if (!IsDragPreviewActive || dragPointId is not { } id) { CancelPointDrag(); return false; }
        UpdatePointDrag(lastPointerX);
        var accepted = false;
        if (IsDragCandidateValid && PreviewTimestamp is { } target && State is { } state && state.Context == dragContext)
        {
            var request = new MoveControlPointRequestedMessage(state.Context, id, dragRevision, target);
            if (MovePointCommand?.CanExecute(request) == true) MovePointCommand.Execute(request);
            accepted = State?.Points.Any(p => p.Id == id && p.Timestamp == target) == true;
        }
        CancelPointDrag(false);
        return accepted;
    }
    internal void CancelPointDrag() => CancelPointDrag(true);
    private void CancelPointDrag(bool clearRejection)
    {
        var wasActive = IsDragPreviewActive;
        if (dragPointId is { } id && FindRealizedTag(id) is { } tag) tag.IsInvalid = false;
        dragPointId = null; dragContext = null; IsDragPreviewActive = false; IsDragCandidateValid = false; PreviewTimestamp = null;
        if (panel is not null) { panel.PreviewPointId = null; panel.InvalidateMeasure(); }
        if (wasActive) { if (ReleasePointer is not null) ReleasePointer(); else if (IsMouseCaptured) ReleaseMouseCapture(); }
        if (clearRejection) SetRejection(null);
    }
    public void ResetInteraction() { CancelVolumeDrag(); CancelPointDrag(); DismissEditor(false); DismissMenus(); pendingEditorId = null; if (panel is not null) { panel.PinnedPointId = null; panel.InvalidateGeometry(); } }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (FindZone(e.OriginalSource as DependencyObject) is { Point: { } point } zone)
        {
            if (Math.Abs(e.GetPosition(zone).Y - zone.VolumeLineOffset) <= 6)
            {
                zone.Focus(); BeginVolumeDrag(point.Id, e.GetPosition(PointItems).Y, zone.ActualHeight); e.Handled = true;
            }
            else BeginPointDrag(point.Id, e.GetPosition(PointItems).X);
        }
        base.OnPreviewMouseLeftButtonDown(e);
    }
    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        if (volumeDragPointId is not null && e.LeftButton == MouseButtonState.Pressed)
        { UpdateVolumeDrag(e.GetPosition(PointItems).Y); if (IsVolumePreviewActive) e.Handled = true; }
        else if (dragPointId is not null && e.LeftButton == MouseButtonState.Pressed) { UpdatePointDrag(e.GetPosition(PointItems).X); if (IsDragPreviewActive) e.Handled = true; }
        base.OnPreviewMouseMove(e);
    }
    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (volumeDragPointId is { } volumeId)
        {
            if (IsVolumePreviewActive) CompleteVolumeDrag();
            else { CancelVolumeDrag(); OpenEditor(volumeId); }
            e.Handled = true;
        }
        else if (IsDragPreviewActive) { CompletePointDrag(); e.Handled = true; }
        else CancelPointDrag();
        base.OnPreviewMouseLeftButtonUp(e);
    }
    protected override void OnLostMouseCapture(MouseEventArgs e)
    { if (e.OriginalSource == this) { if (IsDragPreviewActive) CancelPointDrag(); if (IsVolumePreviewActive) CancelVolumeDrag(); } base.OnLostMouseCapture(e); }

    internal bool HandlePointKey(Key key, ModifierKeys modifiers, DependencyObject? origin)
    {
        if (key == Key.Escape && (dragPointId is not null || volumeDragPointId is not null)) { CancelPointDrag(); CancelVolumeDrag(); return true; }
        var tag = FindZone(origin);
        if (tag is not null && modifiers == ModifierKeys.None && key is Key.Up or Key.Down) return EditVolumeFromKeyboard(tag, key == Key.Up ? 1 : -1);
        if (tag?.Point is { } point && modifiers == ModifierKeys.Alt && key is Key.Left or Key.Right)
        {
            if (IsMenuOwnerActive && double.IsFinite(DurationMilliseconds) && DurationMilliseconds > 0 && State is { CanEdit: true } state && BeatGridCalculator.GetAdjacentPosition(Offsets, Bpms, Subdivision,
                TimeSpan.FromMilliseconds(point.Timestamp), TimeSpan.FromMilliseconds(DurationMilliseconds), key == Key.Right) is { } target)
            {
                var timestamp = Math.Round(target.TotalMilliseconds, MidpointRounding.AwayFromZero);
                if (timestamp <= int.MaxValue && OHsControlPointLayout.CanPlace(point.Id, (int)timestamp, DurationMilliseconds, state.Points))
                {
                    var request = new MoveControlPointRequestedMessage(state.Context, point.Id, state.Revision, (int)timestamp);
                    if (MovePointCommand?.CanExecute(request) == true) MovePointCommand.Execute(request);
                }
            }
            return true;
        }
        if (tag is not null && key is Key.Enter or Key.Space) suppressTagClick = false;
        if (key == Key.Apps || key == Key.F10 && modifiers == ModifierKeys.Shift) { OpenContextMenu(tag); return true; }
        return tag is not null && key is Key.Left or Key.Right;
    }
    protected override void OnContextMenuOpening(ContextMenuEventArgs e)
    { e.Handled = true; OpenContextMenu(FindZone(e.OriginalSource as DependencyObject)); base.OnContextMenuOpening(e); }
    protected override void OnPreviewMouseRightButtonDown(MouseButtonEventArgs e)
    { OpenContextMenu(FindZone(e.OriginalSource as DependencyObject)); e.Handled = true; base.OnPreviewMouseRightButtonDown(e); }
    private void OpenContextMenu(OHsControlPointZone? tag)
    {
        if (!IsMenuOwnerActive) return;
        CancelPointDrag(); CancelVolumeDrag();
        var menu = tag is null ? BackgroundMenu : ZoneMenu;
        menuPointId = tag?.Point?.Id; menuContext = State?.Context;
        if (tag?.Point is { } point && State is { } state)
        {
            ZoneMenu.DataContext = this;
            SetValue(MenuDisableRequestPropertyKey, new SetControlPointDisabledRequestedMessage(state.Context, point.Id, state.Revision, !point.IsDisabled));
            SetValue(MenuSplitRequestPropertyKey, new SplitControlPointRequestedMessage(state.Context, point.Id, state.Revision));
            SetValue(DisableMenuHeaderPropertyKey, point.IsDisabled ? "Enable" : "Disable");
        }
        var target = (FrameworkElement?)tag ?? this;
        menu.PlacementTarget = target;
        if (PresentMenu is not null) PresentMenu(menu, target);
        else if (PresentationSource.FromVisual(this) is HwndSource) menu.IsOpen = true;
    }
    private void DismissMenus()
    {
        dismissingMenus = true;
        try { BackgroundMenu.IsOpen = false; ZoneMenu.IsOpen = false; }
        finally { dismissingMenus = false; }
        BackgroundMenu.PlacementTarget = null; ZoneMenu.PlacementTarget = null;
        ClearMenuRequests();
    }
    private void ClearMenuRequests()
    {
        SetValue(MenuDisableRequestPropertyKey, null); SetValue(MenuSplitRequestPropertyKey, null);
        menuPointId = null; menuContext = null;
    }
    private void OnZoneMenuClosed(object sender, RoutedEventArgs e)
    {
        var id = menuPointId; var context = menuContext;
        ClearMenuRequests(); ZoneMenu.PlacementTarget = null;
        if (!dismissingMenus && State is { CanEdit: true } state && state.Context == context)
        {
            var target = id is { } current && state.Points.Any(p => p.Id == current) ? current : state.Points.FirstOrDefault()?.Id;
            if (target is { } restore) BringPointIntoView(restore)?.Focus(); else RestoreTimelineFocus();
        }
    }
    private static OHsControlPointZone? FindZone(DependencyObject? origin)
    {
        while (origin is not null)
        {
            if (origin is OHsControlPointZone tag) return tag;
            origin = origin is Visual ? VisualTreeHelper.GetParent(origin) : LogicalTreeHelper.GetParent(origin);
        }
        return null;
    }
}
