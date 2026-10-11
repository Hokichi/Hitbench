using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace osuHitsounder.Resources.Helpers;

internal static class KeyboardNavigationPolicy
{
    [ModuleInitializer]
    internal static void Initialize() => KeyboardNavigation.IsTabStopProperty.OverrideMetadata(
        typeof(UIElement), new FrameworkPropertyMetadata(false, null,
            (_, _) => false));
}
