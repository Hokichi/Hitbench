using Hitbench.Test;
using Hitbench.Core.Entities;
using Hitbench.Services;
using Hitbench.Services.ProjectFiles;
using Hitbench.ViewModels.Shell;

namespace Hitbench.Test.Presentation;

public class WorkstationMarkerModePersistenceTests
{
    [Test]
    public async Task FailedWriteAndRetryKeepModeAndOriginalChangeTime()
    {
        var files = new FailOnceProjectFileService(); var clock = new ManualTimeProvider();
        var instant = DateTimeOffset.Parse("2026-11-09T00:00:00Z"); clock.SetUtcNow(instant);
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay),
            new ImmediateUiScheduler(), projectFiles: files, timeProvider: clock);
        try
        {
            await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            vm.SetMarkerMode(true); Assert.ThrowsAsync<IOException>(() => vm.FlushSavesAsync());
            Assert.That(vm.Project!.IsMarkerStatic, Is.True); Assert.That(vm.Project.LastChanged, Is.EqualTo(default(DateTimeOffset)));
            clock.SetUtcNow(instant.AddHours(1)); vm.RetrySaveCommand.Execute(null); await vm.FlushSavesAsync();
            var saved = ProjectDataSerializer.Deserialize(files.SavedJson!);
            Assert.That(saved.IsMarkerStatic, Is.True); Assert.That(saved.LastChanged, Is.EqualTo(instant));
        }
        finally { await vm.DeactivateAsync(); }
    }
    [TestCase(false)] [TestCase(true)]
    public async Task RestoreDoesNotSaveAndAppliedChangeUsesExistingQueue(bool initial)
    {
        var files = new DelayedProjectFileService(); files.Release();
        var clock = new ManualTimeProvider(); var instant = DateTimeOffset.Parse("2026-11-09T00:00:00Z"); clock.SetUtcNow(instant);
        var project = new ProjectData { IsMarkerStatic = initial, UninheritedPoints = TimingTestData.Points([0], [120]), ControlPoints = [new()] };
        var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "audio", project)) };
        var vm = new WorkstationPageVM(source, new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay),
            new ImmediateUiScheduler(), projectFiles: files, timeProvider: clock);
        try
        {
            await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            Assert.That(vm.IsStaticMarker, Is.EqualTo(initial)); Assert.That(files.Writes, Is.Zero);
            vm.SetMarkerMode(initial); Assert.That(vm.HasPendingSaves, Is.False);
            vm.SetMarkerMode(!initial); await vm.FlushSavesAsync();
            var saved = ProjectDataSerializer.Deserialize(files.LatestJson!);
            Assert.That(saved.IsMarkerStatic, Is.EqualTo(!initial)); Assert.That(saved.LastChanged, Is.EqualTo(instant));
            Assert.That(vm.CanUndo, Is.False);
            var writes = files.Writes; vm.SetMarkerMode(!initial); await vm.FlushSavesAsync(); Assert.That(files.Writes, Is.EqualTo(writes));
            source.Load = (_, _) => Task.FromResult(new WorkstationSource("map", "audio", new ProjectData { UninheritedPoints = TimingTestData.Points([0], [120]) }));
            await vm.LoadAsync("other.json", default); Assert.That(vm.IsStaticMarker, Is.False);
            Assert.That(project.IsMarkerStatic, Is.EqualTo(!initial));
        }
        finally { await vm.DeactivateAsync(); }
    }

    [TestCase("{}")] [TestCase("{\"IsMarkerStatic\":false}")] [TestCase("{\"ismarkerstatic\":true}")]
    public void JsonRoundTrip(string json)
    {
        var project = ProjectDataSerializer.Deserialize(json);
        Assert.That(project.IsMarkerStatic, Is.EqualTo(json.Contains("true")));
        Assert.That(ProjectDataSerializer.Deserialize(ProjectDataSerializer.Serialize(project)).IsMarkerStatic, Is.EqualTo(project.IsMarkerStatic));
    }

    [TestCase("\"true\"")] [TestCase("1")] [TestCase("null")] [TestCase("{}")]
    public void RejectNonBoolean(string token) => Assert.Throws<InvalidDataException>(() => ProjectDataSerializer.Deserialize("{\"IsMarkerStatic\":" + token + "}"));
    [Test]
    public void RejectAmbiguousCasing() => Assert.Throws<InvalidDataException>(() => ProjectDataSerializer.Deserialize("{\"IsMarkerStatic\":true,\"ismarkerstatic\":false}"));
}
