using Hitbench.Test;
using Hitbench.Core.Entities;
using Hitbench.Resources.Messages;
using Hitbench.Services;
using Hitbench.ViewModels.Shell;

namespace Hitbench.Test.Presentation;

public class WorkstationControlPointMutationTests
{
    [Test]
    public async Task ValueEditingKeepsTransportAndUnrelatedActionsAvailable()
    {
        var project = new ProjectData { UninheritedPoints = TimingTestData.Points([0], [120]), ControlPoints = [new() { Volume = 50 }] };
        var audio = new StubWorkstationAudioService();
        var owner = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", project)) },
            new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler(), new StubSampleFileService(["a.wav"]),
            new StubSampleFileDialogService([]), messenger: new CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger(),
            trackRemovalDialog: new StubTrackRemovalDialogService(), sampleManagementDialog: new StubSampleManagementDialogService(), instrumentDialogs: new StubInstrumentalTrackDialogService(), saveDelay: (_, _) => Task.CompletedTask);
        try
        {
            await WorkstationControlPointStateTests.Ready(owner); audio.Play();
            var transport = (audio.PlayCalls, audio.PauseCalls, audio.SeekCalls); var state = owner.ControlPointsState!;
            owner.SetControlPointCommand.Execute(new SetControlPointRequestedMessage(state.Context, state.Points[0].Id, 25, "Drum"));
            Assert.That((audio.PlayCalls, audio.PauseCalls, audio.SeekCalls), Is.EqualTo(transport));
            Assert.That(audio.Snapshot.IsPlaying, Is.True);
            Assert.That(owner.CanEditTracks && owner.CanAddTrack && owner.CanAddInstrumentalTrack && owner.CanImportSample && owner.CanRemoveTrack && owner.CanManageSamples && owner.CanOpenInstrumentalEditor, Is.True);
        }
        finally { await owner.DeactivateAsync(); }
    }
}
