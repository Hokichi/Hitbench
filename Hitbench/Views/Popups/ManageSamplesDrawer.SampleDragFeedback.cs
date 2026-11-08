using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Hitbench.Resources.CustomControls;
using Hitbench.Resources.DataModels;
namespace Hitbench.Views.Popups;

public partial class ManageSamplesDrawer
{
    private bool sampleDragNativeInProgress, sampleDragCanceled;
    private SampleManagementEntry? activeSampleEntry;
    private OHsSampleTrackDragData? activeSamplePayload;
    private FrameworkElement? sampleFeedbackOwner;
    private AdornerLayer? sampleFeedbackLayer;
    private OHsSampleDragAdorner? sampleFeedback;
    private readonly HashSet<OHsSampleRow> markedSampleRows = [];
    private Func<FrameworkElement, Point?> samplePointer = ReadSampleDragPointer;
    internal void SetSampleDragPointerProvider(Func<FrameworkElement, Point?> provider) => samplePointer = provider;
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out System.Drawing.Point point);
    private static Point? ReadSampleDragPointer(FrameworkElement owner)
    {
        if (PresentationSource.FromVisual(owner) is null || !GetCursorPos(out var point)) return null;
        try { return owner.PointFromScreen(new Point(point.X,point.Y)); }
        catch (InvalidOperationException) { return null; }
    }
    private void BeginSampleDragFeedback(SampleManagementEntry entry, OHsSampleTrackDragData payload)
    {
        activeSampleEntry = entry; activeSamplePayload = payload;
        entry.PropertyChanged += OnSampleEntryChanged;
        viewModel.CreateTrackCommand.CanExecuteChanged += OnSampleEligibilityChanged;
        viewModel.Entries.CollectionChanged += OnSampleEntriesChanged;
        viewModel.MapsetEntries.CollectionChanged += OnSampleEntriesChanged;
        AddHandler(DragDrop.GiveFeedbackEvent, new GiveFeedbackEventHandler(OnSampleGiveFeedback), true);
        AddHandler(DragDrop.QueryContinueDragEvent, new QueryContinueDragEventHandler(OnSampleQueryContinue), true);
        for (DependencyObject? ancestor = this; ancestor is not null; ancestor = VisualTreeHelper.GetParent(ancestor))
            if (ancestor is AdornerDecorator { Child: FrameworkElement owner } decorator)
            {
                sampleFeedbackOwner = owner; sampleFeedbackLayer = decorator.AdornerLayer; break;
            }
        if (sampleFeedbackOwner is null) return;
        sampleFeedback = new OHsSampleDragAdorner(sampleFeedbackOwner, (DataTemplate)FindResource("OHsManageSamplesDragTagTemplate"),entry.DisplayFileName,(double)FindResource("Space.2"));
        sampleFeedback.SetBinding(FlowDirectionProperty,new Binding(nameof(FlowDirection)){Source=this,Mode=BindingMode.OneWay});
        sampleFeedback.SetBinding(LanguageProperty,new Binding(nameof(Language)){Source=this,Mode=BindingMode.OneWay});
        sampleFeedbackLayer!.Add(sampleFeedback);
        sampleFeedbackOwner.AddHandler(DragDrop.PreviewDragOverEvent,new DragEventHandler(OnSampleFeedbackDrag),true);
        sampleFeedbackOwner.AddHandler(DragDrop.PreviewDragEnterEvent,new DragEventHandler(OnSampleFeedbackDrag),true);
        sampleFeedbackOwner.AddHandler(DragDrop.PreviewDragLeaveEvent,new DragEventHandler(OnSampleFeedbackDrag),true);
        sampleFeedbackOwner.LayoutUpdated += OnSampleFeedbackLayout;
        foreach (var row in Descendants(SampleRows).Concat(Descendants(MapsetSampleRows)).OfType<OHsSampleRow>()) RefreshSampleSource(row);
        UpdateSampleDragFeedback();
    }
    private void RefreshSampleSource(OHsSampleRow row)
    {
        // UserControl root supplies a local default; apply this owner's named variation after realization.
        row.SetCurrentValue(StyleProperty, (Style)FindResource("OHsManageSamplesDragSourceStyle"));
        if (sampleFeedback is not null && ReferenceEquals(row.DataContext,activeSampleEntry)) { row.IsDragSource=true; markedSampleRows.Add(row); }
        else { row.ClearValue(OHsSampleRow.IsDragSourceProperty); markedSampleRows.Remove(row); }
    }
    private void OnSampleDragRowLoaded(object sender,RoutedEventArgs e) => RefreshSampleSource((OHsSampleRow)sender);
    private void OnSampleDragRowChanged(object sender,DependencyPropertyChangedEventArgs e) => RefreshSampleSource((OHsSampleRow)sender);
    private void OnSampleDragRowUnloaded(object sender,RoutedEventArgs e) { var row=(OHsSampleRow)sender; row.ClearValue(OHsSampleRow.IsDragSourceProperty); markedSampleRows.Remove(row); }
    private void OnSampleEntryChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs e) => CheckSampleEligibility();
    private void OnSampleEligibilityChanged(object? sender,EventArgs e) => CheckSampleEligibility();
    private void OnSampleEntriesChanged(object? sender,NotifyCollectionChangedEventArgs e) => CheckSampleEligibility();
    private void CheckSampleEligibility()
    {
        if (activeSampleEntry is {} entry && viewModel.GetTrackDragData(entry) != activeSamplePayload) EndSampleDragFeedback();
    }
    private void OnSampleGiveFeedback(object sender,GiveFeedbackEventArgs e) => UpdateSampleDragFeedback();
    private void OnSampleQueryContinue(object sender,QueryContinueDragEventArgs e)
    {
        var action = GetSampleDragContinuation(e.Action);
        if (action == DragAction.Cancel) { e.Action=action; e.Handled=true; }
    }
    internal DragAction GetSampleDragContinuation(DragAction action)
    { CheckSampleEligibility(); UpdateSampleDragFeedback(); return sampleDragCanceled ? DragAction.Cancel : action; }
    private void OnSampleFeedbackDrag(object sender,DragEventArgs e) => UpdateSampleDragFeedback();
    private void OnSampleFeedbackLayout(object? sender,EventArgs e) => UpdateSampleDragFeedback();
    private void UpdateSampleDragFeedback()
    {
        if (sampleFeedback is null || sampleFeedbackOwner is null) return;
        if (!sampleFeedbackOwner.IsAncestorOf(this)) { EndSampleDragFeedback(); return; }
        var point=samplePointer(sampleFeedbackOwner);
        sampleFeedback.Update(point,new Rect(sampleFeedbackOwner.RenderSize));
    }
    private void EndSampleDragFeedback()
    {
        sampleDragCanceled=true;
        viewModel.CreateTrackCommand.CanExecuteChanged -= OnSampleEligibilityChanged;
        viewModel.Entries.CollectionChanged -= OnSampleEntriesChanged;
        viewModel.MapsetEntries.CollectionChanged -= OnSampleEntriesChanged;
        RemoveHandler(DragDrop.GiveFeedbackEvent,new GiveFeedbackEventHandler(OnSampleGiveFeedback));
        if (!sampleDragNativeInProgress) RemoveHandler(DragDrop.QueryContinueDragEvent,new QueryContinueDragEventHandler(OnSampleQueryContinue));
        if (sampleFeedbackOwner is {} owner)
        {
            owner.RemoveHandler(DragDrop.PreviewDragOverEvent,new DragEventHandler(OnSampleFeedbackDrag));
            owner.RemoveHandler(DragDrop.PreviewDragEnterEvent,new DragEventHandler(OnSampleFeedbackDrag));
            owner.RemoveHandler(DragDrop.PreviewDragLeaveEvent,new DragEventHandler(OnSampleFeedbackDrag));
            owner.LayoutUpdated -= OnSampleFeedbackLayout;
        }
        if(sampleFeedback is not null) sampleFeedbackLayer?.Remove(sampleFeedback);
        foreach(var row in markedSampleRows) row.ClearValue(OHsSampleRow.IsDragSourceProperty);
        if (activeSampleEntry is {} entry) entry.PropertyChanged -= OnSampleEntryChanged;
        markedSampleRows.Clear(); activeSampleEntry=null; activeSamplePayload=null; sampleFeedback=null; sampleFeedbackOwner=null; sampleFeedbackLayer=null;
    }
}
