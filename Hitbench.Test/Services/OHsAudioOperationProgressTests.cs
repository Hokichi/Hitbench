using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.Messages;
using Hitbench.Services.Ui;
using Hitbench.Test.Presentation;

namespace Hitbench.Test.Services;

public class OHsAudioOperationProgressTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task NestedFailureOrCancellationLeavesIndependentOperationsUsable(bool cancel)
    {
        var messenger = new WeakReferenceMessenger();
        var messages = new List<OHsAudioBusyChangedMessage>();
        messenger.Register<OHsAudioBusyChangedMessage>(messages, (_, m) => messages.Add(m));
        var progress = new OHsAudioOperationProgress(messenger, new ImmediateUiScheduler(), (_, _) => Task.CompletedTask);
        var gate = new TaskCompletionSource();
        var task = progress.RunAsync("outer", _ => progress.RunAsync("inner", _ => gate.Task, default), default);
        if (cancel) gate.SetCanceled(); else gate.SetException(new IOException("copy failed"));
        try { await task; Assert.Fail("Expected failure"); } catch (Exception e) when (e is IOException or OperationCanceledException) { }
        Assert.That(messages.Select(m => m.IsBusy), Is.EqualTo(new[] { true, false }));
        var nextGate = new TaskCompletionSource();
        var next = progress.RunAsync("next", _ => nextGate.Task, default);
        nextGate.SetResult(); await next;
        Assert.That(messages.Select(m => m.OperationId).Distinct().Count(), Is.EqualTo(2));
    }

    [Test]
    public async Task NestedWorkUsesOneOperationThroughCompletion()
    {
        var messenger = new WeakReferenceMessenger();
        var messages = new List<OHsAudioBusyChangedMessage>();
        messenger.Register<OHsAudioBusyChangedMessage>(messages, (_, m) => messages.Add(m));
        var progress = new OHsAudioOperationProgress(messenger, new ImmediateUiScheduler(), (_, _) => Task.CompletedTask);
        var gate = new TaskCompletionSource();
        var task = progress.RunAsync("import", _ => progress.RunAsync("refresh", _ => gate.Task, default), default);
        Assert.That(messages.Count(m => m.IsBusy), Is.EqualTo(1));
        Assert.That(task.IsCompleted, Is.False);
        gate.SetResult();
        await task;
        Assert.That(messages.Select(m => m.IsBusy), Is.EqualTo(new[] { true, false }));
    }
    [Test]
    public async Task FastWorkIsSilent()
    {
        var messenger = new WeakReferenceMessenger();
        var messages = new List<OHsAudioBusyChangedMessage>();
        messenger.Register<OHsAudioBusyChangedMessage>(messages, (_, m) => messages.Add(m));
        var progress = new OHsAudioOperationProgress(messenger, new ImmediateUiScheduler());
        Assert.That(await progress.RunAsync("test", _ => Task.FromResult(42), default), Is.EqualTo(42));
        Assert.That(messages, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task OverlappingWorkKeepsDistinctLifetimes(bool fail)
    {
        var messenger = new WeakReferenceMessenger();
        var messages = new List<OHsAudioBusyChangedMessage>();
        messenger.Register<OHsAudioBusyChangedMessage>(messages, (_, m) => messages.Add(m));
        var progress = new OHsAudioOperationProgress(messenger, new ImmediateUiScheduler(), (_, _) => Task.CompletedTask);
        var first = new TaskCompletionSource(); var second = new TaskCompletionSource();
        var a = progress.RunAsync("first", _ => first.Task, default);
        var b = progress.RunAsync("second", _ => second.Task, default);
        Assert.That(messages.Select(m => m.OperationId).Distinct().Count(), Is.EqualTo(2));
        if (fail) first.SetException(new IOException("failed")); else first.SetResult();
        if (fail) Assert.ThrowsAsync<IOException>(async () => await a); else await a;
        Assert.That(messages.Count(m => !m.IsBusy), Is.EqualTo(1));
        second.SetResult(); await b;
        foreach (var id in messages.Select(m => m.OperationId).Distinct())
            Assert.That(messages.Where(m => m.OperationId == id).Select(m => m.IsBusy), Is.EqualTo(new[] { true, false }));
    }

    [Test]
    public async Task CancellationBeforeDelayNeverStartsToast()
    {
        var messenger = new WeakReferenceMessenger();
        var messages = new List<OHsAudioBusyChangedMessage>();
        messenger.Register<OHsAudioBusyChangedMessage>(messages, (_, m) => messages.Add(m));
        using var cancellation = new CancellationTokenSource();
        var progress = new OHsAudioOperationProgress(messenger, new ImmediateUiScheduler());
        var work = progress.RunAsync("cancel", t => Task.Delay(Timeout.Infinite, t), cancellation.Token);
        cancellation.Cancel();
        Assert.CatchAsync<OperationCanceledException>(async () => await work);
        await Task.CompletedTask;
        Assert.That(messages, Is.Empty);
    }

    [Test]
    public async Task VisibleCancellationAlwaysEndsTheSameOperation()
    {
        var messenger=new WeakReferenceMessenger();var messages=new List<OHsAudioBusyChangedMessage>();
        messenger.Register<OHsAudioBusyChangedMessage>(messages,(_,m)=>messages.Add(m));
        using var cancel=new CancellationTokenSource();
        var progress=new OHsAudioOperationProgress(messenger,new ImmediateUiScheduler(),(_,_)=>Task.CompletedTask);
        var task=progress.RunAsync("cancel",t=>Task.Delay(Timeout.Infinite,t),cancel.Token);
        Assert.That(messages.Single().IsBusy,Is.True);
        cancel.Cancel();Assert.CatchAsync<OperationCanceledException>(async()=>await task);
        Assert.That(messages.Select(m=>m.IsBusy),Is.EqualTo(new[]{true,false}));
        Assert.That(messages.Select(m=>m.OperationId).Distinct().Count(),Is.EqualTo(1));
        await Task.CompletedTask;
    }

    [Test]
    public async Task QueuedUiStartIsAwaitedAndCannotResurrectCompletedWork()
    {
        var messenger=new WeakReferenceMessenger();var messages=new List<OHsAudioBusyChangedMessage>();
        messenger.Register<OHsAudioBusyChangedMessage>(messages,(_,m)=>messages.Add(m));
        Action? queued=null;var uiGate=new TaskCompletionSource();var workGate=new TaskCompletionSource<int>();
        var scheduler=new ImmediateUiScheduler {Invocation=(a,_)=>{queued=a;return uiGate.Task;}};
        var progress=new OHsAudioOperationProgress(messenger,scheduler,(_,_)=>Task.CompletedTask);
        var result=progress.RunAsync("queued",_=>workGate.Task,default);
        Assert.That(queued,Is.Not.Null);
        workGate.SetResult(42);Assert.That(result.IsCompleted,Is.False);
        queued!();uiGate.SetResult();
        Assert.That(await result,Is.EqualTo(42));Assert.That(messages,Is.Empty);
    }
}
