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
    protected override void OnRender(DrawingContext drawingContext)
    {
        var brush = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : TryFindResource("Brush.Navy") as Brush;
        var border = SystemParameters.HighContrast ? SystemColors.WindowBrush : TryFindResource("Brush.Border.Default") as Brush;
        var cue = SystemParameters.HighContrast ? SystemColors.WindowBrush : TryFindResource("Brush.Text.Primary") as Brush;
        var pen = new Pen(cue ?? border ?? brush, 1);
        foreach (var bounds in NodeBounds)
        {
            var translated = bounds;
            translated.Offset(CursorPosition.X, CursorPosition.Y);
            OHsSampleNodeDrawing.Draw(drawingContext,brush, pen, translated);
        }
    }
}
