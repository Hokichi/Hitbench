using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Core.Entities;
using Hitbench.Resources.DataModels;
using Hitbench.Services;
using Hitbench.ViewModels.Popups;
using Hitbench.ViewModels.Shell;

namespace Hitbench.Test.Presentation;

public class InstrumentalControlPointPlaybackTests
{
    [TestCase(true)][TestCase(false)]
    public async Task ArrangementUsesWorkstationControlPointsAndKeepsMappedFiles(bool hasPoints)
    {
        var instrument = new InstrumentalTrack { KeyFiles = new() { ["C4"] = "piano.wav" }, Notes = [new() { Key = "C4", Timestamp = 1500 }] };
        var project = new ProjectData { Offsets = [0], BPMs = [120], Tracks = [instrument], ControlPoints = hasPoints ? [new() { Timestamp = 1000, Volume = 50, Sampleset = "Drum" }] : [] };
        var messenger = new WeakReferenceMessenger(); var dialogs = new StubInstrumentalTrackDialogService(); var audio = new StubWorkstationAudioService();
        var owner = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", project)) },
            new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler(), messenger: messenger, instrumentDialogs: dialogs);
        dialogs.Arrangement = async (context, _) =>
        {
            var drawer = new InstrumentalArrangementDrawerVM(context, messenger, new ImmediateUiScheduler());
            try
            {
                await drawer.InitializeAsync(default);
                Assert.That(audio.ControlPoints.Count, Is.EqualTo(hasPoints ? 2 : 1));
                Assert.That(new ControlPointVolumeTimeline(audio.ControlPoints).GetGain(66150), Is.EqualTo(hasPoints ? .5f : 1f));
                Assert.That(audio.SampleUpdates.Last().Single().FileName, Is.EqualTo("piano.wav"));
                await owner.SeekCommand.ExecuteAsync(TimeSpan.FromMilliseconds(1600));
                Assert.That(drawer.Position, Is.EqualTo(TimeSpan.FromMilliseconds(1600)));
                Assert.That(audio.ControlPoints.Count, Is.EqualTo(hasPoints ? 2 : 1));
            }
            finally { await drawer.DeactivateAsync(); }
        };
        try { await owner.LoadAsync("project.json", default); owner.AcknowledgePresentationReady(owner.LoadGeneration); await owner.ViewArrangementCommand.ExecuteAsync(instrument); }
        finally { await owner.DeactivateAsync(); }
    }
}
