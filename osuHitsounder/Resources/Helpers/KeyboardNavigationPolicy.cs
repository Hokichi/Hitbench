using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace osuHitsounder.Resources.Helpers;

internal static class KeyboardNavigationPolicy
{
    internal static readonly DependencyProperty IsTabNavigationEnabledProperty = DependencyProperty.RegisterAttached(
        "IsTabNavigationEnabled", typeof(bool), typeof(KeyboardNavigationPolicy),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits,
            (element, _) => { if (element is UIElement) element.CoerceValue(KeyboardNavigation.IsTabStopProperty); }));

    internal static void EnableTabNavigation(DependencyObject scope) => scope.SetValue(IsTabNavigationEnabledProperty, true);

    [ModuleInitializer]
    internal static void Initialize() => KeyboardNavigation.IsTabStopProperty.OverrideMetadata(
        typeof(UIElement), new FrameworkPropertyMetadata(false, null,
            (element, value) => (bool)element.GetValue(IsTabNavigationEnabledProperty) && (bool)value));
}
