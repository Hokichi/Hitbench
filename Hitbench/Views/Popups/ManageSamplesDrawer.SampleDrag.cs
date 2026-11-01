using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Hitbench.Resources.CustomControls;
using Hitbench.Resources.DataModels;
namespace Hitbench.Views.Popups;

public partial class ManageSamplesDrawer
{
    private Point? sampleDragOrigin;
    private SampleManagementEntry? sampleDragEntry;
    private Func<DependencyObject, IDataObject, DragDropEffects, DragDropEffects> startSampleDrag = (source, data, effects) => DragDrop.DoDragDrop(source, data, effects);
    internal void SetSampleDragHandler(Func<DependencyObject, IDataObject, DragDropEffects, DragDropEffects> handler) => startSampleDrag = handler;
    private void InitializeSampleDrag() { PreviewMouseLeftButtonDown += OnSamplePointerDown; PreviewMouseMove += OnSamplePointerMove; PreviewMouseLeftButtonUp += (_, _) => ClearSampleDrag(); Unloaded += (_, _) => { ClearSampleDrag(); EndSampleDragFeedback(); }; }
    private void ClearSampleDrag() { sampleDragOrigin = null; sampleDragEntry = null; }
    private void OnSamplePointerDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject origin) BeginSampleDragPointer(origin, e.GetPosition(this));
    }
    internal bool BeginSampleDragPointer(DependencyObject origin, Point point)
    {
        if (sampleDragNativeInProgress) return false;
        ClearSampleDrag();
        for (var current = origin; current is not null; current = current is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
        {
            if (current is ButtonBase or TextBoxBase or PasswordBox or Selector or RangeBase or BalloonControl or MenuItem) return false;
            if (current is OHsSampleRow { DataContext: SampleManagementEntry entry })
            {
                if (viewModel.GetTrackDragData(entry) is null) return false;
                sampleDragOrigin = point; sampleDragEntry = entry; return true;
            }
            if (ReferenceEquals(current, this)) return false;
        }
        return false;
    }
    private void OnSamplePointerMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) { ClearSampleDrag(); return; }
        if (sampleDragOrigin is not { } start || sampleDragEntry is not { } entry) return;
        var point = e.GetPosition(this); if (TryStartSampleDrag(entry, point - start)) { ClearSampleDrag(); e.Handled = true; }
    }
    internal bool TryStartSampleDrag(SampleManagementEntry entry, Vector delta)
    {
        if (sampleDragNativeInProgress) return false;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return false;
        ClearSampleDrag(); if (viewModel.GetTrackDragData(entry) is not { } payload) return false;
        sampleDragNativeInProgress = true; sampleDragCanceled = false;
        try
        {
            BeginSampleDragFeedback(entry, payload);
            var data = new DataObject(typeof(OHsSampleTrackDragData), payload);
            startSampleDrag(this, data, DragDropEffects.Copy); return true;
        }
        finally { sampleDragNativeInProgress = false; EndSampleDragFeedback(); ClearSampleDrag(); }
    }
}
