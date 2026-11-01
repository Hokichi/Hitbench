using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;

namespace Hitbench.Resources.CustomControls;

[ContentProperty(nameof(Options))]
public class BalloonMenu : BalloonButton
{
    private static readonly DependencyPropertyKey IsExpandedPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsExpanded), typeof(bool), typeof(BalloonMenu), new PropertyMetadata(false));
    public static readonly DependencyProperty IsExpandedProperty = IsExpandedPropertyKey.DependencyProperty;

    static BalloonMenu()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(BalloonMenu), new FrameworkPropertyMetadata(typeof(BalloonMenu)));
        ShouldShowTextProperty.OverrideMetadata(typeof(BalloonMenu), new FrameworkPropertyMetadata(false, null, (_, _) => false));
    }

    public ObservableCollection<BalloonMenuOption> Options { get; } = [];
    public bool IsExpanded => (bool)GetValue(IsExpandedProperty);
    public event EventHandler? Expanded;
    public event EventHandler? Collapsed;

    // All native presentation goes through this replaceable boundary, including teardown.
    internal Action<Popup, bool> PresentPopup { get; set; } =
        static (popup, open) => popup.SetCurrentValue(Popup.IsOpenProperty, open);

    public BalloonMenu() => Options.CollectionChanged += (_, _) => CloseMenu();

    public void CloseMenu(bool restoreFocus = false) => SetExpanded(false);

    private void SetExpanded(bool expanded)
    {
        if (IsExpanded == expanded) return;
        SetValue(IsExpandedPropertyKey, expanded);
        if (expanded) Expanded?.Invoke(this, EventArgs.Empty);
        else Collapsed?.Invoke(this, EventArgs.Empty);
    }
}
