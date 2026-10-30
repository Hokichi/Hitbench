using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Hitbench.Views.Pages;

public partial class OHsWorkstationToolbar : UserControl
{
    private static readonly DependencyPropertyKey IsCompactLayoutPropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsCompactLayout), typeof(bool), typeof(OHsWorkstationToolbar), new PropertyMetadata(false));
    public static readonly DependencyProperty IsCompactLayoutProperty = IsCompactLayoutPropertyKey.DependencyProperty;
    public bool IsCompactLayout => (bool)GetValue(IsCompactLayoutProperty);
    public OHsWorkstationToolbar()
    {
        InitializeComponent();
        SizeChanged += (_, e) => SetValue(IsCompactLayoutPropertyKey, e.NewSize.Width < 960);
    }
    public OHsWorkstationFooter Footer => FooterControls;
    public static readonly DependencyProperty IsShortLayoutProperty = DependencyProperty.Register(nameof(IsShortLayout), typeof(bool), typeof(OHsWorkstationToolbar));
    public bool IsShortLayout { get => (bool)GetValue(IsShortLayoutProperty); set => SetValue(IsShortLayoutProperty, value); }
    public bool IsCollapseActionOrigin(DependencyObject? origin) => IsWithin(origin, ProjectActionsButton) ||
        ProjectActionsButton.ContextMenu.Items.OfType<MenuItem>().Any(item => IsWithin(origin, item));
    private static bool IsWithin(DependencyObject? origin, DependencyObject owner)
    {
        for (var current = origin; current is not null; current = current is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D ? System.Windows.Media.VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
            if (ReferenceEquals(current, owner)) return true;
        return false;
    }
    public bool FocusCollapseAction()
    {
        var target = (UIElement)ProjectActionsButton;
        FocusManager.SetFocusedElement(FocusManager.GetFocusScope(target), target);
        return target.Focus();
    }

}
