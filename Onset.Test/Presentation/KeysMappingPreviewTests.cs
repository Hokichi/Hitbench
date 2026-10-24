using CommunityToolkit.Mvvm.Messaging;
using Onset.Services;
using Onset.Services.FileSystem;
namespace Onset.Test.Presentation;
public class KeysMappingPreviewTests
{
    [Test]
    public async Task AuditionIsOwnedAndReleasedByDrawer()
    {
        await using var c=await OHsKeysMappingTestContext.CreateAsync();
        await c.VM.SelectKeyAsync("C5"); c.Volumes.ApplicationVolume=25;
        await c.VM.PlaySelectedCommand.ExecuteAsync(null);
        Assert.That(c.Preview.Playing,Is.True);
        Assert.That(c.Preview.PlayedVolumes.Single(),Is.EqualTo(25));
        Assert.That(c.Preview.PlayedPaths.Single(),Is.EqualTo(SamplePaths.Resolve("project.json","Piano/C5.wav")));
        Assert.That(c.Owner.CanPlay,Is.False);
        await c.VM.DeactivateAsync();
        Assert.That(c.Preview.Disposed,Is.True); Assert.That(c.Preview.Playing,Is.False); Assert.That(c.Preview.DisposeCalls,Is.EqualTo(1));
    }
    [Test]
    public async Task NewPlayStopsOldOutputBeforeStartingAndKeyChangeStopsPreview()
    {
        await using var c=await OHsKeysMappingTestContext.CreateAsync();
        await c.VM.SelectKeyAsync("C5");
        await c.VM.PlaySelectedCommand.ExecuteAsync(null); await c.VM.PlaySelectedCommand.ExecuteAsync(null);
        var events=c.Preview.Events;
        Assert.That(events[^2..],Is.EqualTo(new[]{"stop","play"}));
        await c.VM.SelectKeyAsync("D5"); Assert.That(c.Preview.Playing,Is.False);
    }
    [Test]
    public async Task MissingOrUnmappedSelectionCannotPlayAndInvalidDraftDoesNotPlayOldMapping()
    {
        await using var c=await OHsKeysMappingTestContext.CreateAsync();
        await c.VM.SelectKeyAsync("D5"); Assert.That(c.VM.CanPlaySelected,Is.False);
        await c.VM.SelectKeyAsync("C5"); c.VM.SelectedEntry!.IsMissing=true;
        Assert.That(c.VM.CanPlaySelected,Is.False);
        c.VM.SelectedEntry.IsMissing=false; c.VM.SelectedEntry.DraftFileName="../outside.wav";
        await c.VM.PlaySelectedCommand.ExecuteAsync(null); Assert.That(c.Preview.PlayedPaths,Is.Empty);
    }
    [Test]
    public async Task LatePlayAfterCancellationIsStoppedAndCloseWaitsForDispose()
    {
        var entered=new TaskCompletionSource(); var release=new TaskCompletionSource();
        var preview=new StubSamplePreviewService {IgnorePlayCancellation=true,Play=_=>{entered.TrySetResult();return release.Task;}};
        await using var c=await OHsKeysMappingTestContext.CreateAsync(preview:preview);
        await c.VM.SelectKeyAsync("C5"); var playing=c.VM.PlaySelectedCommand.ExecuteAsync(null); await entered.Task;
        var closing=c.VM.DeactivateAsync(); Assert.That(closing.IsCompleted,Is.False);
        release.SetResult(); await Task.WhenAll(playing,closing).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(preview.Playing,Is.False); Assert.That(preview.Disposed,Is.True);
    }
    [Test]
    public async Task ConcurrentDeactivationWaitsAndDisposesOnce()
    {
        var entered=new TaskCompletionSource();var release=new TaskCompletionSource();
        var preview=new StubSamplePreviewService {Dispose=()=>{entered.TrySetResult();return release.Task;}};
        await using var c=await OHsKeysMappingTestContext.CreateAsync(preview:preview);
        var first=c.VM.DeactivateAsync();await entered.Task;var second=c.VM.DeactivateAsync();
        Assert.That(first.IsCompleted,Is.False); Assert.That(second.IsCompleted,Is.False);
        release.SetResult(); await Task.WhenAll(first,second); Assert.That(preview.DisposeCalls,Is.EqualTo(1));
    }
    [Test]
    public async Task MonitorReportsLateAudioFailureAndNaturalCompletionEndsMonitoring()
    {
        var release=new TaskCompletionSource<SamplePreviewSnapshot>();
        var preview=new StubSamplePreviewService {Snapshot=_=>release.Task};
        await using var c=await OHsKeysMappingTestContext.CreateAsync(preview:preview);
        await c.VM.SelectKeyAsync("C5");await c.VM.PlaySelectedCommand.ExecuteAsync(null);
        release.SetResult(new(false,"Device disconnected",null));
        await StubWorkstationAudioService.WaitUntilAsync(()=>c.VM.ErrorMessage is not null);
        Assert.That(c.VM.ErrorMessage,Is.EqualTo("Device disconnected"));
    }
    [Test]
    public async Task FailedStopStillDisposesAndDeactivatesRecipient()
    {
        var preview=new StubSamplePreviewService();
        await using var c=await OHsKeysMappingTestContext.CreateAsync(preview:preview);
        await c.VM.SelectKeyAsync("C5"); preview.Stop=()=>Task.FromException(new IOException("stop failed"));
        try {await c.VM.DeactivateAsync();}catch(IOException){}
        Assert.That(preview.Disposed,Is.True);Assert.That(c.VM.CanEditMapping,Is.False);
    }
    [Test]
    public async Task SelectionWaitingForStopCannotCompleteAfterClose()
    {
        var entered=new TaskCompletionSource();var release=new TaskCompletionSource();
        var preview=new StubSamplePreviewService();
        await using var c=await OHsKeysMappingTestContext.CreateAsync(preview:preview);
        preview.Stop=()=>{entered.TrySetResult();return release.Task;};
        var selecting=c.VM.SelectKeyAsync("D5");await entered.Task;
        var closing=c.VM.DeactivateAsync();release.SetResult(); await Task.WhenAll(selecting,closing);
        Assert.That(c.VM.SelectedEntry,Is.Null);
    }
    [Test]
    public async Task ExternalUnmapStopsCurrentPreview()
    {
        await using var c=await OHsKeysMappingTestContext.CreateAsync();
        await c.VM.SelectKeyAsync("C5"); await c.VM.PlaySelectedCommand.ExecuteAsync(null);
        c.Messenger.Send(new Onset.Resources.Messages.SetInstrumentalKeyMappingRequestedMessage(c.Snapshot.Context,Guid.NewGuid(),"C5",null));
        await StubWorkstationAudioService.WaitUntilAsync(()=>!c.Preview.Playing);
        Assert.That(c.VM.CanPlaySelected,Is.False);
    }
    [Test]
    public async Task ThreeRapidPlaysKeepNewestCancellationSourceAlive()
    {
        var entered=new TaskCompletionSource();var release=new TaskCompletionSource();var calls=0;
        var preview=new StubSamplePreviewService {IgnorePlayCancellation=true,Play=_=>{if(++calls==1){entered.TrySetResult();return release.Task;}return Task.CompletedTask;}};
        await using var c=await OHsKeysMappingTestContext.CreateAsync(preview:preview);
        await c.VM.SelectKeyAsync("C5");var first=c.VM.PlaySelectedAsync(default);await entered.Task;
        var second=c.VM.PlaySelectedAsync(default);var third=c.VM.PlaySelectedAsync(default);
        release.SetResult();await Task.WhenAll(first,second,third);
        Assert.That(c.VM.ErrorMessage,Is.Null);Assert.That(preview.Playing,Is.True);Assert.That(preview.PlayedPaths.Count,Is.EqualTo(2));
    }    [Test]
    public async Task CloseWhileStoppingPreviousAudioDoesNotStartAnotherPreview()
    {
        var entered=new TaskCompletionSource();var release=new TaskCompletionSource();
        var preview=new StubSamplePreviewService {IgnorePlayCancellation=true};
        await using var c=await OHsKeysMappingTestContext.CreateAsync(preview:preview);
        await c.VM.SelectKeyAsync("C5");preview.Stop=()=>{entered.TrySetResult();return release.Task;};
        var playing=c.VM.PlaySelectedAsync(default);await entered.Task;
        var closing=c.VM.DeactivateAsync();release.SetResult();await Task.WhenAll(playing,closing);
        Assert.That(preview.PlayedPaths,Is.Empty);
    }}
