using CommunityToolkit.Mvvm.Messaging;
using Soundie.Core.Entities;
using Soundie.Resources.DataModels;
using Soundie.Services;
using Soundie.ViewModels.Shell;

namespace Soundie.Test.Presentation;

public sealed class WorkstationTrackIdentityTests
{
    [Test]
    public async Task SnapshotUsesCurrentDisplayedIndexAfterReorder()
    {
        var instrument = new InstrumentalTrack { KeyFiles = new() { ["C4"] = "piano.wav" } };
        var project = new ProjectData { Offsets = [0], BPMs = [120],
            Tracks = [.. Enumerable.Range(0, 8).Select(_ => new Track()), instrument] };
        var indices = new List<int>();
        var dialogs = new StubInstrumentalTrackDialogService
        {
            Arrangement = (context, _) => { indices.Add(context.Track.TrackColorIndex); return Task.CompletedTask; }
        };
        var owner = new WorkstationPageVM(new StubWorkstationSourceService
            { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", project)) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(),
            messenger: new WeakReferenceMessenger(), instrumentDialogs: dialogs);
        try
        {
            await owner.LoadAsync("project.json", default);
            owner.AcknowledgePresentationReady(owner.LoadGeneration);
            await owner.ViewArrangementCommand.ExecuteAsync(instrument);
            Assert.That(owner.MoveTrack(instrument, 1), Is.True);
            await owner.ViewArrangementCommand.ExecuteAsync(instrument);
            Assert.That(indices, Is.EqualTo(new[] { 8, 1 }));
            Assert.That(owner.Tracks[1], Is.SameAs(instrument));
        }
        finally { await owner.DeactivateAsync(); }
    }
}
