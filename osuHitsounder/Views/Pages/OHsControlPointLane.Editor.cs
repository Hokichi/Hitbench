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

    internal bool OpenEditor(Guid id)
    {
        if (PresentEditor is null && PresentationSource.FromVisual(this) is not HwndSource) return false;
        if (State is not { CanEdit: true } state || state.Points.FirstOrDefault(p => p.Id == id) is not { } point ||
            BringPointIntoView(id) is not { } tag) return false;
        if (IsEditorOpen) DismissEditor(false);
        editingPointId = id; editorInvoker = tag; tag.IsEditing = true;
        ValueEditor.FlowDirection = FlowDirection;
        ValueEditor.SetPointCommand = SetPointCommand; ValueEditor.Context = state.Context; ValueEditor.Point = point;
        IsEditorOpen = true;
        if (PresentEditor is not null) PresentEditor(true, tag);
        else { EditorPopup.PlacementTarget = tag; EditorPopup.IsOpen = true; }
        return true;
    }

    internal void DismissEditor(bool restoreFocus = true)
    {
        if (!IsEditorOpen) return;
        IsEditorOpen = false;
        var invoker = editorInvoker;
        if (invoker is not null) invoker.IsEditing = false;
        editorInvoker = null; editingPointId = null; ValueEditor.Clear();
        if (PresentEditor is not null) PresentEditor(false, invoker);
        else
        {
            EditorPopup.IsOpen = false; EditorPopup.PlacementTarget = null;
            if (restoreFocus) { if (invoker is { IsEnabled: true, IsVisible: true }) invoker.Focus(); else MoveFocus(new(FocusNavigationDirection.Previous)); }
        }
    }

    private void RefreshEditor(ControlPointsSnapshot? previous)
    {
        if (!IsEditorOpen) return;
        var point = State?.Points.FirstOrDefault(p => p.Id == editingPointId);
        if (State?.CanEdit != true || State.Context != previous?.Context || point is null) { DismissEditor(false); return; }
        ValueEditor.Point = point;
    }
    private void OnEditorOpened(object sender, EventArgs e) => ValueEditor.FocusInitialInput();
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
