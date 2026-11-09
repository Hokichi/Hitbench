using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Hitbench.Resources.CustomControls;

public class SegmentedToggleOption : RadioButton
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(Geometry), typeof(SegmentedToggleOption), new PropertyMetadata(null));
    public static readonly DependencyProperty IconSizeProperty = DependencyProperty.Register(
        nameof(IconSize), typeof(double), typeof(SegmentedToggleOption), new PropertyMetadata(20d));
    public Geometry? Icon { get => (Geometry?)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public double IconSize { get => (double)GetValue(IconSizeProperty); set => SetValue(IconSizeProperty, value); }
    static SegmentedToggleOption()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(SegmentedToggleOption),
            new FrameworkPropertyMetadata(typeof(SegmentedToggleOption)));
    }

    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(SegmentedToggleOption),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                (d, e) => d.SetCurrentValue(IsCheckedProperty, (bool)e.NewValue)));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(object), typeof(SegmentedToggleOption), new PropertyMetadata(null));

    public static readonly DependencyProperty SelectOnClickProperty =
        DependencyProperty.Register(nameof(SelectOnClick), typeof(bool), typeof(SegmentedToggleOption), new PropertyMetadata(true));

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public object? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public bool SelectOnClick
    {
        get => (bool)GetValue(SelectOnClickProperty);
        set => SetValue(SelectOnClickProperty, value);
    }

    protected override void OnClick()
    {
        if (!IsEnabled) return;
        base.OnClick();
    }

    protected override void OnToggle()
    {
        if (SelectOnClick) base.OnToggle();
    }

    protected override void OnChecked(RoutedEventArgs e)
    {
        SetCurrentValue(IsSelectedProperty, true);
        base.OnChecked(e);
    }

    protected override void OnUnchecked(RoutedEventArgs e)
    {
        SetCurrentValue(IsSelectedProperty, false);
        base.OnUnchecked(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter && IsEnabled)
        {
            OnClick();
            e.Handled = true;
        }
        else base.OnKeyDown(e);
    }
}


