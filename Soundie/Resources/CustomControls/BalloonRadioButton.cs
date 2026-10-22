using System.Windows;

namespace Soundie.Resources.CustomControls;

public class BalloonRadioButton : BalloonCheckBox
{
    public static readonly DependencyProperty GroupNameProperty =
        DependencyProperty.Register(nameof(GroupName), typeof(string), typeof(BalloonRadioButton),
            new PropertyMetadata(string.Empty));

    static BalloonRadioButton()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(BalloonRadioButton),
            new FrameworkPropertyMetadata(typeof(BalloonRadioButton)));
    }

    public string GroupName
    {
        get => (string)GetValue(GroupNameProperty);
        set => SetValue(GroupNameProperty, value);
    }

    protected override bool CanUncheckOnClick => false;

    protected override object? ResolveButtonIcon() =>
        IsChecked ? CheckedIcon ?? UncheckedIcon ?? ButtonIcon : base.ResolveButtonIcon();

    protected override string? ResolveButtonText() =>
        IsChecked ? CheckedText ?? UncheckedText ?? ButtonText : base.ResolveButtonText();

    protected override void OnChecked(RoutedEventArgs e)
    {
        if (Parent is DependencyObject parent)
            foreach (var peer in LogicalTreeHelper.GetChildren(parent).OfType<BalloonRadioButton>())
                if (!ReferenceEquals(this, peer) && string.Equals(GroupName, peer.GroupName, StringComparison.Ordinal))
                    peer.SetCurrentValue(IsCheckedProperty, false);

        base.OnChecked(e);
    }
}


