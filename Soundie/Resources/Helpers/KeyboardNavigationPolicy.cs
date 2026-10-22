using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace Soundie.Resources.Helpers;

public static class KeyboardNavigationPolicy
{
    public static readonly DependencyProperty AllowTabNavigationProperty = DependencyProperty.RegisterAttached(
        "AllowTabNavigation", typeof(bool), typeof(KeyboardNavigationPolicy),
        new FrameworkPropertyMetadata(false, (element, _) => element.CoerceValue(KeyboardNavigation.IsTabStopProperty)));

    public static bool GetAllowTabNavigation(DependencyObject element) => (bool)element.GetValue(AllowTabNavigationProperty);
    public static void SetAllowTabNavigation(DependencyObject element, bool value) => element.SetValue(AllowTabNavigationProperty, value);

    [ModuleInitializer]
    internal static void Initialize() => KeyboardNavigation.IsTabStopProperty.OverrideMetadata(
        typeof(UIElement), new FrameworkPropertyMetadata(false, null,
            (element, value) => GetAllowTabNavigation(element) && (bool)value));
}
