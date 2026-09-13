using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace osuHitsounder.Resources.Helpers;

public static class DependencyObjectTree
{
    public static DependencyObject? GetParent(DependencyObject element)
    {
        if (element is Visual or Visual3D)
        {
            var visualParent = VisualTreeHelper.GetParent(element);
            if (visualParent is not null)
                return visualParent;
        }

        if (element is FrameworkContentElement frameworkContentElement)
            return frameworkContentElement.Parent;

        if (element is ContentElement contentElement)
            return ContentOperations.GetParent(contentElement);

        return LogicalTreeHelper.GetParent(element);
    }

    public static IEnumerable<DependencyObject> GetChildren(DependencyObject element)
    {
        if (element is Visual or Visual3D)
        {
            var childCount = VisualTreeHelper.GetChildrenCount(element);
            for (var index = 0; index < childCount; index++)
                yield return VisualTreeHelper.GetChild(element, index);

            yield break;
        }

        foreach (var child in LogicalTreeHelper.GetChildren(element))
            if (child is DependencyObject dependencyObject)
                yield return dependencyObject;
    }

    public static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        for (var current = source; current is not null; current = GetParent(current))
            if (current is T match)
                return match;

        return null;
    }

    public static bool IsDescendantOf(DependencyObject? element, DependencyObject ancestor)
    {
        for (var current = element; current is not null; current = GetParent(current))
            if (ReferenceEquals(current, ancestor))
                return true;

        return false;
    }
}


