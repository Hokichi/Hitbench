using System.Windows;
using System.Windows.Controls;

namespace Hitbench.Test.Presentation;

public static class UiControlContract
{
    public static void AssertStyle<T>(FrameworkElement element, object resourceKey) where T : FrameworkElement
    {
        Assert.That(element, Is.TypeOf<T>());
        var expected = element.TryFindResource(resourceKey);
        Assert.That(expected, Is.TypeOf<Style>(), $"Missing style resource: {resourceKey}");
        Assert.That(element.Style, Is.SameAs(expected), $"Incorrect style assignment: {resourceKey}");
    }

    public static void AssertTemplate(Control control, object resourceKey)
    {
        control.ApplyTemplate();
        var expected = control.TryFindResource(resourceKey);
        Assert.That(expected, Is.TypeOf<ControlTemplate>(), $"Missing template resource: {resourceKey}");
        Assert.That(control.Template, Is.SameAs(expected), $"Incorrect template assignment: {resourceKey}");
    }
}
