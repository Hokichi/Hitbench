using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace Hitbench.Resources.Helpers;

public static class KeyboardNavigationPolicy
{
    public static readonly DependencyProperty SuppressTabNavigationProperty = DependencyProperty.RegisterAttached(
        "SuppressTabNavigation", typeof(bool), typeof(KeyboardNavigationPolicy),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits,
            (element, _) => element.CoerceValue(KeyboardNavigation.IsTabStopProperty)));

    public static bool GetSuppressTabNavigation(DependencyObject element) => (bool)element.GetValue(SuppressTabNavigationProperty);
    public static void SetSuppressTabNavigation(DependencyObject element, bool value) => element.SetValue(SuppressTabNavigationProperty, value);

    internal static void BindSuppressionScope(DependencyObject target, DependencyObject? owner)
    {
        if (owner is null) BindingOperations.ClearBinding(target, SuppressTabNavigationProperty);
        else BindingOperations.SetBinding(target, SuppressTabNavigationProperty, new Binding
        {
            Source = owner, Path = new PropertyPath(SuppressTabNavigationProperty), Mode = BindingMode.OneWay
        });
    }

    public static readonly DependencyProperty AllowTabNavigationProperty = DependencyProperty.RegisterAttached(
        "AllowTabNavigation", typeof(bool), typeof(KeyboardNavigationPolicy),
        new FrameworkPropertyMetadata(false, (element, _) => element.CoerceValue(KeyboardNavigation.IsTabStopProperty)));

    public static bool GetAllowTabNavigation(DependencyObject element) => (bool)element.GetValue(AllowTabNavigationProperty);
    public static void SetAllowTabNavigation(DependencyObject element, bool value) => element.SetValue(AllowTabNavigationProperty, value);

    [ModuleInitializer]
    internal static void Initialize() => KeyboardNavigation.IsTabStopProperty.OverrideMetadata(
        typeof(UIElement), new FrameworkPropertyMetadata(false, null,
            (element, value) => !GetSuppressTabNavigation(element) && GetAllowTabNavigation(element) && (bool)value));
}
