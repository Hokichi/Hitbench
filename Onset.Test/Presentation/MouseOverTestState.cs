using System.Reflection;
using System.Windows;

namespace Onset.Test.Presentation;

/// <summary>Supplies mouse-over state without a native input source or window.</summary>
internal static class MouseOverTestState
{
    private static readonly DependencyPropertyKey Key = (DependencyPropertyKey)(typeof(UIElement)
        .GetField("IsMouseOverPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null)
        ?? throw new InvalidOperationException("WPF mouse-over dependency-property key is unavailable."));

    public static void Set(UIElement element, bool value) => element.SetValue(Key, value);
}
