using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;

namespace osuHitsounder.Resources.CustomControls;

public class ValueSlider : Slider
{
    public static readonly DependencyProperty DisplayTextProperty = DependencyProperty.Register(
        nameof(DisplayText), typeof(string), typeof(ValueSlider), new PropertyMetadata(string.Empty));

    public string DisplayText
    {
        get => (string)GetValue(DisplayTextProperty);
        set => SetValue(DisplayTextProperty, value);
    }

    public static readonly DependencyProperty ShowValueTagProperty = DependencyProperty.Register(nameof(ShowValueTag), typeof(bool), typeof(ValueSlider), new PropertyMetadata(false, OnTagChanged));
    public static readonly DependencyProperty ValueTagTemplateProperty = DependencyProperty.Register(nameof(ValueTagTemplate), typeof(DataTemplate), typeof(ValueSlider), new PropertyMetadata(null, OnTagChanged));
    private static readonly DependencyPropertyKey IsValueTagVisiblePropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsValueTagVisible), typeof(bool), typeof(ValueSlider), new PropertyMetadata(false));
    public static readonly DependencyProperty IsValueTagVisibleProperty = IsValueTagVisiblePropertyKey.DependencyProperty;
    public bool ShowValueTag { get => (bool)GetValue(ShowValueTagProperty); set => SetValue(ShowValueTagProperty, value); }
    public DataTemplate? ValueTagTemplate { get => (DataTemplate?)GetValue(ValueTagTemplateProperty); set => SetValue(ValueTagTemplateProperty, value); }
    public bool IsValueTagVisible => (bool)GetValue(IsValueTagVisibleProperty);
    private Thumb? thumb;
    private AdornerLayer? layer;
    private ValueSliderTagAdorner? tag;
    private bool loaded, dragging;
    public ValueSlider()
    {
        Loaded += (_, _) => { loaded = true; AttachThumb(); RefreshTag(); };
        Unloaded += (_, _) => { loaded = false; dragging = false; DetachThumb(); RefreshTag(); };
        IsEnabledChanged += (_, _) => { if (!IsEnabled) dragging = false; RefreshTag(); };
        IsKeyboardFocusWithinChanged += (_, _) => RefreshTag();
        ValueChanged += (_, _) => tag?.InvalidateArrange();
        LayoutUpdated += (_, _) => tag?.UpdatePosition();
    }
    private static void OnTagChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var slider = (ValueSlider)d;
        slider.RemoveTag();
        slider.RefreshTag();
    }
    public override void OnApplyTemplate()
    {
        RemoveTag();
        DetachThumb();
        dragging = false;
        base.OnApplyTemplate();
        thumb = (GetTemplateChild("PART_Track") as Track)?.Thumb;
        AttachThumb();
        RefreshTag();
    }
    private void DetachThumb()
    {
        if (thumb is null) return;
        thumb.MouseEnter -= OnThumbHover; thumb.MouseLeave -= OnThumbHover;
        thumb.DragStarted -= OnDragStarted; thumb.DragCompleted -= OnDragCompleted;
        thumb.LostMouseCapture -= OnCaptureLost;
    }
    private void AttachThumb()
    {
        DetachThumb();
        if (thumb is not null)
        {
            thumb.MouseEnter += OnThumbHover; thumb.MouseLeave += OnThumbHover;
            thumb.DragStarted += OnDragStarted; thumb.DragCompleted += OnDragCompleted;
            thumb.LostMouseCapture += OnCaptureLost;
        }
    }
    private void OnThumbHover(object sender, MouseEventArgs e) => RefreshTag();
    private void OnDragStarted(object sender, DragStartedEventArgs e) { dragging = true; RefreshTag(); }
    private void OnDragCompleted(object sender, DragCompletedEventArgs e) { dragging = false; RefreshTag(); }
    private void OnCaptureLost(object sender, MouseEventArgs e) { dragging = false; RefreshTag(); }
    private void RefreshTag() => UpdateValueTagState(thumb?.IsMouseOver == true, dragging, IsKeyboardFocusWithin);
    internal void UpdateValueTagState(bool isThumbHovered, bool isDragging, bool hasKeyboardFocus)
    {
        var visible = loaded && IsEnabled && ShowValueTag && (isThumbHovered || isDragging || hasKeyboardFocus);
        SetValue(IsValueTagVisiblePropertyKey, visible);
        if (!visible) { RemoveTag(); return; }
        if (tag is null && thumb is not null && ValueTagTemplate is not null && AdornerLayer.GetAdornerLayer(this) is { } owner)
        {
            layer = owner;
            tag = new ValueSliderTagAdorner(this, ValueTagTemplate);
            layer.Add(tag);
        }
    }
    internal Thumb? ValueThumb => thumb;
    private void RemoveTag()
    {
        if (tag is not null) layer?.Remove(tag);
        tag = null; layer = null;
    }
}
