using Hitbench.Core.Entities;
using System.Windows;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class RenamedApplicationResourceTests
{
    [Test]
    public void ProductAssembliesExposeHitbenchNames()
    {
        Assert.That(typeof(App).Assembly.GetName().Name, Is.EqualTo("Hitbench"));
        Assert.That(typeof(AppSettings).Assembly.GetName().Name, Is.EqualTo("Hitbench.Core"));
        Assert.That(typeof(RenamedApplicationResourceTests).Assembly.GetName().Name, Is.EqualTo("Hitbench.Test"));
    }

    [TestCase("Resources/Themes/Dark.xaml")]
    [TestCase("Resources/Themes/Light.xaml")]
    [TestCase("Resources/Fonts/NotoSans-Regular.ttf")]
    [TestCase("Resources/Fonts/NotoSans-Italic.ttf")]
    [TestCase("Resources/Fonts/NotoSans-Bold.ttf")]
    [TestCase("Resources/Fonts/NotoSans-BoldItalic.ttf")]
    [TestCase("Resources/Fonts/NotoSans-Light.ttf")]
    [TestCase("Resources/Fonts/NotoSans-LightItalic.ttf")]
    [TestCase("Resources/Fonts/NotoSansCJK-Regular.ttc")]
    [TestCase("Resources/Fonts/NotoSansCJK-Bold.ttc")]
    [TestCase("Resources/Fonts/NotoSansThai-Regular.ttf")]
    [TestCase("Resources/Fonts/NotoSansThai-Bold.ttf")]
    [TestCase("Resources/Fonts/NotoSansArabic-Regular.ttf")]
    [TestCase("Resources/Fonts/NotoSansArabic-Bold.ttf")]
    [TestCase("Resources/Fonts/NotoSansHebrew-Regular.ttf")]
    [TestCase("Resources/Fonts/NotoSansHebrew-Bold.ttf")]
    [TestCase("Resources/Fonts/OHsNotoSans.CompositeFont")]
    public void PackResourcesResolveFromHitbenchAssembly(string resourcePath)
    {
        _ = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        using var stream = Application.GetResourceStream(new Uri($"/Hitbench;component/{resourcePath}", UriKind.Relative))?.Stream;
        Assert.That(stream, Is.Not.Null);
        Assert.That(stream!.Length, Is.GreaterThan(0));
    }
}
