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
    public double NodeWidth { get; set; } = 40;
    public double NodeHeight { get; set; } = 36;
    protected override void OnRender(DrawingContext drawingContext)
    {
        var brush = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : TryFindResource("Brush.Navy") as Brush;
        var border = SystemParameters.HighContrast ? SystemColors.WindowBrush : TryFindResource("Brush.Border.Default") as Brush;
        drawingContext.DrawRoundedRectangle(brush, new Pen(border ?? brush, 1),
            new Rect(CursorPosition.X - NodeWidth / 2, CursorPosition.Y - NodeHeight / 2, NodeWidth, NodeHeight), 4, 4);
    }
}
