using System.Windows;
using System.Windows.Controls;
using osuHitsounder.Resources.CustomControls;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class SharedKeyboardFocusTests
{
    [TestCase("Dark")]
    [TestCase("Light")]
    public void CompatibleFocusStylesAssignTheSharedTemplate(string theme)
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var resources = Application.Current.Resources;
        resources.MergedDictionaries[0] = new ResourceDictionary
        {
            Source = new Uri($"/osuHitsounder;component/Resources/Themes/{theme}.xaml", UriKind.Relative)
        };
        foreach (var key in new[] { "DefaultFocusVisualStyle", "OHsSampleManagerKeyboardFocusStyle" })
        {
            var focus = new Control { Resources = resources, Style = (Style)resources[key] };
            UiControlContract.AssertStyle<Control>(focus, key);
            UiControlContract.AssertTemplate(focus, "OHsKeyboardFocusRingTemplate");
        }
    }
}
