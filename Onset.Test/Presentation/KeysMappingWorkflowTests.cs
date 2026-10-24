using Onset.Resources.DataModels;
using Onset.Resources.Messages;
using Onset.Services;
using Onset.Services.FileSystem;
using CommunityToolkit.Mvvm.Messaging;
namespace Onset.Test.Presentation;

public class KeysMappingWorkflowTests
{
    [Test]
    public async Task OpensWithoutSelectionAndPreservesAllKeys()
    {
        await using var c = await OHsKeysMappingTestContext.CreateAsync();
        Assert.That(c.VM.SelectedEntry, Is.Null);
        Assert.That(c.VM.Rows.Select(r => r.Key), Is.EqualTo(Onset.Core.Constants.InstrumentalKeys.All));
        Assert.That(c.VM.Rows.Count, Is.EqualTo(108));
        Assert.That(c.VM.CanEditSelectedMapping, Is.False);
        Assert.That(c.VM.PlaySelectedCommand.CanExecute(null), Is.False);
        Assert.That(c.VM.DeleteSelectedCommand.CanExecute(null), Is.False);
        Assert.That(await c.VM.SelectKeyAsync("C10"), Is.False);
    }
    [Test]
    public async Task SelectionCommitsCapturedOldKeyAndPreservesAcceptedState()
    {
        await using var c = await OHsKeysMappingTestContext.CreateAsync();
        await c.VM.SelectKeyAsync("C5"); c.VM.SelectedEntry!.DraftFileName="new.wav";
        Assert.That(await c.VM.SelectKeyAsync("D5"), Is.True);
        Assert.That(c.Track.KeyFiles["C5"], Is.EqualTo("Piano/new.wav"));
        Assert.That(c.Track.KeyFiles.ContainsKey("D5"), Is.False);
        Assert.That(c.VM.SelectedEntry!.Key, Is.EqualTo("D5"));
    }
    [Test]
    public async Task DeleteUnmapsOnlySelectedKeyAndCloseCannotRestoreDraft()
    {
        await using var c = await OHsKeysMappingTestContext.CreateAsync();
        await c.VM.SelectKeyAsync("C5"); c.VM.SelectedEntry!.DraftFileName="new.wav";
        await c.VM.DeleteSelectedCommand.ExecuteAsync(null);
        await c.VM.PrepareCloseAsync();
        Assert.That(c.Track.KeyFiles, Is.Empty);
        Assert.That(c.Other.KeyFiles["D5"], Is.EqualTo("other.wav"));
        Assert.That(c.Track.Notes.Single().Key, Is.EqualTo("C5"));
        Assert.That(c.Track.FolderName, Is.EqualTo("Piano"));
        Assert.That(c.VM.SelectedEntry.MappingStatus, Is.EqualTo("Unmapped"));
    }
    [Test]
    public async Task ConfirmationRevisionChangeRequiresRetryAndRepeatedCommandIsGated()
    {
        var release = new TaskCompletionSource<bool>(); var calls=0;
        await using var c = await OHsKeysMappingTestContext.CreateAsync((_,_)=>{calls++;return release.Task;});
        var clear = c.VM.ClearAllMappingsCommand.ExecuteAsync(null);
        Assert.That(c.VM.ClearAllMappingsCommand.CanExecute(null), Is.False);
        await c.VM.ClearAllMappingsAsync(default);
        c.Messenger.Send(new SetInstrumentalKeyMappingRequestedMessage(c.Snapshot.Context,Guid.NewGuid(),"D5","other.wav"));
        release.SetResult(true); await clear;
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(c.Track.KeyFiles.Count, Is.EqualTo(2));
        Assert.That(c.VM.ErrorMessage, Does.Contain("Mappings changed"));
    }
    [Test]
    public async Task OwnerClearPublishesOneSnapshotAndEmptyClearDoesNotMutate()
    {
        await using var c = await OHsKeysMappingTestContext.CreateAsync();
        var revision=c.Snapshot.Revision; var snapshots=0;
        c.Messenger.Register<InstrumentalTrackChangedMessage>(this,(_,_)=>snapshots++);
        try {
            c.Messenger.Send(new OHsClearInstrumentalKeyMappingsRequestedMessage(c.Snapshot.Context,Guid.NewGuid(),revision));
            Assert.That(c.Snapshot.Revision, Is.EqualTo(revision+1));
            Assert.That(snapshots, Is.EqualTo(1));
            c.Messenger.Send(new OHsClearInstrumentalKeyMappingsRequestedMessage(c.Snapshot.Context,Guid.NewGuid(),c.Snapshot.Revision));
            Assert.That(snapshots, Is.EqualTo(1));
        } finally { c.Messenger.UnregisterAll(this); }
    }
    [Test]
    public async Task OldSessionCannotClearCurrentTrack()
    {
        await using var c = await OHsKeysMappingTestContext.CreateAsync();
        var old=c.Snapshot.Context with { ProjectSessionId=Guid.NewGuid() };
        c.Messenger.Send(new OHsClearInstrumentalKeyMappingsRequestedMessage(old,Guid.NewGuid(),c.Snapshot.Revision));
        Assert.That(c.Track.KeyFiles.Count, Is.EqualTo(1));
    }
    [Test]
    public async Task RepeatOpenPreservesSelectionAndBlocksSongUntilClose()
    {
        await using var c = await OHsKeysMappingTestContext.CreateAsync();
        await c.VM.SelectKeyAsync("D5");
        await c.Owner.KeysMappingCommand.ExecuteAsync(c.Track);
        Assert.That(c.Presentations, Is.EqualTo(1));
        Assert.That(c.VM.SelectedEntry!.Key, Is.EqualTo("D5"));
        Assert.That(c.Owner.CanPlay, Is.False);
        await c.Owner.PlayPauseCommand.ExecuteAsync(null);
        Assert.That(c.Audio.PlayCalls, Is.Zero);
        await c.Owner.SettleInstrumentalEditorAsync();
        Assert.That(c.Owner.CanPlay, Is.True);
        Assert.That(c.Audio.Snapshot.IsPlaying, Is.False);
    }
    [Test]
    public async Task ClearedMappingSaveFailurePreservesAcceptedStateAndRetrySavesIt()
    {
        var files=new FailOnceProjectFileService();
        await using var c=await OHsKeysMappingTestContext.CreateAsync(projectFiles:files);
        await c.VM.ClearAllMappingsCommand.ExecuteAsync(null);
        await StubWorkstationAudioService.WaitUntilAsync(() => c.VM.SaveError is not null);
        Assert.That(c.Track.KeyFiles, Is.Empty);
        Assert.That(c.VM.SaveError, Does.Contain("Disk full"));
        c.Owner.RetrySaveCommand.Execute(null); await StubWorkstationAudioService.WaitUntilAsync(() => c.VM.SaveError is null);
        await c.Owner.SettleInstrumentalEditorAsync(); await c.Owner.FlushSavesAsync();
        Assert.That(c.VM.SaveError, Is.Null);
        var saved=Onset.Services.ProjectFiles.ProjectDataSerializer.Deserialize(files.SavedJson!);
        var track=(Onset.Core.Entities.InstrumentalTrack)saved.Tracks[0];
        Assert.That(track.KeyFiles, Is.Empty); Assert.That(track.Notes.Count, Is.EqualTo(1)); Assert.That(track.FolderName, Is.EqualTo("Piano"));
    }
    [Test]
    public async Task SnapshotUpdatesCommandAvailability()
    {
        await using var c=await OHsKeysMappingTestContext.CreateAsync();
        await c.VM.SelectKeyAsync("D5"); var changes=0;
        c.VM.PlaySelectedCommand.CanExecuteChanged+=(_,_)=>changes++;
        c.Messenger.Send(new SetInstrumentalKeyMappingRequestedMessage(c.Snapshot.Context,Guid.NewGuid(),"D5","other.wav"));
        Assert.That(c.VM.CanPlaySelected, Is.True);
        Assert.That(changes, Is.GreaterThan(0));
    }
    [Test]
    public async Task LateSelectionCommitCannotRedirectToAnotherEntry()
    {
        var entered=new TaskCompletionSource();var release=new TaskCompletionSource<string>();
        var files=new StubSampleFileService(["Piano/C5.wav","new.wav"]) {Import=(_,_,_)=>{entered.TrySetResult();return release.Task;}};
        await using var c=await OHsKeysMappingTestContext.CreateAsync(files:files);
        await c.VM.SelectKeyAsync("C5");c.VM.SelectedEntry!.DraftFileName="new.wav";
        var selecting=c.VM.SelectKeyAsync("D5");await entered.Task;
        Assert.That(await c.VM.SelectKeyAsync("E5"),Is.False);
        release.SetResult("Piano/new.wav");await selecting;
        Assert.That(c.Track.KeyFiles["C5"],Is.EqualTo("Piano/new.wav"));
        Assert.That(c.Track.KeyFiles.ContainsKey("D5"),Is.False);
        Assert.That(c.VM.SelectedEntry!.Key,Is.EqualTo("D5"));
    }
    [Test]
    public async Task ClearCannotDeleteSampleFilesOrFolders()
    {
        var root=Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"))).FullName;
        try {
            var folder=Directory.CreateDirectory(Path.Combine(root,"Samples","Piano")).FullName;
            var sample=Path.Combine(folder,"C5.wav");File.WriteAllBytes(sample,[1,2,3]);
            var deletes=0;var files=new StubSampleFileService(["Piano/C5.wav"]) {Delete=(_,_,_)=>{deletes++;return Task.CompletedTask;}};
            await using var c=await OHsKeysMappingTestContext.CreateAsync(files:files);
            await c.VM.ClearAllMappingsCommand.ExecuteAsync(null);
            Assert.That(deletes,Is.Zero);Assert.That(Directory.Exists(folder),Is.True);Assert.That(File.ReadAllBytes(sample),Is.EqualTo(new byte[]{1,2,3}));
        } finally {Directory.Delete(root,true);}
    }
    [Test]
    public async Task ReplacementWaitsForOldPreviewCleanup()
    {
        var entered=new TaskCompletionSource();var release=new TaskCompletionSource();
        var preview=new StubSamplePreviewService {Dispose=()=>{entered.TrySetResult();return release.Task;}};
        await using var c=await OHsKeysMappingTestContext.CreateAsync(preview:preview);
        var old=c.VM;await old.SelectKeyAsync("C5");await old.PlaySelectedCommand.ExecuteAsync(null);
        var switching=c.Owner.KeysMappingCommand.ExecuteAsync(c.Other);await entered.Task;
        Assert.That(c.Presentations,Is.EqualTo(1));release.SetResult();
        await StubWorkstationAudioService.WaitUntilAsync(()=>c.Presentations==2);
        Assert.That(preview.Disposed,Is.True);Assert.That(c.VM.SelectedEntry,Is.Null);
        Assert.That(await old.SelectKeyAsync("D5"),Is.False);
        await c.Owner.SettleInstrumentalEditorAsync();await switching;
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task CancelledSelectedActionsRetainAcceptedMapping(bool play)
    {
        await using var c=await OHsKeysMappingTestContext.CreateAsync();
        await c.VM.SelectKeyAsync("C5");c.VM.SelectedEntry!.DraftFileName="new.wav";
        if(play) await c.VM.PlaySelectedAsync(new CancellationToken(true));
        else await c.VM.DeleteSelectedAsync(new CancellationToken(true));
        Assert.That(c.Track.KeyFiles["C5"],Is.EqualTo("Piano/C5.wav"));
        Assert.That(c.VM.IsBusy,Is.False);
        Assert.That(c.Preview.PlayedPaths,Is.Empty);
    }    [Test]
    public async Task ClosingDuringClearAudioStopDoesNotApplyConfirmedClear()
    {
        var entered=new TaskCompletionSource();var release=new TaskCompletionSource();
        var preview=new StubSamplePreviewService {Stop=()=>{entered.TrySetResult();return release.Task;}};
        await using var c=await OHsKeysMappingTestContext.CreateAsync(preview:preview);
        var clearing=c.VM.ClearAllMappingsAsync(default);await entered.Task;
        var closing=c.VM.DeactivateAsync();release.SetResult();await Task.WhenAll(clearing,closing);
        Assert.That(c.Track.KeyFiles["C5"],Is.EqualTo("Piano/C5.wav"));
    }    [Test]
    public async Task ClosingDuringUnmapCommitDoesNotApplyCancelledEdit()
    {
        var entered=new TaskCompletionSource();var release=new TaskCompletionSource();var stops=0;
        var preview=new StubSamplePreviewService();
        await using var c=await OHsKeysMappingTestContext.CreateAsync(preview:preview);
        await c.VM.SelectKeyAsync("C5");
        preview.Stop=()=>{if(++stops==2){entered.TrySetResult();return release.Task;}return Task.CompletedTask;};
        var deleting=c.VM.DeleteSelectedAsync(default);await entered.Task;
        var closing=c.VM.DeactivateAsync();release.SetResult();await Task.WhenAll(deleting,closing);
        Assert.That(c.Track.KeyFiles["C5"],Is.EqualTo("Piano/C5.wav"));
    }}
