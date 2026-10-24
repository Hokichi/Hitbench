using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Hitbench.Resources.CustomControls;

[TemplatePart(Name = PartShape, Type = typeof(Path))]
[TemplatePart(Name = PartOverlay, Type = typeof(Path))]
[TemplatePart(Name = PartIconSlot, Type = typeof(FrameworkElement))]
[TemplatePart(Name = PartIcon, Type = typeof(Path))]
[TemplatePart(Name = PartTextContent, Type = typeof(Border))]
[TemplatePart(Name = PartButtonText, Type = typeof(TextBlock))]
[TemplatePart(Name = PartSubText, Type = typeof(TextBlock))]
public class BalloonControl : ButtonBase
{
    private const string PartShape = "PART_Shape";
    private const string PartOverlay = "PART_Overlay";
    private const string PartIconSlot = "PART_IconSlot";
    private const string PartIcon = "PART_Icon";
    private const string PartTextContent = "PART_TextContent";
    private const string PartButtonText = "PART_ButtonText";
    private const string PartSubText = "PART_SubText";
    // --- DefaultBackground ---
    public static readonly DependencyProperty DefaultBackgroundProperty =
        DependencyProperty.Register(nameof(DefaultBackground), typeof(Brush), typeof(BalloonControl),
            new PropertyMetadata(Brushes.CornflowerBlue, OnDefaultBackgroundChanged));

    // --- HoveredBackground ---
    public static readonly DependencyProperty HoveredBackgroundProperty =
        DependencyProperty.Register(nameof(HoveredBackground), typeof(Brush), typeof(BalloonControl),
            new PropertyMetadata(Brushes.RoyalBlue, OnPresentationPropertyChanged));

    public static readonly DependencyProperty PressedBackgroundProperty =
        DependencyProperty.Register(nameof(PressedBackground), typeof(Brush), typeof(BalloonControl),
            new PropertyMetadata(null, OnPresentationPropertyChanged));
    public Brush? PressedBackground
    {
        get => (Brush?)GetValue(PressedBackgroundProperty);
        set => SetValue(PressedBackgroundProperty, value);
    }

    // --- StrokeBrush ---
    public static readonly DependencyProperty StrokeBrushProperty =
        DependencyProperty.Register(nameof(StrokeBrush), typeof(Brush), typeof(BalloonControl),
            new PropertyMetadata(null));

