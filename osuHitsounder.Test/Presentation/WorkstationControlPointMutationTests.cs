using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Resources.Messages;
using osuHitsounder.Services;
using osuHitsounder.Services.ProjectFiles;
using osuHitsounder.ViewModels.Shell;

namespace osuHitsounder.Test.Presentation;

public class WorkstationControlPointMutationTests
{
    [Test]
    public async Task DrawerLeavesInlineEditingAndTransportAvailableWithoutTransportSideEffects()
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120], ControlPoints = [new() { Timestamp = 0, Volume = 50 }] };
        var messages = new WeakReferenceMessenger(); var dialog = new StubControlPointsDialogService();
        var audio = new StubWorkstationAudioService();
        var owner = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", project)) },
            new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler(), new StubSampleFileService(["a.wav"]),
            new StubSampleFileDialogService([]), messenger: messages, trackRemovalDialog: new StubTrackRemovalDialogService(),
            sampleManagementDialog: new StubSampleManagementDialogService(), instrumentDialogs: new StubInstrumentalTrackDialogService(), controlPointsDialog: dialog);
        dialog.Show = (initial, _) =>
        {
            var transport = (audio.PlayCalls, audio.PauseCalls, audio.SeekCalls);
            Assert.That(owner.CanEditTracks, Is.True); Assert.That(owner.CanPlaceSamples, Is.True);
            Assert.That(owner.CanAddTrack, Is.True); Assert.That(owner.CanAddInstrumentalTrack, Is.True);
            Assert.That(owner.PlayPauseCommand.CanExecute(null), Is.True);
            Assert.That(owner.CanOpenControlPoints, Is.False); Assert.That(owner.CanImportSample, Is.False);
            Assert.That(owner.CanRemoveTrack, Is.False); Assert.That(owner.CanManageSamples, Is.False);
            Assert.That(owner.CanOpenInstrumentalEditor, Is.False);
            messages.Send(new SetControlPointRequestedMessage(initial.Context, initial.Points[0].Id, 25, "Drum"));
            Assert.That((audio.PlayCalls, audio.PauseCalls, audio.SeekCalls), Is.EqualTo(transport));
            return Task.CompletedTask;
        };
        try
        {
            await Ready(owner); var transport = (audio.PlayCalls, audio.PauseCalls, audio.SeekCalls);
            await owner.AddControlPointCommand.ExecuteAsync(null); Assert.That(owner.ErrorMessage, Is.Null);
            Assert.That((audio.PlayCalls, audio.PauseCalls, audio.SeekCalls), Is.EqualTo(transport));
            Assert.That(owner.CanImportSample && owner.CanManageSamples && owner.CanRemoveTrack && owner.CanOpenInstrumentalEditor, Is.True);
        }
        finally { await owner.DeactivateAsync(); }
    }
    private static WorkstationPageVM CreateOwner(ProjectData project, IMessenger messenger,
        StubControlPointsDialogService dialog, StubWorkstationAudioService audio, IProjectFileService? files = null) =>
        new(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", project)) },
            new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler(),
            projectFiles: files, messenger: messenger, controlPointsDialog: dialog);

    private static async Task Ready(WorkstationPageVM owner)
    {
        await owner.LoadAsync("project.json", default);
        owner.AcknowledgePresentationReady(owner.LoadGeneration);
        owner.Position = TimeSpan.FromMilliseconds(1030);
        owner.SelectedSubdivisionIndex = 3;
    }
    private static async Task Until(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        while (!predicate()) await Task.Delay(1, timeout.Token);
    }

    [Test]
    public async Task OpeningNormalizesOnceBeforeSnapshotAudioAndBlockedSave()
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120],
            ControlPoints = [new() { Timestamp = 0, Volume = 0 }, new() { Timestamp = 900, Volume = 4 },
                new() { Timestamp = 1500, Volume = 5 }, new() { Timestamp = 2000, Volume = 100 }] };
        var messages = new WeakReferenceMessenger(); var dialog = new StubControlPointsDialogService();
        var audio = new StubWorkstationAudioService(); var files = new DelayedProjectFileService();
        var owner = CreateOwner(project, messages, dialog, audio, files);
        var opens = 0;
        dialog.Show = (initial, _) =>
        {
            Assert.That(initial.Points.Select(p => p.Volume), Is.EqualTo(new[] { 5, 5, 5, 100 }));
            Assert.That(audio.ControlPoints.Select(p => p.Volume), Is.EqualTo(new[] { 5, 5, 5, 100 }));
            Assert.That(files.Writes, Is.EqualTo(1));
            if (++opens == 1) Assert.That(files.LatestJson, Is.Null);
            return Task.CompletedTask;
        };
        try
        {
            await Ready(owner); Assert.That(project.ControlPoints[0].Volume, Is.Zero);
            var open = owner.AddControlPointCommand.ExecuteAsync(null); Assert.That(open.IsCompleted, Is.False);
            files.Release(); await open;
            await owner.AddControlPointCommand.ExecuteAsync(null); Assert.That(files.Writes, Is.EqualTo(1));
            Assert.That(owner.ErrorMessage, Is.Null);
        }
        finally { files.Release(); await owner.DeactivateAsync(); }
    }

    [Test]
    public async Task DeletePublishesBeforeSaveRejectsStaleAndFinalPointRestoresFullGain()
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120],
            ControlPoints = [new() { Timestamp = 0, Volume = 25 }, new() { Timestamp = 1000, Volume = 50 }] };
        var messages = new WeakReferenceMessenger(); var dialog = new StubControlPointsDialogService();
        var audio = new StubWorkstationAudioService(); var files = new DelayedProjectFileService();
        var owner = CreateOwner(project, messages, dialog, audio, files);
        dialog.Show = (initial, _) =>
        {
            var id = initial.Points[0].Id;
            messages.Send(new DeleteControlPointRequestedMessage(initial.Context with { DrawerId = Guid.NewGuid() }, id));
            Assert.That(files.Writes, Is.Zero);
            messages.Send(new DeleteControlPointRequestedMessage(initial.Context, id));
            Assert.That(project.ControlPoints, Has.Count.EqualTo(1));
            Assert.That(audio.ControlPoints.Single().Volume, Is.EqualTo(50));
            Assert.That(files.FirstWriteStarted, Is.True);
            messages.Send(new DeleteControlPointRequestedMessage(initial.Context, id));
            messages.Send(new DeleteControlPointRequestedMessage(initial.Context, Guid.NewGuid()));
            messages.Send(new DeleteControlPointRequestedMessage(initial.Context, initial.Points[1].Id));
            Assert.That(project.ControlPoints, Is.Empty); Assert.That(audio.ControlPoints, Is.Empty);
            Assert.That(new ControlPointVolumeTimeline(audio.ControlPoints).GetGain(88200), Is.EqualTo(1f));
            files.Release(); return Task.CompletedTask;
        };
        try
        {
            await Ready(owner); await owner.AddControlPointCommand.ExecuteAsync(null);
            Assert.That(owner.ErrorMessage, Is.Null);
            Assert.That(files.Writes, Is.EqualTo(2));
            Assert.That(ProjectDataSerializer.Deserialize(files.LatestJson!).ControlPoints, Is.Empty);
        }
        finally { files.Release(); await owner.DeactivateAsync(); }
    }

    [TestCase(false, false)][TestCase(true, false)][TestCase(false, true)][TestCase(true, true)]
    public async Task SnapPreservesSelectedIdentityAndSettingsAndConfirmsAllConflicts(bool replace, bool sameTimestamp)
    {
        var selected = new ControlPoint { Timestamp = sameTimestamp ? 1000 : 500, Volume = 25, Sampleset = "Drum" };
        var project = new ProjectData { Offsets = [0], BPMs = [120],
            ControlPoints = [selected, new() { Timestamp = 1000, Volume = 50 }, new() { Timestamp = 1000, Volume = 75 }] };
        var messages = new WeakReferenceMessenger(); var dialog = new StubControlPointsDialogService();
        var audio = new StubWorkstationAudioService(); var files = new DelayedProjectFileService(); files.Release();
        var owner = CreateOwner(project, messages, dialog, audio, files);
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        ControlPointSnapshot? confirmed = null; var target = -1; var count = 0;
        dialog.Confirm = (point, timestamp, conflicts, _) =>
        { confirmed = point; target = timestamp; count = conflicts; return answer.Task; };
        dialog.Show = async (initial, _) =>
        {
            var id = initial.Points.Single(p => p.Volume == 25).Id;
            messages.Send(new SnapControlPointRequestedMessage(initial.Context, id));
            Assert.That(confirmed!.Id, Is.EqualTo(id)); Assert.That((target, count), Is.EqualTo((1000, 2)));
            owner.Position = TimeSpan.FromMilliseconds(2000);
            answer.SetResult(replace);
            await Until(() => replace ? files.Writes == 1 : answer.Task.IsCompleted);
            if (!replace) await Task.Delay(20);
            Assert.That(selected.Timestamp, Is.EqualTo(replace || sameTimestamp ? 1000 : 500));
            Assert.That((selected.Volume, selected.Sampleset), Is.EqualTo((25, "Drum")));
            Assert.That(project.ControlPoints.Count(p => p.Timestamp == 1000), Is.EqualTo(replace ? 1 : sameTimestamp ? 3 : 2));
            Assert.That(files.Writes, Is.EqualTo(replace ? 1 : 0));
        };
        try { await Ready(owner); await owner.AddControlPointCommand.ExecuteAsync(null); Assert.That(owner.ErrorMessage, Is.Null); }
        finally { answer.TrySetResult(false); await owner.DeactivateAsync(); }
    }

    [TestCase(false)][TestCase(true)]
    public async Task SnapWithoutCollisionIsSingleMutationAndThenNoOp(bool unavailable)
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120],
            ControlPoints = [new() { Timestamp = 500, Volume = 25, Sampleset = "Drum" }] };
        var messages = new WeakReferenceMessenger(); var dialog = new StubControlPointsDialogService();
        var audio = new StubWorkstationAudioService(); var files = new DelayedProjectFileService(); files.Release();
        var owner = CreateOwner(project, messages, dialog, audio, files);
        dialog.Confirm = (_, _, _, _) => throw new AssertionException("No conflict.");
        dialog.Show = async (initial, _) =>
        {
            if (unavailable) project.BPMs = [];
            messages.Send(new SnapControlPointRequestedMessage(initial.Context, initial.Points[0].Id));
            await Until(() => unavailable || files.Writes == 1);
            messages.Send(new SnapControlPointRequestedMessage(initial.Context, initial.Points[0].Id));
            Assert.That(project.ControlPoints[0].Timestamp, Is.EqualTo(unavailable ? 500 : 1000));
            Assert.That(files.Writes, Is.EqualTo(unavailable ? 0 : 1));
        };
        try { await Ready(owner); await owner.AddControlPointCommand.ExecuteAsync(null); Assert.That(owner.ErrorMessage, Is.Null); }
        finally { await owner.DeactivateAsync(); }
    }

    [TestCase(false)][TestCase(true)]
    public async Task ClosedOwnerBoundsIgnoredCancellationAndRejectsLateAnswer(bool cancelProject)
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120],
            ControlPoints = [new() { Timestamp = 500, Volume = 25 }, new() { Timestamp = 1000, Volume = 50 }] };
        var messages = new WeakReferenceMessenger(); var dialog = new StubControlPointsDialogService();
        var audio = new StubWorkstationAudioService(); var owner = CreateOwner(project, messages, dialog, audio);
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var close = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var opened = new TaskCompletionSource<ControlPointsSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        dialog.Confirm = (_, _, _, _) => answer.Task;
        dialog.Show = async (initial, token) =>
        {
            messages.Send(new SnapControlPointRequestedMessage(initial.Context, initial.Points[0].Id));
            opened.SetResult(initial);
            await close.Task.WaitAsync(token);
        };
        try
        {
            await Ready(owner); var operation = owner.AddControlPointCommand.ExecuteAsync(null);
            var initial = await opened.Task;
            if (cancelProject) await owner.DeactivateAsync().WaitAsync(TimeSpan.FromSeconds(4));
            else close.SetResult();
            await operation.WaitAsync(TimeSpan.FromSeconds(4));
            messages.Send(new DeleteControlPointRequestedMessage(initial.Context, initial.Points[0].Id));
            answer.SetResult(true); await Task.Delay(20);
            Assert.That(project.ControlPoints.Select(p => p.Timestamp), Is.EqualTo(new[] { 500, 1000 }));
        }
        finally { close.TrySetResult(); answer.TrySetResult(false); await owner.DeactivateAsync(); }
    }

    [Test]
    public async Task QueuedEditsRevalidateMembershipAfterReplacementAndSerializeAcceptedSettings()
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120],
            ControlPoints = [new() { Timestamp = 500, Volume = 25 }, new() { Timestamp = 1000, Volume = 50 }] };
        var messages = new WeakReferenceMessenger(); var dialog = new StubControlPointsDialogService();
        var audio = new StubWorkstationAudioService(); var files = new DelayedProjectFileService(); files.Release();
        var owner = CreateOwner(project, messages, dialog, audio, files);
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        dialog.Confirm = (_, _, _, _) => answer.Task;
        dialog.Show = async (initial, _) =>
        {
            messages.Send(new SnapControlPointRequestedMessage(initial.Context, initial.Points[0].Id));
            messages.Send(new SetControlPointRequestedMessage(initial.Context, initial.Points[1].Id, 80, "Soft"));
            messages.Send(new SetControlPointRequestedMessage(initial.Context, initial.Points[0].Id, 35, "Drum"));
            answer.SetResult(true);
            await Until(() => files.Writes == 2);
        };
        try
        {
            await Ready(owner); await owner.AddControlPointCommand.ExecuteAsync(null);
            Assert.That(owner.ErrorMessage, Is.Null);
            Assert.That(project.ControlPoints, Has.Count.EqualTo(1));
            var saved = ProjectDataSerializer.Deserialize(files.LatestJson!).ControlPoints.Single();
            Assert.That((saved.Timestamp, saved.Volume, saved.Sampleset), Is.EqualTo((1000, 35, "Drum")));
            Assert.That(audio.ControlPoints.Single().Volume, Is.EqualTo(35));
        }
        finally { answer.TrySetResult(false); await owner.DeactivateAsync(); }
    }
}
