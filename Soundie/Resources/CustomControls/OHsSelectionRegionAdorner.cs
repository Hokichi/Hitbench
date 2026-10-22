using System.ComponentModel;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Soundie.Resources.CustomControls;

public sealed class OHsSelectionRegionAdorner : Adorner
{
    public static readonly DependencyProperty FillBrushProperty = DependencyProperty.Register(nameof(FillBrush), typeof(Brush),
        typeof(OHsSelectionRegionAdorner), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.Register(nameof(BorderBrush), typeof(Brush),
        typeof(OHsSelectionRegionAdorner), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush? FillBrush { get => (Brush?)GetValue(FillBrushProperty); set => SetValue(FillBrushProperty, value); }
    public Brush? BorderBrush { get => (Brush?)GetValue(BorderBrushProperty); set => SetValue(BorderBrushProperty, value); }
    private Rect selectionBounds = Rect.Empty, clipBounds = Rect.Empty;
    public Rect SelectionBounds { get => selectionBounds; set { if (selectionBounds == value) return; selectionBounds = value; InvalidateVisual(); } }
    public Rect ClipBounds { get => clipBounds; set { if (clipBounds == value) return; clipBounds = value; InvalidateVisual(); } }

    public OHsSelectionRegionAdorner(UIElement adornedElement) : base(adornedElement)
    {
        IsHitTestVisible = false;
        SetResourceReference(FillBrushProperty, "Brush.Navy.Tint");
        SetResourceReference(BorderBrushProperty, "Brush.Navy");
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= OnSystemSettingsChanged;
        SystemParameters.StaticPropertyChanged += OnSystemSettingsChanged;
    }
    private void OnUnloaded(object sender, RoutedEventArgs e) => SystemParameters.StaticPropertyChanged -= OnSystemSettingsChanged;
    private void OnSystemSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Dispatcher.CheckAccess()) InvalidateVisual();
        else Dispatcher.BeginInvoke(InvalidateVisual);
    }
    protected override void OnRender(DrawingContext context)
    {
        if (SelectionBounds.IsEmpty || ClipBounds.IsEmpty) return;
        context.PushClip(new RectangleGeometry(ClipBounds));
        context.PushOpacity(0.25);
        context.DrawRectangle(SystemParameters.HighContrast ? SystemColors.HighlightBrush : FillBrush, null, SelectionBounds);
        context.Pop();
        context.DrawRectangle(null, new Pen(SystemParameters.HighContrast ? SystemColors.WindowTextBrush : BorderBrush, 1), SelectionBounds);
        context.Pop();
    }
}
