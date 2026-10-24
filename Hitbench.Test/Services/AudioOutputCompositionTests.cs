using Microsoft.Extensions.DependencyInjection;
using Hitbench.Services;

namespace Hitbench.Test.Services;

public class AudioOutputCompositionTests
{
    [Test]
    public void WorkstationsOwnDistinctAudioServicesWithoutOpeningDevicesDuringComposition()
    {
        var opens = 0;
        var outputs = new AudioOutputFactory(_=>{opens++;return new StubWavePlayer();},
            ()=>{opens++;return new StubWavePlayer();},()=>{opens++;return new StubWavePlayer();});
        using var services = App.CreateServices("Studio ASIO",outputs);
        var factory = services.GetRequiredService<Func<IWorkstationAudioService>>();
        using var first = factory();
        using var second = factory();
        Assert.That(first,Is.Not.SameAs(second));
        Assert.That(opens,Is.Zero);
    }
}
