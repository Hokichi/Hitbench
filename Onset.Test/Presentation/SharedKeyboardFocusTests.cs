using System.Windows;
using System.Windows.Controls;
using Onset.Resources.CustomControls;

namespace Onset.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class SharedKeyboardFocusTests
{
    [Test]
    public void NodeGridResolvesItsSharedControlStyle()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var grid = new BeatGridView { Style = Application.Current.TryFindResource(typeof(BeatGridView)) as Style };
        UiControlContract.AssertStyle<BeatGridView>(grid, typeof(BeatGridView));
        Assert.That(grid.FocusVisualStyle, Is.SameAs(grid.FindResource("DefaultFocusVisualStyle")));
    }

    [TestCase("Dark")]
    [TestCase("Light")]
    public void CompatibleFocusStylesAssignTheSharedTemplate(string theme)
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var resources = Application.Current.Resources;
        resources.MergedDictionaries[0] = new ResourceDictionary
        {
            Source = new Uri($"/Onset;component/Resources/Themes/{theme}.xaml", UriKind.Relative)
        };
        foreach (var key in new[] { "DefaultFocusVisualStyle", "OHsSampleManagerKeyboardFocusStyle" })
        {
            var focus = new Control { Resources = resources, Style = (Style)resources[key] };
            UiControlContract.AssertStyle<Control>(focus, key);
            UiControlContract.AssertTemplate(focus, "OHsKeyboardFocusRingTemplate");
        }
    }
}
