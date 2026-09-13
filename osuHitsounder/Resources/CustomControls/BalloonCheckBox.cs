using System.Windows;
using System.Windows.Media;

namespace osuHitsounder.Resources.CustomControls;

public class BalloonCheckBox : BalloonControl
{
    public static readonly DependencyProperty CheckedBackgroundProperty =
        DependencyProperty.Register(nameof(CheckedBackground), typeof(Brush), typeof(BalloonCheckBox),
            new PropertyMetadata(Brushes.MintCream, OnPresentationPropertyChanged));

    public static readonly DependencyProperty UncheckedIconProperty =
        DependencyProperty.Register(nameof(UncheckedIcon), typeof(object), typeof(BalloonCheckBox),
            new PropertyMetadata(null, OnPresentationPropertyChanged));

    public static readonly DependencyProperty CheckedIconProperty =
        DependencyProperty.Register(nameof(CheckedIcon), typeof(object), typeof(BalloonCheckBox),
            new PropertyMetadata(null, OnPresentationPropertyChanged));

    public static readonly DependencyProperty UncheckedTextProperty =
        DependencyProperty.Register(nameof(UncheckedText), typeof(string), typeof(BalloonCheckBox),
            new PropertyMetadata(null, OnPresentationPropertyChanged));

    public static readonly DependencyProperty CheckedTextProperty =
        DependencyProperty.Register(nameof(CheckedText), typeof(string), typeof(BalloonCheckBox),
            new PropertyMetadata(null, OnPresentationPropertyChanged));

    public static readonly DependencyProperty IsCheckedProperty =
        DependencyProperty.Register(nameof(IsChecked), typeof(bool), typeof(BalloonCheckBox),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnIsCheckedChanged));

    public static readonly RoutedEvent CheckedEvent = EventManager.RegisterRoutedEvent(
        nameof(Checked), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(BalloonCheckBox));

    public static readonly RoutedEvent UncheckedEvent = EventManager.RegisterRoutedEvent(
        nameof(Unchecked), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(BalloonCheckBox));

    static BalloonCheckBox()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(BalloonCheckBox),
            new FrameworkPropertyMetadata(typeof(BalloonCheckBox)));
    }

    public Brush CheckedBackground
    {
        get => (Brush)GetValue(CheckedBackgroundProperty);
        set => SetValue(CheckedBackgroundProperty, value);
    }

    public object? UncheckedIcon
    {
        get => GetValue(UncheckedIconProperty);
        set => SetValue(UncheckedIconProperty, value);
    }

    public object? CheckedIcon
    {
        get => GetValue(CheckedIconProperty);
        set => SetValue(CheckedIconProperty, value);
    }

    public string? UncheckedText
    {
        get => (string?)GetValue(UncheckedTextProperty);
        set => SetValue(UncheckedTextProperty, value);
    }

    public string? CheckedText
    {
        get => (string?)GetValue(CheckedTextProperty);
        set => SetValue(CheckedTextProperty, value);
    }

    public bool IsChecked
    {
        get => (bool)GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    public event RoutedEventHandler Checked
    {
        add => AddHandler(CheckedEvent, value);
        remove => RemoveHandler(CheckedEvent, value);
    }

    public event RoutedEventHandler Unchecked
    {
        add => AddHandler(UncheckedEvent, value);
        remove => RemoveHandler(UncheckedEvent, value);
    }

    protected virtual bool CanUncheckOnClick => true;

    protected override void OnClick()
    {
        if (!IsChecked || CanUncheckOnClick)
            SetCurrentValue(IsCheckedProperty, !IsChecked);
        base.OnClick();
    }

    protected virtual void OnChecked(RoutedEventArgs e) => RaiseEvent(e);

    protected virtual void OnUnchecked(RoutedEventArgs e) => RaiseEvent(e);

    protected override Brush ResolveRestingBackground() =>
        IsChecked ? CheckedBackground : DefaultBackground;

    protected override object? ResolveButtonIcon() =>
        IsChecked ? CheckedIcon ?? ButtonIcon : UncheckedIcon ?? ButtonIcon;

    protected override string? ResolveButtonText() =>
        IsChecked ? CheckedText ?? ButtonText : UncheckedText ?? ButtonText;

    private static void OnIsCheckedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var checkBox = (BalloonCheckBox)d;
        checkBox.RefreshPresentation();
        var routedEvent = (bool)e.NewValue ? CheckedEvent : UncheckedEvent;
        var args = new RoutedEventArgs(routedEvent, checkBox);
        if ((bool)e.NewValue)
            checkBox.OnChecked(args);
        else
            checkBox.OnUnchecked(args);
    }

    private static void OnPresentationPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((BalloonCheckBox)d).RefreshPresentation();
    }
}


