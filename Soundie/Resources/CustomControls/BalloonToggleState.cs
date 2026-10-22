using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Soundie.Resources.CustomControls;

public sealed class BalloonToggleState : Freezable
{
    public static readonly DependencyProperty ButtonIconProperty =
        DependencyProperty.Register(nameof(ButtonIcon), typeof(object), typeof(BalloonToggleState));

    public static readonly DependencyProperty ButtonTextProperty =
        DependencyProperty.Register(nameof(ButtonText), typeof(string), typeof(BalloonToggleState));

    public static readonly DependencyProperty DefaultBackgroundProperty =
        DependencyProperty.Register(nameof(DefaultBackground), typeof(Brush), typeof(BalloonToggleState),
            new PropertyMetadata(null));

    public static readonly DependencyProperty HoverBackgroundProperty =
        DependencyProperty.Register(nameof(HoverBackground), typeof(Brush), typeof(BalloonToggleState),
            new PropertyMetadata(null));

    public static readonly DependencyProperty PressedBackgroundProperty =
        DependencyProperty.Register(nameof(PressedBackground), typeof(Brush), typeof(BalloonToggleState));
    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(BalloonToggleState));
    public Brush? PressedBackground { get => (Brush?)GetValue(PressedBackgroundProperty); set => SetValue(PressedBackgroundProperty, value); }
    public Brush? Foreground { get => (Brush?)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    public static readonly DependencyProperty OnCheckedProperty =
        DependencyProperty.Register(nameof(OnChecked), typeof(ICommand), typeof(BalloonToggleState));

    public object? ButtonIcon
    {
        get => GetValue(ButtonIconProperty);
        set => SetValue(ButtonIconProperty, value);
    }

    public string? ButtonText
    {
        get => (string?)GetValue(ButtonTextProperty);
        set => SetValue(ButtonTextProperty, value);
    }

    public Brush? DefaultBackground
    {
        get => (Brush?)GetValue(DefaultBackgroundProperty);
        set => SetValue(DefaultBackgroundProperty, value);
    }

    public Brush? HoverBackground
    {
        get => (Brush?)GetValue(HoverBackgroundProperty);
        set => SetValue(HoverBackgroundProperty, value);
    }

    public ICommand? OnChecked
    {
        get => (ICommand?)GetValue(OnCheckedProperty);
        set => SetValue(OnCheckedProperty, value);
    }

    protected override Freezable CreateInstanceCore() => new BalloonToggleState();
}


