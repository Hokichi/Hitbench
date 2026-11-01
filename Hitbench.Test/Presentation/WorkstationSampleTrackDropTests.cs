using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Services;
using Hitbench.Resources.DataModels;
using Hitbench.ViewModels.Shell;
namespace Hitbench.Test.Presentation;

public sealed class WorkstationSampleTrackDropTests
{
    [Test]
    public async Task ValidSampleCreatesOneTrackAndStaleSessionIsRejected()
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), MarkerPlaybackCoordinator.DelayAsync), new ImmediateUiScheduler(), new StubSampleFileService(new[] { "a.wav", "b.ogg" }), new StubSampleFileDialogService(Array.Empty<string>()), messenger: new WeakReferenceMessenger());
        await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
        var data = new OHsSampleTrackDragData(vm.SampleManagementContext!, "b.ogg");
        Assert.That(vm.TryAddSampleTrack(data), Is.True); Assert.That(vm.Tracks.Single().SelectedSampleFileName, Is.EqualTo("b.ogg")); Assert.That(vm.Tracks.Single().Samples, Is.Empty);
        Assert.That(vm.Tracks.Single().Name, Is.EqualTo("New Track"));
        Assert.That(vm.Tracks.Single().Color, Is.Null);
        Assert.That(vm.TryAddSampleTrack(data with { Context = data.Context with { SessionId = Guid.NewGuid() } }), Is.False);
        Assert.That(vm.TryAddSampleTrack(data with { FileName = "missing.wav" }), Is.False);
        Assert.That(vm.TryAddSampleTrack(data with { Context = data.Context with { ProjectPath = "other.json" } }), Is.False);
        Assert.That(vm.Tracks.Count, Is.EqualTo(1));
        await vm.DeactivateAsync();
    }
}
