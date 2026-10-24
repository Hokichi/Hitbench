using System.Windows;
using System.Windows.Media;

namespace Onset.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class RuntimeIconResourceTests
{
    [TestCase("CompressAlt"), TestCase("ExpandAlt"), TestCase("Ban"), TestCase("Check")]
    [TestCase("Close"), TestCase("Info"), TestCase("Delete")]
    public void RuntimeSelectedIconKeysResolve(string key)
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        Assert.That(Application.Current.FindResource(key), Is.InstanceOf<Geometry>());
    }
}
