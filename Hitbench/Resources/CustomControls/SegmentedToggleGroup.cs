using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Hitbench.Resources.CustomControls;

public class SegmentedToggleGroup : ItemsControl
{
    private bool reconciling;
    static SegmentedToggleGroup()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(SegmentedToggleGroup),
            new FrameworkPropertyMetadata(typeof(SegmentedToggleGroup)));
    }

    public SegmentedToggleGroup()
    {
        AddHandler(SegmentedToggleOption.CheckedEvent, new RoutedEventHandler(OnOptionChecked));
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

    private void OnOptionChecked(object sender, RoutedEventArgs e)
    {
        if (reconciling || e.OriginalSource is not SegmentedToggleOption option || !Items.Contains(option)) return;
        if (!TrySelectOption(option)) ApplySelectionFromValue();
    }

    private bool TrySelectOption(SegmentedToggleOption option)
    {
        if (!IsEnabled || !Items.Contains(option) || !option.IsEnabled || option.Visibility == Visibility.Collapsed) return false;
        var selectedValue = option.Value ?? option.CommandParameter;
        if (selectedValue is null || Equals(selectedValue, SelectedValue)) return false;
        SetCurrentValue(SelectedValueProperty, selectedValue);
        RaiseEvent(new RoutedEventArgs(OptionSelectedEvent, option));
        return true;
    }

    private void ApplySelectionFromValue()
    {
        if (reconciling) return;
        reconciling = true;
        try
        {
            var matched = false;
            foreach (var option in Items.OfType<SegmentedToggleOption>())
            {
                var value = option.Value ?? option.CommandParameter;
                var selected = !matched && value is not null && Equals(value, SelectedValue);
                option.SetCurrentValue(SegmentedToggleOption.IsSelectedProperty, selected);
                matched |= selected;
            }
        }
        finally { reconciling = false; }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled || e.OriginalSource is not SegmentedToggleOption current || !Items.Contains(current)) return;
        if (e.Key is not (Key.Left or Key.Right or Key.Home or Key.End)) return;
        var options = Items.OfType<SegmentedToggleOption>()
            .Where(option => option.IsEnabled && option.Visibility == Visibility.Visible && (option.Value ?? option.CommandParameter) is not null).ToArray();
        if (!IsEnabled || options.Length == 0) return;
        var index = Array.IndexOf(options, current);
        var forward = (e.Key == Key.Right) != (FlowDirection == FlowDirection.RightToLeft);
        var next = e.Key switch
        {
            Key.Home => 0,
            Key.End => options.Length - 1,
            _ => index < 0 ? (forward ? 0 : options.Length - 1) : (index + (forward ? 1 : -1) + options.Length) % options.Length
        };
        TrySelectOption(options[next]);
        options[next].Focus();
        e.Handled = true;
    }
}


