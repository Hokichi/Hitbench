using Microsoft.Extensions.DependencyInjection;
using Hitbench.Services;
using Hitbench.Services.FileSystem;
using Hitbench.Test.Presentation;

namespace Hitbench.Test.Services;

public class AudioMemoryCompositionTests
{
    [Test]
    public async Task BrowserAndWorkstationShareTheSuppliedBudget()
    {
        var budget = new AudioMemoryBudget(4096, 4096);
        using var services = App.CreateServices(null, null, new ImmediateUiScheduler(), 100, budget);
        Assert.That(services.GetRequiredService<AudioMemoryBudget>(), Is.SameAs(budget));
        var path = Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Audio"), "*.mp3").First();
        Assert.ThrowsAsync<AudioMemoryLimitException>(() => services.GetRequiredService<ISampleBrowserService>().ReadMetadataAsync(path, default));
        using var audio = services.GetRequiredService<Func<IWorkstationAudioService>>()();
        Assert.ThrowsAsync<AudioMemoryLimitException>(() => audio.AnalyzeAsync(path, default));
        Assert.That(budget.UsedBytes, Is.Zero);
        await Task.CompletedTask;
    }
}
