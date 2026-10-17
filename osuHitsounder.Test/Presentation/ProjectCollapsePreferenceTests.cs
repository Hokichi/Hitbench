using Newtonsoft.Json.Linq;
using osuHitsounder.Core.Entities;
using osuHitsounder.Services;
using osuHitsounder.Services.ProjectFiles;
using osuHitsounder.ViewModels.Shell;

namespace osuHitsounder.Test.Presentation;

public class ProjectCollapsePreferenceTests
{
    [TestCase(true)]
    [TestCase(false)]
    public async Task LoadRestoresCollapseWithoutSavingAndTogglePersists(bool collapsed)
    {
        var project = ProjectDataSerializer.Deserialize("{\"IsCollapsed\":" + collapsed.ToString().ToLowerInvariant() + ",\"Tracks\":[{}],\"Offsets\":[0],\"BPMs\":[120]}");
        var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", project)) };
        var saves = new DelayedProjectFileService(); saves.Release();
        using var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(source, new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler(), projectFiles: saves);
        try
        {
            await vm.LoadAsync("project.json", default);
            vm.AcknowledgePresentationReady(vm.LoadGeneration);
            Assert.That(vm.AreTracksCollapsed, Is.EqualTo(collapsed));
            Assert.That(saves.Writes, Is.Zero);
            var updates = audio.SampleUpdates.Count;
            vm.ToggleTracksCollapsedCommand.Execute(null);
            await vm.FlushSavesAsync();
            Assert.That(JObject.Parse(saves.LatestJson!)["Tracks"]![0]!["IsCollapsed"]!.Value<bool>(), Is.EqualTo(!collapsed));
            Assert.That(audio.SampleUpdates, Has.Count.EqualTo(updates));
            Assert.That(vm.CanUndo, Is.False);
            source.Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", ProjectDataSerializer.Deserialize("{\"IsCollapsed\":false}")));
            await vm.LoadAsync("next.json", default);
            Assert.That(vm.AreTracksCollapsed, Is.False);
            Assert.That(saves.Writes, Is.EqualTo(1));
        }
        finally { await vm.DeactivateAsync(); }
    }

    [TestCase("null")]
    [TestCase("1")]
    [TestCase("\"true\"")]
    public void CollapsePreferenceRejectsNonBoolean(string value) =>
        Assert.Throws<InvalidDataException>(() => ProjectDataSerializer.Deserialize("{\"IsCollapsed\":" + value + "}"));

    [Test]
    public void OlderProjectsDefaultExpanded() =>
        Assert.That(JObject.Parse(ProjectDataSerializer.Serialize(ProjectDataSerializer.Deserialize("{}")))["IsCollapsed"], Is.Null);
}
