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
    private IReadOnlyList<string?> trackColors=Array.Empty<string?>();
    public IReadOnlyList<string?> TrackColors { get=>trackColors; set{trackColors=value;InvalidateVisual();} }
    protected override void OnRender(DrawingContext drawingContext)
    {
        var border = SystemParameters.HighContrast ? SystemColors.WindowBrush : TryFindResource("Brush.Border.Default") as Brush;
        var cue = SystemParameters.HighContrast ? SystemColors.WindowBrush : TryFindResource("Brush.Node.SelectedOutline") as Brush;
        var pen = new Pen(cue ?? border, 2);
        for (var index = 0; index < NodeBounds.Count; index++)
        {
            var bounds = NodeBounds[index];
            var brush = SystemParameters.HighContrast ? SystemColors.WindowTextBrush
                : osuHitsounder.Resources.Helpers.OHsTrackPalette.ResolveBrush(this,
                    index < TrackColorIndices.Count ? TrackColorIndices[index] : 0,
                    index < TrackColors.Count ? TrackColors[index] : null);
            var translated = bounds;
            translated.Offset(CursorPosition.X, CursorPosition.Y);
            if (!SystemParameters.HighContrast && index < TrackColors.Count && TrackColors[index] is not null)
                OHsSampleNodeDrawing.Draw(drawingContext,null,new Pen(TryFindResource("Brush.Text.Primary") as Brush,4),translated);
            OHsSampleNodeDrawing.Draw(drawingContext,brush, pen, translated);
        }
    }
}
