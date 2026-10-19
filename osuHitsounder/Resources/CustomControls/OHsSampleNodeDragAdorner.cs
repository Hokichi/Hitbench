using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace osuHitsounder.Resources.CustomControls;

public sealed class OHsSampleNodeDragAdorner : Adorner
{
    public OHsSampleNodeDragAdorner(UIElement adornedElement) : base(adornedElement)
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => { SystemParameters.StaticPropertyChanged -= OnSystemSettingsChanged; SystemParameters.StaticPropertyChanged += OnSystemSettingsChanged; };
        Unloaded += (_, _) => SystemParameters.StaticPropertyChanged -= OnSystemSettingsChanged;
    }
    private void OnSystemSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (Dispatcher.CheckAccess()) InvalidateVisual();
        else Dispatcher.BeginInvoke(InvalidateVisual);
    }
    private Point cursorPosition;
    public Point CursorPosition { get => cursorPosition; set { cursorPosition = value; InvalidateVisual(); } }
    private IReadOnlyList<Rect> nodeBounds = Array.Empty<Rect>();
    public IReadOnlyList<Rect> NodeBounds { get => nodeBounds; set { nodeBounds = value; InvalidateVisual(); } }
    private IReadOnlyList<int> trackColorIndices = Array.Empty<int>();
    public IReadOnlyList<int> TrackColorIndices { get => trackColorIndices; set { trackColorIndices = value; InvalidateVisual(); } }
    protected override void OnRender(DrawingContext drawingContext)
    {
        var border = SystemParameters.HighContrast ? SystemColors.WindowBrush : TryFindResource("Brush.Border.Default") as Brush;
        var cue = SystemParameters.HighContrast ? SystemColors.WindowBrush : TryFindResource("Brush.Node.SelectedOutline") as Brush;
        var pen = new Pen(cue ?? border, 2);
        for (var index = 0; index < NodeBounds.Count; index++)
        {
            var bounds = NodeBounds[index];
            var brush = SystemParameters.HighContrast ? SystemColors.WindowTextBrush
                : TryFindResource(osuHitsounder.Resources.Helpers.OHsTrackPalette.GetResourceKey(
                    index < TrackColorIndices.Count ? TrackColorIndices[index] : 0)) as Brush;
            var translated = bounds;
            translated.Offset(CursorPosition.X, CursorPosition.Y);
            OHsSampleNodeDrawing.Draw(drawingContext,brush, pen, translated);
        }
    }
}
