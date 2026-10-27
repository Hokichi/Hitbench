using System.Windows;
using System.Windows.Media;
using System.Windows.Data;
using Hitbench.Views.Pages;

namespace Hitbench.Test.Presentation;

internal static class WorkstationControlTree
{
    internal static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
        // Configuration controls live in a closed Popup. Exercise their real bindings
        // in memory without opening the popup or giving it a native presentation source.
        if (root is OHsWorkstationTrackRow row && row.FindName("SampleConfigurationContent") is FrameworkElement content)
        {
            content.SetBinding(UIElement.IsEnabledProperty, new Binding(nameof(UIElement.IsEnabled)) { Source = row, Mode = BindingMode.OneWay });
            UiTestPump.Layout(content, 320, 240);
            row.RefreshPresentation();
            foreach (var child in Descendants(content)) yield return child;
        }
    }
}
