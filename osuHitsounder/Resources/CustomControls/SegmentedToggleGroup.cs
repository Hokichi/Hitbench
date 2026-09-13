using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace osuHitsounder.Resources.CustomControls;

public class SegmentedToggleGroup : ItemsControl
{
    static SegmentedToggleGroup()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(SegmentedToggleGroup),
            new FrameworkPropertyMetadata(typeof(SegmentedToggleGroup)));
    }

    public SegmentedToggleGroup()
    {
        AddHandler(SegmentedToggleOption.ClickEvent, new RoutedEventHandler(OnOptionClick));
    }

    public static readonly DependencyProperty OptionSpacingProperty =
        DependencyProperty.Register(nameof(OptionSpacing), typeof(double), typeof(SegmentedToggleGroup), new PropertyMetadata(4.0, OnOptionSpacingChanged));

    public static readonly DependencyProperty SelectedValueProperty =
        DependencyProperty.Register(nameof(SelectedValue), typeof(object), typeof(SegmentedToggleGroup), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedValueChanged));

    public static readonly RoutedEvent OptionSelectedEvent =
        EventManager.RegisterRoutedEvent(nameof(OptionSelected), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(SegmentedToggleGroup));

    public event RoutedEventHandler OptionSelected
    {
        add => AddHandler(OptionSelectedEvent, value);
        remove => RemoveHandler(OptionSelectedEvent, value);
    }

    public double OptionSpacing
    {
        get => (double)GetValue(OptionSpacingProperty);
        set => SetValue(OptionSpacingProperty, value);
    }

    public object? SelectedValue
    {
        get => GetValue(SelectedValueProperty);
        set => SetValue(SelectedValueProperty, value);
    }

    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(e);
        ApplySelectionFromValue();
    }

    private static void OnOptionSpacingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SegmentedToggleGroup group)
            return;

        group.InvalidateMeasure();
        group.InvalidateArrange();
    }

    private static void OnSelectedValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((SegmentedToggleGroup)d).ApplySelectionFromValue();
    }

    private void OnOptionClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not SegmentedToggleOption option)
            return;

        var selectedValue = option.Value ?? option.CommandParameter;
        if (selectedValue is not null)
            SetCurrentValue(SelectedValueProperty, selectedValue);

        RaiseEvent(new RoutedEventArgs(OptionSelectedEvent, option));
    }

    private void ApplySelectionFromValue()
    {
        if (SelectedValue is null)
            return;

        for (var index = 0; index < Items.Count; index++)
        {
            if (Items[index] is not SegmentedToggleOption option)
                continue;

            var optionValue = option.Value ?? option.CommandParameter;
            if (optionValue is null)
                continue;

            option.SetCurrentValue(SegmentedToggleOption.IsSelectedProperty, Equals(optionValue, SelectedValue));
        }
    }
}


