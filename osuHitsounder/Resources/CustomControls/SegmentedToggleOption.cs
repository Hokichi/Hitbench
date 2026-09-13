using System.Windows;
using System.Windows.Controls;

namespace osuHitsounder.Resources.CustomControls;

public class SegmentedToggleOption : Button
{
    static SegmentedToggleOption()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(SegmentedToggleOption),
            new FrameworkPropertyMetadata(typeof(SegmentedToggleOption)));
    }

    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(SegmentedToggleOption), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

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
        if (SelectOnClick)
            SetCurrentValue(IsSelectedProperty, true);

        base.OnClick();
    }
}


