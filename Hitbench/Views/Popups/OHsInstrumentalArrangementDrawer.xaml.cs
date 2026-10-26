using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Hitbench.Resources.CustomControls;
using Hitbench.Resources.DataModels;
using Hitbench.ViewModels.Popups;

namespace Hitbench.Views.Popups;

public partial class OHsInstrumentalArrangementDrawer : UserControl, IOHsShellDrawer
{
    private readonly InstrumentalArrangementDrawerVM viewModel;
    private readonly DependencyPropertyDescriptor offsetDescriptor = DependencyPropertyDescriptor.FromProperty(PianoRollView.HorizontalOffsetProperty, typeof(PianoRollView));
    private bool attached, initialized, publishingOffset;
    private ModifierKeys wheelMode;
    private int wheelRemainder;
    private double previousLaneHeight;
    public OHsInstrumentalArrangementDrawer(InstrumentalArrangementDrawerVM vm)
    {
        viewModel = vm; InitializeComponent(); DataContext = vm; previousLaneHeight = vm.LaneHeight;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }
    public event EventHandler? CloseRequested;
    public event Action<double>? HorizontalOffsetRequested;
    public bool CanDismiss => true;
    public bool TryPrepareClose() { PianoRoll.CancelInteraction(); return true; }
    public void FocusInitialAction() => PianoRoll.Focus();
    public static readonly DependencyProperty TimelineContentWidthProperty = DependencyProperty.Register(nameof(TimelineContentWidth), typeof(double), typeof(OHsInstrumentalArrangementDrawer));
    public static readonly DependencyProperty TimelineViewportWidthProperty = DependencyProperty.Register(nameof(TimelineViewportWidth), typeof(double), typeof(OHsInstrumentalArrangementDrawer));
    public static readonly DependencyProperty TimelineHorizontalOffsetProperty = DependencyProperty.Register(nameof(TimelineHorizontalOffset), typeof(double), typeof(OHsInstrumentalArrangementDrawer));
    public double TimelineContentWidth { get => (double)GetValue(TimelineContentWidthProperty); set => SetValue(TimelineContentWidthProperty, value); }
    public double TimelineViewportWidth { get => (double)GetValue(TimelineViewportWidthProperty); set => SetValue(TimelineViewportWidthProperty, value); }
    public double TimelineHorizontalOffset { get => (double)GetValue(TimelineHorizontalOffsetProperty); set => SetValue(TimelineHorizontalOffsetProperty, value); }
    private static readonly DependencyPropertyKey PianoKeyColumnWidthPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(PianoKeyColumnWidth), typeof(double), typeof(OHsInstrumentalArrangementDrawer), new PropertyMetadata(0d));
    public static readonly DependencyProperty PianoKeyColumnWidthProperty = PianoKeyColumnWidthPropertyKey.DependencyProperty;
    public double PianoKeyColumnWidth => (double)GetValue(PianoKeyColumnWidthProperty);
    private static readonly DependencyPropertyKey PianoRollWidthPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(PianoRollWidth), typeof(double), typeof(OHsInstrumentalArrangementDrawer), new PropertyMetadata(0d));
    public static readonly DependencyProperty PianoRollWidthProperty = PianoRollWidthPropertyKey.DependencyProperty;
    public double PianoRollWidth => (double)GetValue(PianoRollWidthProperty);

    public void AlignTimelineViewport(Rect boundsInDrawer)
    {
        var keys = 0d;
        var width = 0d;
        if (!boundsInDrawer.IsEmpty && boundsInDrawer.Width > 0 && double.IsFinite(boundsInDrawer.X) &&
            double.IsFinite(boundsInDrawer.Y) && double.IsFinite(boundsInDrawer.Width) &&
            double.IsFinite(boundsInDrawer.Height) && PianoArea.IsArrangeValid)
        {
            var local = TransformToVisual(PianoArea).TransformBounds(boundsInDrawer);
            var available = Math.Max(0, PianoArea.ActualWidth - PitchScroll.ActualWidth);
            keys = Math.Clamp(local.Left, 0, available);
            width = keys + Math.Clamp(local.Width, 0, available - keys);
        }
        if (PianoKeyColumnWidth != keys) SetValue(PianoKeyColumnWidthPropertyKey, keys);
        if (PianoRollWidth != width) SetValue(PianoRollWidthPropertyKey, width);
    }
    internal void Attach()
    {
        if (attached) return; attached = true;
        offsetDescriptor.AddValueChanged(PianoRoll, OnPianoHorizontalOffsetChanged);
        viewModel.PropertyChanged += OnStateChanged;
        if (!initialized) { initialized = true; PianoRoll.VerticalOffset = 66 * viewModel.LaneHeight; }
    }
    internal void Detach()
    {
        if (!attached) return; attached = false;
        offsetDescriptor.RemoveValueChanged(PianoRoll, OnPianoHorizontalOffsetChanged);
        viewModel.PropertyChanged -= OnStateChanged; PianoRoll.CancelInteraction();
    }
    private void OnPianoHorizontalOffsetChanged(object? sender, EventArgs e)
    {
        if (publishingOffset || PianoRoll.HorizontalOffset == TimelineHorizontalOffset) return;
        RequestOffset(PianoRoll.HorizontalOffset);
        publishingOffset = true;
        try { PianoRoll.GetBindingExpression(PianoRollView.HorizontalOffsetProperty)?.UpdateTarget(); }
        finally { publishingOffset = false; }
    }
    private void RequestOffset(double value)
    {
        if (!double.IsFinite(value)) return;
        viewModel.RequestFollow(false);
        HorizontalOffsetRequested?.Invoke(Math.Clamp(value, 0, Math.Max(0, TimelineContentWidth - TimelineViewportWidth)));
    }
    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(viewModel.IsKeysMappingRequested) && viewModel.IsKeysMappingRequested)
        {
            PianoRoll.CancelInteraction();
            CloseRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (e.PropertyName != nameof(viewModel.LaneHeight)) return;
        ApplyLaneHeightChange(previousLaneHeight, viewModel.LaneHeight); previousLaneHeight = viewModel.LaneHeight;
    }
    internal void ApplyLaneHeightChange(double previous, double next)
    {
        var offset = PianoRoll.VerticalOffset / previous * next;
        PianoRoll.SetCurrentValue(PianoRollView.LaneHeightProperty, next);
        PianoRoll.SetCurrentValue(PianoRollView.VerticalOffsetProperty, Math.Clamp(offset, 0, PianoRoll.MaximumVerticalOffset));
    }
    private void OnCloseClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
    private void OnNoteSelection(object sender, InstrumentalNoteSelectionRequestedEventArgs e) { viewModel.SetNoteSelection(e.NoteIds, e.AnchorId); e.Handled = true; }
    private void OnNoteEdit(object sender, InstrumentalNoteEditRequestedEventArgs e)
    {
        switch (e.Kind)
        {
            case InstrumentalNoteEditKind.Place: viewModel.PlaceNote(e.Key, e.RequestedMilliseconds); break;
            case InstrumentalNoteEditKind.Move: viewModel.MoveNotes(e.NoteIds, e.NoteId, e.Key, e.RequestedMilliseconds); break;
            case InstrumentalNoteEditKind.Remove: viewModel.RemoveNotes(e.NoteIds); break;
        }
        e.Handled = true;
    }
    internal static bool IsTextInput(DependencyObject? origin)
    {
        for (var node = origin; node is not null; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (node is TextBoxBase or PasswordBox or ComboBox { IsEditable: true }) return true;
        return false;
    }
    internal bool HandleEditorShortcut(Key key, ModifierKeys modifiers, bool repeat, DependencyObject? origin)
    {
        if (IsTextInput(origin)) return false;
        ICommand? command = modifiers == ModifierKeys.Control ? key switch
        { Key.Z => viewModel.UndoCommand, Key.Y => viewModel.RedoCommand, Key.C => viewModel.CopyNotesCommand, Key.V => viewModel.PasteNotesCommand, Key.A => viewModel.SelectAllNotesCommand, _ => null }
            : modifiers == ModifierKeys.None && key == Key.Delete ? viewModel.DeleteSelectedNotesCommand : null;
        if (command is not null) { if (!repeat && command.CanExecute(null)) { PianoRoll.CancelInteraction(); command.Execute(null); } return true; }
        if (modifiers == ModifierKeys.None && key is Key.D1 or Key.NumPad1 or Key.D2 or Key.NumPad2)
        { if (!repeat && viewModel.CanEdit) viewModel.RequestEditingMode(key is Key.D2 or Key.NumPad2); return true; }
        if (modifiers == ModifierKeys.Control)
        {
            var index = key switch { Key.D1 or Key.NumPad1 => 0, Key.D2 or Key.NumPad2 => 1, Key.D3 or Key.NumPad3 => 2, Key.D4 or Key.NumPad4 => 3, Key.D5 or Key.NumPad5 => 4, Key.D6 or Key.NumPad6 => 5, Key.D8 or Key.NumPad8 => 6, _ => -1 };
            if (index >= 0) { if (!repeat) viewModel.RequestSubdivision(index); return true; }
        }
        if (modifiers == ModifierKeys.None && key is Key.Home or Key.End)
        { if (!repeat) RequestOffset(key == Key.Home ? 0 : TimelineContentWidth); return true; }
        return false;
    }
    internal bool HandleArrangementWheel(ModifierKeys modifiers, int delta, DependencyObject? origin)
    {
        if (!viewModel.CanEdit || modifiers is not (ModifierKeys.Control or ModifierKeys.Shift or ModifierKeys.Alt or (ModifierKeys.Control | ModifierKeys.Shift)))
        { wheelMode = ModifierKeys.None; wheelRemainder = 0; return false; }
        if (wheelMode != modifiers) { wheelMode = modifiers; wheelRemainder = 0; }
        wheelRemainder += delta; var steps = wheelRemainder / 120; wheelRemainder %= 120;
        if (steps == 0) return true;
        if (modifiers == ModifierKeys.Control) viewModel.RequestZoom(viewModel.ZoomFactor + steps);
        else if (modifiers == ModifierKeys.Alt) viewModel.RequestSubdivision(viewModel.SelectedSubdivisionIndex + steps);
        else if (modifiers == ModifierKeys.Shift) RequestOffset(TimelineHorizontalOffset - steps * 36);
        else viewModel.LaneHeight += steps * 6;
        return true;
    }
    internal bool HandleEscape()
    {
        if (PianoRoll.CancelInteraction()) return true;
        if (viewModel.SelectedNoteIds.Count > 0) { viewModel.SelectNote(null); return true; }
        CloseRequested?.Invoke(this, EventArgs.Empty); return true;
    }
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (HandleEditorShortcut(key, Keyboard.Modifiers, e.IsRepeat, e.OriginalSource as DependencyObject) || key == Key.Escape && HandleEscape()) { e.Handled = true; return; }
        base.OnPreviewKeyDown(e);
    }
    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        if (HandleArrangementWheel(Keyboard.Modifiers, e.Delta, e.OriginalSource as DependencyObject)) { e.Handled = true; return; }
        base.OnPreviewMouseWheel(e);
    }
}
