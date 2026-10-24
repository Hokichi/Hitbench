using Onset.Core.Entities;
using System.Windows;

namespace Onset.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class RenamedApplicationResourceTests
{
    [Test]
    public void ProductAssembliesExposeOnsetNames()
    {
        Assert.That(typeof(App).Assembly.GetName().Name, Is.EqualTo("Onset"));
        Assert.That(typeof(AppSettings).Assembly.GetName().Name, Is.EqualTo("Onset.Core"));
        Assert.That(typeof(RenamedApplicationResourceTests).Assembly.GetName().Name, Is.EqualTo("Onset.Test"));
    }

    [TestCase("Resources/Themes/Dark.xaml")]
    [TestCase("Resources/Themes/Light.xaml")]
    [TestCase("Resources/Fonts/AllerDisplay_Std_Rg.ttf")]
    [TestCase("Resources/Fonts/Aller_Std_Rg.ttf")]
    [TestCase("Resources/Fonts/Aller_Std_It.ttf")]
    [TestCase("Resources/Fonts/Aller_Std_Bd.ttf")]
    [TestCase("Resources/Fonts/Aller_Std_BdIt.ttf")]
    [TestCase("Resources/Fonts/Aller_Std_Lt.ttf")]
    [TestCase("Resources/Fonts/Aller_Std_LtIt.ttf")]
    public void PackResourcesResolveFromOnsetAssembly(string resourcePath)
    {
        _ = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        using var stream = Application.GetResourceStream(new Uri($"/Onset;component/{resourcePath}", UriKind.Relative))?.Stream;
        Assert.That(stream, Is.Not.Null);
        Assert.That(stream!.Length, Is.GreaterThan(0));
    }
}
