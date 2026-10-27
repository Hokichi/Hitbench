using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Core.Entities;
using Hitbench.Resources.DataModels;
using Hitbench.Resources.Messages;
using Hitbench.Services;
using Hitbench.ViewModels.Shell;

namespace Hitbench.Test.Presentation;

public sealed class WorkstationDockedSamplesTests
{
    [Test]
    public async Task DockedSamplesAllowsTrackAndPointEditsAndSerializesRenameChanges()
    {
        var node = new Sample { FileName = "hit.wav", Timestamp = 125 };
        var track = new Track { SelectedSampleFileName = "hit.wav", Samples = [node] };
        var project = new ProjectData { Offsets = [0], BPMs = [120], Tracks = [track], ControlPoints = [new() { Volume = 50 }] };
        var messenger = new WeakReferenceMessenger();
        var opened = new TaskCompletionSource<SampleManagementContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        var close = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var manager = new StubSampleManagementDialogService { Present = (context, token) =>
        {
            opened.TrySetResult(context);
            return close.Task.WaitAsync(token);
        } };
        var vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", project)) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(),
            new StubSampleFileService(["hit.wav"]), new StubSampleFileDialogService([]), messenger: messenger,
            trackRemovalDialog: new StubTrackRemovalDialogService { Confirm = _ => Task.FromResult(true) },
            sampleManagementDialog: manager, exportDialog: new StubOHsExportDialogService());
        try
        {
            await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            var managing = vm.ManageSamplesCommand.ExecuteAsync(null);
            var context = await opened.Task;
            Assert.That(vm.CanPlaceSamples, Is.True);
            Assert.That(vm.CanRemoveTrack, Is.True);
            Assert.That(vm.CanEditControlPoints, Is.True);
            Assert.That(vm.CanExport, Is.False);
            Assert.That(vm.HasPendingSaves, Is.True);
            var state = vm.ControlPointsState!;
            vm.SetControlPointCommand.Execute(new SetControlPointRequestedMessage(state.Context, state.Points[0].Id, 75, null));
            Assert.That(project.ControlPoints[0].Volume, Is.EqualTo(75));
            vm.SelectNode(track, node, false);
            Assert.That(vm.BeginNodeDrag(track, node), Is.True);
            vm.PreviewNodeDrag(125);
            messenger.Send(new ProjectSamplesChangedMessage(context, ["renamed.wav"], "hit.wav", "renamed.wav"));
            close.SetResult(); await managing;
            await vm.FlushSavesAsync();
            Assert.That(vm.IsNodeDragActive, Is.False);
            Assert.That(track.SelectedSampleFileName, Is.EqualTo("renamed.wav"));
            Assert.That(node.FileName, Is.EqualTo("renamed.wav"));
        }
        finally { close.TrySetResult(); await vm.DeactivateAsync(); }
    }
}