    // --- StrokeThickness ---
    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(BalloonControl),
            new PropertyMetadata(0d));

    // --- ButtonIcon ---
    public static readonly DependencyProperty ButtonIconProperty =
        DependencyProperty.Register(nameof(ButtonIcon), typeof(object), typeof(BalloonControl),
            new PropertyMetadata(null, OnPresentationPropertyChanged));

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(object), typeof(BalloonControl),
            new PropertyMetadata(null, OnPresentationPropertyChanged));

    // --- ButtonText ---
    public static readonly DependencyProperty ButtonTextProperty =
        DependencyProperty.Register(nameof(ButtonText), typeof(string), typeof(BalloonControl),
            new PropertyMetadata(null, OnPresentationPropertyChanged));

    // --- SubText ---
    public static readonly DependencyProperty SubTextProperty =
        DependencyProperty.Register(nameof(SubText), typeof(string), typeof(BalloonControl),
            new PropertyMetadata(null, OnPresentationPropertyChanged));

    public static readonly DependencyProperty HasIconProperty =
        DependencyProperty.Register(nameof(HasIcon), typeof(bool), typeof(BalloonControl),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsMeasure, OnPresentationPropertyChanged));

    // --- ShouldShowText ---
    public static readonly DependencyProperty ShouldShowTextProperty =
        DependencyProperty.Register(nameof(ShouldShowText), typeof(bool), typeof(BalloonControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure, OnPresentationPropertyChanged));

    // --- ButtonSize ---
    public static readonly DependencyProperty ButtonSizeProperty =
        DependencyProperty.Register(nameof(ButtonSize), typeof(double), typeof(BalloonControl),
            new PropertyMetadata(30.0, OnLayoutPropertyChanged));

    // --- CurveHeight ---
    public static readonly DependencyProperty CurveHeightProperty =
        DependencyProperty.Register(nameof(CurveHeight), typeof(double), typeof(BalloonControl),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnGeometryInvalidated));

    // --- CornerRadius ---
    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(nameof(CornerRadius), typeof(double), typeof(BalloonControl),
            new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsRender, OnGeometryInvalidated));

    // --- IconSize ---
    public static readonly DependencyProperty IconSizeProperty =
        DependencyProperty.Register(nameof(IconSize), typeof(double), typeof(BalloonControl),
            new PropertyMetadata(12.0, OnLayoutPropertyChanged));

    private Path? _icon;
    private FrameworkElement? _iconSlot;
    private Path? _overlay;

    private Path? _shape;
    private TextBlock? _buttonHotkeys;
    private TextBlock? _buttonText;
    private Border? _textContent;

    static BalloonControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(BalloonControl),
            new FrameworkPropertyMetadata(typeof(BalloonControl)));
    }

    public BalloonControl()
    {
        Loaded += (_, _) =>
        {
            SystemParameters.StaticPropertyChanged -= OnSystemSettingsChanged;
            SystemParameters.StaticPropertyChanged += OnSystemSettingsChanged;
            RefreshPresentation();
        };
        Unloaded += (_, _) => SystemParameters.StaticPropertyChanged -= OnSystemSettingsChanged;
    }

    private void OnSystemSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SystemParameters.HighContrast)) return;
        if (Dispatcher.CheckAccess()) RefreshPresentation();
        else Dispatcher.BeginInvoke(RefreshPresentation);
    }

    public Brush DefaultBackground
    {
        get => (Brush)GetValue(DefaultBackgroundProperty);
        set => SetValue(DefaultBackgroundProperty, value);
    }

    public Brush HoveredBackground
    {
        get => (Brush)GetValue(HoveredBackgroundProperty);
        set => SetValue(HoveredBackgroundProperty, value);
    }

    public Brush? StrokeBrush
    {
        get => (Brush?)GetValue(StrokeBrushProperty);
        set => SetValue(StrokeBrushProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public object? ButtonIcon
    {
        get => GetValue(ButtonIconProperty);
        set => SetValue(ButtonIconProperty, value);
    }

    public object? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string? ButtonText
    {
        get => (string?)GetValue(ButtonTextProperty);
        set => SetValue(ButtonTextProperty, value);
    }

    public string? SubText
    {
        get => (string?)GetValue(SubTextProperty);
        set => SetValue(SubTextProperty, value);
    }

    public bool HasIcon
    {
        get => (bool)GetValue(HasIconProperty);
        set => SetValue(HasIconProperty, value);
    }

    public bool ShouldShowText
    {
        get => (bool)GetValue(ShouldShowTextProperty);
        set => SetValue(ShouldShowTextProperty, value);
    }

    public double ButtonSize
    {
        get => (double)GetValue(ButtonSizeProperty);
        set => SetValue(ButtonSizeProperty, value);
    }

    public double CurveHeight
    {
        get => (double)GetValue(CurveHeightProperty);
        set => SetValue(CurveHeightProperty, value);
    }

    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    // -------------------------------------------------------

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _shape = GetTemplateChild(PartShape) as Path;
        _overlay = GetTemplateChild(PartOverlay) as Path;
        _iconSlot = GetTemplateChild(PartIconSlot) as FrameworkElement;
        _icon = GetTemplateChild(PartIcon) as Path;
        _textContent = GetTemplateChild(PartTextContent) as Border;
        _buttonText = GetTemplateChild(PartButtonText) as TextBlock;
        _buttonHotkeys = GetTemplateChild(PartSubText) as TextBlock;
        RefreshPresentation();
        RebuildGeometry();
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var desired = base.MeasureOverride(constraint);
        return new Size(Math.Max(ButtonSize, ShouldShowText ? desired.Width : ButtonSize), desired.Height);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        RebuildGeometry();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == IsPressedProperty || e.Property == IsMouseOverProperty || e.Property == IsEnabledProperty)
        {
            ResetShapeFill();
            ApplyForeground();
        }
        if (e.Property == ForegroundProperty || e.Property == IsEnabledProperty)
            ApplyForeground();

    }

    private static void OnGeometryInvalidated(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((BalloonControl)d).RebuildGeometry();
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((BalloonControl)d).RefreshPresentation();
    }

    private static void OnDefaultBackgroundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((BalloonControl)d).RefreshPresentation();
    }

    private static void OnPresentationPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((BalloonControl)d).RefreshPresentation();
    }

    protected virtual Brush ResolveRestingBackground() => SystemParameters.HighContrast ? SystemColors.WindowBrush : DefaultBackground;

    protected virtual Brush ResolveHoveredBackground() => SystemParameters.HighContrast ? SystemColors.HighlightBrush : HoveredBackground;

    protected virtual Brush ResolvePressedBackground() => SystemParameters.HighContrast ? SystemColors.HighlightBrush : PressedBackground ?? ResolveHoveredBackground();
    protected virtual Brush ResolveForeground() => SystemParameters.HighContrast
        ? !IsEnabled ? SystemColors.GrayTextBrush : IsMouseOver || IsPressed ? SystemColors.HighlightTextBrush : SystemColors.WindowTextBrush
        : Foreground;
    private void ApplyForeground()
    {
        var brush = ResolveForeground();
        if (_icon is not null) _icon.Fill = brush;
        if (_buttonText is not null) _buttonText.Foreground = brush;
        if (_buttonHotkeys is not null) _buttonHotkeys.Foreground = brush;
    }

    protected virtual object? ResolveButtonIcon() => Icon ?? ButtonIcon;

    protected virtual string? ResolveButtonText() => ButtonText;

    protected void RefreshPresentation()
    {
        ResetShapeFill();
        ApplyIcon();
        ApplyText();
        ApplyForeground();
        if (_iconSlot is not null) _iconSlot.Visibility = HasIcon ? Visibility.Visible : Visibility.Collapsed;
        if (_textContent is not null)
        {
            _textContent.Visibility = ShouldShowText ? Visibility.Visible : Visibility.Collapsed;
            _textContent.Margin = HasIcon ? new Thickness(4, 0, 0, 0) : new Thickness(0);
        }
        InvalidateMeasure();
    }

    // Snaps the fill back to DefaultBackground (no animation); called on template apply or DP change.
    private void ResetShapeFill()
    {
        SetShapeFill(IsEnabled && IsPressed ? ResolvePressedBackground() : IsEnabled && IsMouseOver ? ResolveHoveredBackground() : ResolveRestingBackground());
    }

    private void SetShapeFill(Brush background)
    {
        if (_shape is not null)
            _shape.Fill = background;
    }

    private void ApplyIcon()
    {
        if (_icon is not null)
            _icon.Data = ResolveGeometry(ResolveButtonIcon());
    }

    private void ApplyText()
    {
        var text = ResolveButtonText();
        if (_buttonText is not null)
        {
            _buttonText.Text = text;
            _buttonText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }
        if (_buttonHotkeys is not null)
        {
            _buttonHotkeys.Text = SubText;
            _buttonHotkeys.Margin = string.IsNullOrEmpty(text) ? new Thickness(0) : new Thickness(8, 2, 0, 0);
            _buttonHotkeys.Visibility = string.IsNullOrEmpty(SubText) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private static Geometry? ResolveGeometry(object? source)
    {
        switch (source)
        {
            case Geometry geometry:
                return geometry;
            case GeometryDrawing geometryDrawing:
                return geometryDrawing.Geometry;
            case DrawingGroup drawingGroup:
                foreach (var drawing in drawingGroup.Children)
                {
                    var resolved = ResolveGeometry(drawing);
                    if (resolved is not null)
                        return resolved;
                }

                break;
        }

        return null;
    }

    private void RebuildGeometry()
    {
        if (_shape == null || _overlay == null) return;

        var w = ActualWidth;
        var h = ActualHeight;
        var r = CornerRadius;
        if (w <= 0 || h <= 0) return;
        var geometry = new RectangleGeometry(new Rect(0, 0, w, h), r, r);
        _shape.Data = geometry;
        _overlay.Data = geometry;
    }
}

