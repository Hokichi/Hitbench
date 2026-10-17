using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using osuHitsounder.Resources.DataModels;

namespace osuHitsounder.Views.Pages;

public partial class OHsControlPointLane
{
    public static readonly DependencyProperty SetPointCommandProperty = DependencyProperty.Register(nameof(SetPointCommand), typeof(ICommand), typeof(OHsControlPointLane));
    public ICommand? SetPointCommand { get => (ICommand?)GetValue(SetPointCommandProperty); set => SetValue(SetPointCommandProperty, value); }
    internal Action<bool, UIElement?>? PresentEditor { get; set; }
    internal bool IsEditorOpen { get; private set; }
    private Guid? editingPointId;
    private OHsControlPointTag? editorInvoker;
    private bool editorAwaitingPlacement;

    internal bool OpenEditor(Guid id)
    {
        if (PresentEditor is null && PresentationSource.FromVisual(this) is not HwndSource) return false;
        if (IsEditorOpen && editingPointId == id) return true;
        if (State is not { CanEdit: true } state || state.Points.FirstOrDefault(p => p.Id == id) is not { } point ||
            BringPointIntoView(id) is not { } tag) return false;
        if (IsEditorOpen) DismissEditor(false);
        editingPointId = id; editorInvoker = tag; tag.IsEditing = true;
        ValueEditor.FlowDirection = FlowDirection;
        ValueEditor.SetPointCommand = SetPointCommand; ValueEditor.Context = state.Context; ValueEditor.Point = point;
        IsEditorOpen = true; editorAwaitingPlacement = true;
        UpdateEditorViewport();
        return IsEditorOpen;
    }

    internal void DismissEditor(bool restoreFocus = true)
    {
        if (!IsEditorOpen) return;
        var pointId = editingPointId;
        IsEditorOpen = false;
        ValueEditor.CommitPendingInput();
        var wasPresented = !editorAwaitingPlacement;
        editorAwaitingPlacement = false;
        var invoker = editorInvoker;
        if (invoker is not null) invoker.IsEditing = false;
        if (restoreFocus && pointId is { } id && State?.Points.Any(p => p.Id == id) == true)
            invoker = BringPointIntoView(id) ?? invoker;
        editorInvoker = null; editingPointId = null; ValueEditor.Clear();
        if (PresentEditor is not null) { if (wasPresented) PresentEditor(false, invoker); }
        else
        {
            EditorPopup.IsOpen = false; EditorPopup.PlacementTarget = null;
            if (restoreFocus && invoker?.Focus() != true) RestoreTimelineFocus();
        }
    }

    private void RestoreTimelineFocus()
    {
        DependencyObject? current = this;
        while ((current = System.Windows.Media.VisualTreeHelper.GetParent(current)) is not null)
            if (current is OHsWorkstationTimeline timeline) { timeline.FocusTimeline(); return; }
        MoveFocus(new(FocusNavigationDirection.Previous));
    }

    private void RefreshEditor(ControlPointsSnapshot? previous)
    {
        if (!IsEditorOpen) return;
        var point = State?.Points.FirstOrDefault(p => p.Id == editingPointId);
        if (State?.CanEdit != true || State.Context != previous?.Context || point is null) { DismissEditor(false); return; }
        ValueEditor.Point = point;
    }
    private void OnEditorOpened(object sender, EventArgs e) => ValueEditor.FocusInitialInput();
    private void UpdateEditorViewport()
    {
        if (!IsEditorOpen || State?.Points.FirstOrDefault(p => p.Id == editingPointId) is not { } point) return;
        if (panel?.IsArrangeValid != true) return;
        if (FindRealizedTag(point.Id) is not { } currentTag) { DismissEditor(false); return; }
        if (editorInvoker != currentTag)
        {
            if (editorInvoker is not null) editorInvoker.IsEditing = false;
            editorInvoker = currentTag; currentTag.IsEditing = true;
        }
        var left = osuHitsounder.Resources.Helpers.OHsControlPointLayout.GetLeft(point.Timestamp, DurationMilliseconds, ContentWidth);
        var width = editorInvoker?.DesiredSize.Width ?? 0;
        if (!double.IsFinite(left) || ViewportWidth <= 0 || left < 0 || left > ContentWidth || editorInvoker?.Point?.Id != editingPointId)
        { DismissEditor(false); return; }
        if (left > HorizontalOffset + ViewportWidth || left + width < HorizontalOffset)
        { if (!editorAwaitingPlacement) DismissEditor(false); return; }
        if (PresentEditor is null && editorInvoker is { } tag)
        {
            var origin = tag.TranslatePoint(new Point(), this);
            EditorPopup.PlacementTarget = this;
            EditorPopup.PlacementRectangle = new Rect(origin, tag.RenderSize);
        }
        if (editorAwaitingPlacement)
        {
            editorAwaitingPlacement = false;
            if (PresentEditor is not null) PresentEditor(true, editorInvoker);
            else EditorPopup.IsOpen = true;
        }
    }
    private void OnEditorClosed(object sender, EventArgs e) => DismissEditor(false);
    private void OnEditorPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && ValueEditor.HandleEscape()) { e.Handled = true; DismissEditor(); }
    }
    private void OnTagClick(object sender, RoutedEventArgs e)
    {
        if (suppressTagClick) { suppressTagClick = false; e.Handled = true; return; }
        if (e.OriginalSource is OHsControlPointTag { Point: { } point }) { OpenEditor(point.Id); e.Handled = true; }
    }
}
