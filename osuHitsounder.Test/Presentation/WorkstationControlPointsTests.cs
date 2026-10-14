using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Resources.Messages;
using osuHitsounder.Services;
using osuHitsounder.ViewModels.Shell;

namespace osuHitsounder.Test.Presentation;

public class WorkstationControlPointsTests
{
    [Test]
    public async Task ClosedDrawerRejectsRequestsWhileAcceptedSaveIsPending()
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120], ControlPoints = [new() { Timestamp = 1000, Volume = 50 }] };
        var messenger = new WeakReferenceMessenger();
        var dialog = new StubControlPointsDialogService();
        var audio = new StubWorkstationAudioService();
        var saves = new DelayedProjectFileService();
        var owner = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", project)) },
            new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler(), projectFiles: saves, messenger: messenger, controlPointsDialog: dialog);
        ControlPointsSnapshot? closed = null;
        dialog.Show = (snapshot, _) =>
        {
            closed = snapshot;
            messenger.Send(new SetControlPointRequestedMessage(snapshot.Context, snapshot.Points[0].Id, 25, "Drum"));
            return Task.CompletedTask;
        };
        try
        {
            await owner.LoadAsync("project.json", default);
            owner.AcknowledgePresentationReady(owner.LoadGeneration);
            var operation = owner.AddControlPointCommand.ExecuteAsync(null);
            Assert.That(saves.FirstWriteStarted, Is.True);
            Assert.That(operation.IsCompleted, Is.False);
            messenger.Send(new SetControlPointRequestedMessage(closed!.Context, closed.Points[0].Id, 100, "Normal"));
            Assert.That(project.ControlPoints[0].Volume, Is.EqualTo(25));
            Assert.That(audio.ControlPoints[0].Volume, Is.EqualTo(25));
            saves.Release();
            await operation;
            Assert.That(osuHitsounder.Services.ProjectFiles.ProjectDataSerializer.Deserialize(saves.LatestJson!).ControlPoints[0].Volume, Is.EqualTo(25));
        }
        finally { saves.Release(); await owner.DeactivateAsync(); }
    }
    [Test]
    public async Task LiveEditDoesNotWaitForSaveAndRetryRetainsAcceptedPoint()
    {
        var p=new ProjectData { Offsets=[0],BPMs=[120],ControlPoints=[new(){Timestamp=1000,Volume=50}] };
        var messenger=new WeakReferenceMessenger(); var dialog=new StubControlPointsDialogService(); var audio=new StubWorkstationAudioService();
        var saves=new DelayedProjectFileService();
        var owner=new WorkstationPageVM(new StubWorkstationSourceService { Load=(_,_)=>Task.FromResult(new WorkstationSource("map","song",p)) },
            new MarkerPlaybackCoordinator(audio,Task.Delay),new ImmediateUiScheduler(),projectFiles:saves,messenger:messenger,controlPointsDialog:dialog);
        dialog.Show=(s,_)=>
        {
            messenger.Send(new SetControlPointRequestedMessage(s.Context,s.Points[0].Id,25,"Drum"));
            Assert.That(saves.FirstWriteStarted,Is.True); Assert.That(audio.ControlPoints[0].Volume,Is.EqualTo(25));
            Assert.That(saves.LatestJson,Is.Null); saves.Release(); return Task.CompletedTask;
        };
        try
        {
            await owner.LoadAsync("project.json",default); owner.AcknowledgePresentationReady(owner.LoadGeneration); await owner.AddControlPointCommand.ExecuteAsync(null);
            var point=osuHitsounder.Services.ProjectFiles.ProjectDataSerializer.Deserialize(saves.LatestJson!).ControlPoints[0];
            Assert.That((point.Volume,point.Sampleset),Is.EqualTo((25,"Drum")));
        }
        finally { saves.Release(); await owner.DeactivateAsync(); }
    }
    [Test]
    public async Task SaveFailureCanRetryAndTeardownRejectsOldDrawer()
    {
        var p=new ProjectData { Offsets=[0],BPMs=[120],ControlPoints=[new(){Timestamp=1000}] };
        var messenger=new WeakReferenceMessenger(); var dialog=new StubControlPointsDialogService(); var audio=new StubWorkstationAudioService();
        var saves=new FailOnceProjectFileService();
        var owner=new WorkstationPageVM(new StubWorkstationSourceService { Load=(_,_)=>Task.FromResult(new WorkstationSource("map","song",p)) },
            new MarkerPlaybackCoordinator(audio,Task.Delay),new ImmediateUiScheduler(),projectFiles:saves,messenger:messenger,controlPointsDialog:dialog);
        var ready=new TaskCompletionSource<ControlPointsSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        dialog.Show=async(s,token)=>
        {
            messenger.Send(new SetControlPointRequestedMessage(s.Context,s.Points[0].Id,5,"Soft"));
            Assert.That(owner.SaveError,Does.Contain("Disk full")); Assert.That(audio.ControlPoints[0].Volume,Is.EqualTo(5));
            messenger.Send(new RetryControlPointsSaveRequestedMessage(s.Context)); Assert.That(owner.SaveError,Is.Null);
            ready.SetResult(s);
            try { await Task.Delay(Timeout.Infinite,token); }
            finally { messenger.Send(new SetControlPointRequestedMessage(s.Context,s.Points[0].Id,100,"Normal")); }
        };
        try
        {
            await owner.LoadAsync("project.json",default); owner.AcknowledgePresentationReady(owner.LoadGeneration);
            var open=owner.AddControlPointCommand.ExecuteAsync(null); var snapshot=await ready.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(owner.CanOpenControlPoints,Is.False); await owner.DeactivateAsync(); await open;
            messenger.Send(new SetControlPointRequestedMessage(snapshot.Context,snapshot.Points[0].Id,100,"Normal"));
            Assert.That(p.ControlPoints[0].Volume,Is.EqualTo(5)); Assert.That(saves.SavedJson,Is.Not.Null);
        }
        finally { await owner.DeactivateAsync(); }
    }
    [TestCase(50)][TestCase(0)][TestCase(100)]
    public async Task AdditionInheritsPredecessorAndDuplicateIsUnchanged(int volume)
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120], ControlPoints = [new(){Timestamp=2000,Volume=25},new(){Timestamp=900,Volume=30},new(){Timestamp=900,Volume=volume}] };
        var messenger = new WeakReferenceMessenger(); var dialogs = new StubControlPointsDialogService(); var audio = new StubWorkstationAudioService();
        var owner = Owner(project,messenger,dialogs,audio); ControlPointsContext? old = null;
        dialogs.Show = (initial,_) =>
        {
            old = initial.Context; Assert.That(project.ControlPoints, Has.Count.EqualTo(3));
            owner.Position = TimeSpan.FromMilliseconds(1030); owner.SelectedSubdivisionIndex = 3;
            messenger.Send(new AddControlPointRequestedMessage(initial.Context));
            var added = project.ControlPoints.Single(p=>p.Timestamp==1000);
            Assert.That(added.Volume, Is.EqualTo(Math.Max(5, volume))); Assert.That(added.Sampleset, Is.EqualTo("Normal"));
            messenger.Send(new AddControlPointRequestedMessage(initial.Context)); Assert.That(project.ControlPoints,Has.Count.EqualTo(4));
            owner.Position = TimeSpan.Zero; messenger.Send(new AddControlPointRequestedMessage(initial.Context));
            Assert.That(project.ControlPoints.Single(p=>p.Timestamp==0).Volume, Is.EqualTo(100));
            Assert.That(audio.ControlPoints,Has.Count.EqualTo(5)); return Task.CompletedTask;
        };
        try
        {
            await owner.LoadAsync("project.json",default); owner.AcknowledgePresentationReady(owner.LoadGeneration);
            await owner.AddControlPointCommand.ExecuteAsync(null);
            messenger.Send(new AddControlPointRequestedMessage(old!)); Assert.That(project.ControlPoints,Has.Count.EqualTo(5));
        }
        finally { await owner.DeactivateAsync(); }
    }

    [Test]
    public async Task EditsPublishImmediatelyAndRejectInvalidOrStaleRequests()
    {
        var project = new ProjectData { Offsets=[0],BPMs=[120],ControlPoints=[new(){Timestamp=1000,Volume=50}] };
        var messenger = new WeakReferenceMessenger(); var dialogs = new StubControlPointsDialogService(); var audio = new StubWorkstationAudioService();
        var owner = Owner(project,messenger,dialogs,audio);
        dialogs.Show = (initial,_) =>
        {
            var id=initial.Points.Single().Id; owner.Position=TimeSpan.FromMilliseconds(1500);
            messenger.Send(new SetControlPointRequestedMessage(initial.Context,id,25,"Drum"));
            Assert.That(project.ControlPoints[0].Volume,Is.EqualTo(25)); Assert.That(project.ControlPoints[0].Sampleset,Is.EqualTo("Drum"));
            Assert.That(audio.ControlPoints.Single().Volume,Is.EqualTo(25));
            foreach(var invalid in new[]{-1m,0m,4m,101m,50.5m}) messenger.Send(new SetControlPointRequestedMessage(initial.Context,id,invalid,"Normal"));
            messenger.Send(new SetControlPointRequestedMessage(initial.Context,id,50,"Bad"));
            messenger.Send(new SetControlPointRequestedMessage(initial.Context with { DrawerId=Guid.NewGuid() },id,50,"Normal"));
            Assert.That(project.ControlPoints[0].Volume,Is.EqualTo(25)); return Task.CompletedTask;
        };
        try { await owner.LoadAsync("project.json",default); owner.AcknowledgePresentationReady(owner.LoadGeneration); await owner.AddControlPointCommand.ExecuteAsync(null); }
        finally { await owner.DeactivateAsync(); }
    }

    private static WorkstationPageVM Owner(ProjectData project,IMessenger messenger,StubControlPointsDialogService dialogs,StubWorkstationAudioService audio) =>
        new(new StubWorkstationSourceService { Load=(_,_)=>Task.FromResult(new WorkstationSource("map","song",project)) },
            new MarkerPlaybackCoordinator(audio,Task.Delay), new ImmediateUiScheduler(),messenger:messenger,controlPointsDialog:dialogs);
}
