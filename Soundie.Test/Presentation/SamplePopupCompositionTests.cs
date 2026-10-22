using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Soundie.Services;
using Soundie.Services.FileSystem;
using Soundie.ViewModels.Popups;

namespace Soundie.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public sealed class SamplePopupCompositionTests
{
    [Test]
    public void FactoryCreatesIndependentPopupLifetimesWithoutOpeningOutputs()
    {
        _ = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var opens = 0;
        var outputs = new AudioOutputFactory(_ => { opens++; return new Soundie.Test.Services.StubWavePlayer(); },
            () => { opens++; return new Soundie.Test.Services.StubWavePlayer(); },
            () => { opens++; return new Soundie.Test.Services.StubWavePlayer(); });
        using var services = App.CreateServices("ASIO", outputs, new ImmediateUiScheduler());
        var factory = services.GetRequiredService<Func<SampleLoaderPopupVM>>();
        var first = factory();
        var second = factory();
        Assert.That(first, Is.Not.SameAs(second));
        Assert.That(services.GetRequiredService<ISampleBrowserService>(), Is.TypeOf<SampleBrowserService>());
        Assert.That(opens, Is.Zero);
        UiTestPump.Run(first.DeactivateAsync());
        UiTestPump.Run(second.DeactivateAsync());
        Assert.That(second.CanImport, Is.False);
    }
}
