using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using Hitbench.Resources.CustomControls;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class SegmentedToggleIconTests
{
    [SetUp]
    public void Setup() => new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();

    [TestCase(false, true)] [TestCase(true, false)] [TestCase(true, true)]
    public void OptionalIconsPreserveContentAndRadioSelection(bool icon, bool content)
    {
        var geometry = Geometry.Parse("M0,0 L10,10");
        var option = new SegmentedToggleOption { Icon = icon ? geometry : null, Content = content ? "Choice" : null, Value = 1 };
        AutomationProperties.SetName(option, "Choice");
        var group = new SegmentedToggleGroup { Items = { option } };
        UiTestPump.Layout(group, 300, 60);
        UiControlContract.AssertTemplate(option, "SegmentedToggleOptionTemplate");
        Assert.That(option.Icon, Is.SameAs(icon ? geometry : null));
        var peer = UIElementAutomationPeer.CreatePeerForElement(option)!;
        ((ISelectionItemProvider)peer.GetPattern(PatternInterface.SelectionItem)).Select();
        Assert.That(group.SelectedValue, Is.EqualTo(1));
        Assert.That(peer.GetName(), Is.EqualTo("Choice"));
        option.IsEnabled = false;
        Assert.Throws<ElementNotEnabledException>(() => ((ISelectionItemProvider)peer.GetPattern(PatternInterface.SelectionItem)).Select());
    }

    [Test]
    public void CustomContentTemplateRemainsAssignedWithIcon()
    {
        var option = new SegmentedToggleOption { Content = "Choice", Icon = Geometry.Parse("M0,0 L10,10") };
        var template = (DataTemplate)Application.Current.FindResource("SegmentedToggleOptionContentTemplate");
        option.ContentTemplate = template;
        UiTestPump.Layout(option, 100, 50);
        Assert.That(option.ContentTemplate, Is.SameAs(template));
        UiControlContract.AssertTemplate(option, "SegmentedToggleOptionTemplate");
    }
}
