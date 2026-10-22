using Soundie.Core.Entities;

namespace Soundie.Test.Presentation;

public sealed class RenamedApplicationResourceTests
{
    [Test]
    public void ProductAssembliesExposeSoundieNames()
    {
        Assert.That(typeof(App).Assembly.GetName().Name, Is.EqualTo("Soundie"));
        Assert.That(typeof(AppSettings).Assembly.GetName().Name, Is.EqualTo("Soundie.Core"));
    }
}
