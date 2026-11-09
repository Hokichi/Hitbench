using Hitbench.Core.Entities;
using Hitbench.Resources.DataModels;
using Hitbench.ViewModels.Popups;
using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.Messages;
namespace Hitbench.Test.Presentation;
public sealed class ManageSamplesWaveformTests
{
    [TestCase("hit.wav", false, false)]
    [TestCase("hit.wav", false, true)]
    [TestCase("hit.wav", true, false)]
    [TestCase("hit.wav", true, true)]
    [TestCase("Mapset/hit.wav", false, false)]
    [TestCase("Mapset/hit.wav", false, true)]
    [TestCase("Mapset/hit.wav", true, false)]
    [TestCase("Mapset/hit.wav", true, true)]
    public async Task SkinRefreshPreservesManagedWaveforms(string name, bool pending, bool prepareRefresh)
    {
        var messenger = new WeakReferenceMessenger();
        var expected = new WaveformData(TimeSpan.FromSeconds(1), [.4f]);
        var release = new TaskCompletionSource<WaveformData>();
        var reads = 0;
        CancellationToken readToken = default;
        var browser = new StubSampleBrowserService { Waveform = (_, token) =>
        {
            reads++; readToken = token;
            return pending ? release.Task : Task.FromResult(expected);
        } };
        var vm = CreateWaveformManager(new([name]), messenger, browser);
        Task loading = Task.CompletedTask, preparation = Task.CompletedTask;
        try
        {
            await vm.InitializeAsync(default);
            var row = vm.Entries.Concat(vm.MapsetEntries).Single();
            loading = vm.LoadWaveformAsync(row);
            if (!pending) await loading;
            if (prepareRefresh)
            {
                preparation = vm.PrepareSkinDefaultsRefreshAsync(default);
                Assert.That(preparation.IsCompletedSuccessfully, Is.True, "Unrelated reads must not delay skin preparation.");
                await preparation;
            }
            messenger.Send(new OHsSkinDefaultsStateChangedMessage(vm.Context, false, "skin", [], true));
            messenger.Send(new OHsSkinDefaultsStateChangedMessage(vm.Context, true, "skin", ["SkinDefault/normal-hitnormal.wav"], false));
            Assert.That(vm.SkinDefaultEntries, Has.Count.EqualTo(1));
            Assert.That(readToken.IsCancellationRequested, Is.False);
            release.TrySetResult(expected);
            await loading;
            Assert.That(row.Waveform, Is.SameAs(expected));
            Assert.That(reads, Is.EqualTo(1));
        }
        finally
        {
            release.TrySetResult(expected);
            await Task.WhenAll(loading, preparation);
            await vm.DeactivateAsync();
        }
    }

    [TestCase(false, false)] [TestCase(false, true)]
    [TestCase(true, false)] [TestCase(true, true)]
    public async Task ManagedRefreshPreservesSkinWaveforms(bool pending, bool overwrite)
    {
        var messenger = new WeakReferenceMessenger();
        var expected = new WaveformData(TimeSpan.FromSeconds(1), [.4f]);
        var managedWaveform = new WaveformData(TimeSpan.FromSeconds(1), [.1f]);
        var release = new TaskCompletionSource<WaveformData>();
        var skinReads = 0;
        CancellationToken skinToken = default;
        var browser = new StubSampleBrowserService { Waveform = (path, token) =>
        {
            if (!path.Contains("SkinDefault")) return Task.FromResult(managedWaveform);
            skinReads++; skinToken = token;
            return pending ? release.Task : Task.FromResult(expected);
        } };
        var files = new StubSampleFileService(overwrite ? ["Mapset/hit.wav"] : []);
        var vm = CreateWaveformManager(files, messenger, browser);
        Task loading = Task.CompletedTask;
        try
        {
            await vm.InitializeAsync(default);
            messenger.Send(new OHsSkinDefaultsStateChangedMessage(vm.Context, true, "skin", ["SkinDefault/normal-hitnormal.wav"], false));
            var skinRow = vm.SkinDefaultEntries.Single();
            loading = vm.LoadWaveformAsync(skinRow);
            if (!pending) await loading;
            if (overwrite) await vm.LoadWaveformAsync(vm.MapsetEntries.Single());
            managedWaveform = new WaveformData(TimeSpan.FromSeconds(1), [.9f]);
            messenger.Send(new ProjectSamplesChangedMessage(vm.Context, ["Mapset/hit.wav"], reloadSamples: true));
            Assert.That(skinToken.IsCancellationRequested, Is.False);
            release.TrySetResult(expected);
            await loading;
            Assert.That(vm.SkinDefaultEntries.Single(), Is.SameAs(skinRow));
            Assert.That(skinRow.Waveform, Is.SameAs(expected));
            Assert.That(skinReads, Is.EqualTo(1));
            var managedRow = vm.MapsetEntries.Single();
            if (!overwrite) await vm.LoadWaveformAsync(managedRow);
            Assert.That(managedRow.Waveform, Is.SameAs(managedWaveform));
        }
        finally { release.TrySetResult(expected); await loading; await vm.DeactivateAsync(); }
    }

    [TestCase(false)] [TestCase(true)]
    public async Task LocalManagedMutationPreservesSkinWaveforms(bool delete)
    {
        var messenger = new WeakReferenceMessenger();
        var names = new List<string> { "hit.wav" };
        var files = new StubSampleFileService(names)
        {
            Rename = (_, _, _, _) => { names[0] = "renamed.wav"; return Task.FromResult(names[0]); },
            Delete = (_, name, _) => { names.Remove(name); return Task.CompletedTask; }
        };
        var expected = new WaveformData(TimeSpan.FromSeconds(1), [.4f]);
        var release = new TaskCompletionSource<WaveformData>();
        var skinReads = 0;
        CancellationToken managedToken = default;
        var browser = new StubSampleBrowserService { Waveform = (path, token) =>
        {
            if (path.Contains("SkinDefault")) { skinReads++; return Task.FromResult(expected); }
            managedToken = token;
            return delete ? release.Task : Task.FromResult(expected);
        } };
        var removal = new StubSampleRemovalDialogService { Confirm = (_, _) => Task.FromResult(true) };
        var vm = CreateWaveformManager(files, messenger, browser, removal);
        Task loading = Task.CompletedTask;
        try
        {
            await vm.InitializeAsync(default);
            messenger.Send(new OHsSkinDefaultsStateChangedMessage(vm.Context, true, "skin", ["SkinDefault/normal-hitnormal.wav"], false));
            var skinRow = vm.SkinDefaultEntries.Single();
            await vm.LoadWaveformAsync(skinRow);
            var managedRow = vm.Entries.Single();
            loading = vm.LoadWaveformAsync(managedRow);
            if (delete)
            {
                await vm.DeleteCommand.ExecuteAsync(managedRow);
                Assert.That(vm.Entries, Is.Empty);
                Assert.That(managedToken.IsCancellationRequested, Is.True);
                release.TrySetResult(expected);
                await loading;
                Assert.That(managedRow.Waveform, Is.Null);
            }
            else
            {
                await loading;
                await vm.EditCommand.ExecuteAsync(managedRow);
                vm.DraftStem = "renamed";
                await vm.CommitRenameCommand.ExecuteAsync(null);
                Assert.That(managedRow.FileName, Is.EqualTo("renamed.wav"));
                Assert.That(managedRow.Waveform, Is.SameAs(expected));
            }
            Assert.That(skinRow.Waveform, Is.SameAs(expected));
            Assert.That(skinReads, Is.EqualTo(1));
        }
        finally { release.TrySetResult(expected); await loading; await vm.DeactivateAsync(); }
    }

    [TestCase(false)] [TestCase(true)]
    public async Task SkinRefreshSettlesAffectedReadsAndRejectsLateContent(bool busyNotificationFirst)
    {
        var messenger = new WeakReferenceMessenger();
        var stale = new WaveformData(TimeSpan.FromSeconds(1), [.1f]);
        var fresh = new WaveformData(TimeSpan.FromSeconds(1), [.9f]);
        var release = new TaskCompletionSource<WaveformData>();
        var managedRelease = new TaskCompletionSource<WaveformData>();
        var reads = 0;
        CancellationToken skinToken = default, managedToken = default;
        var browser = new StubSampleBrowserService { Waveform = (path, token) =>
        {
            if (!path.Contains("SkinDefault")) { managedToken = token; return managedRelease.Task; }
            skinToken = token;
            return ++reads == 1 ? release.Task : Task.FromResult(fresh);
        } };
        var vm = CreateWaveformManager(new(["Mapset/hit.wav"]), messenger, browser);
        Task loading = Task.CompletedTask, managedLoading = Task.CompletedTask, preparation = Task.CompletedTask;
        try
        {
            await vm.InitializeAsync(default);
            messenger.Send(new OHsSkinDefaultsStateChangedMessage(vm.Context, true, "old", ["SkinDefault/normal-hitnormal.wav"], false));
            var oldRow = vm.SkinDefaultEntries.Single();
            loading = vm.LoadWaveformAsync(oldRow);
            managedLoading = vm.LoadWaveformAsync(vm.MapsetEntries.Single());
            if (busyNotificationFirst)
                messenger.Send(new OHsSkinDefaultsStateChangedMessage(vm.Context, true, "old", [oldRow.FileName], true));
            preparation = vm.PrepareSkinDefaultsRefreshAsync(default);
            Assert.That(skinToken.IsCancellationRequested, Is.True);
            Assert.That(managedToken.IsCancellationRequested, Is.False);
            Assert.That(preparation.IsCompleted, Is.False);
            release.SetResult(stale);
            await loading;
            Assert.That(preparation.IsCompletedSuccessfully, Is.True, "Only skin reads must settle before cache replacement.");
            await preparation;
            messenger.Send(new OHsSkinDefaultsStateChangedMessage(vm.Context, true, "new", [oldRow.FileName], false));
            var newRow = vm.SkinDefaultEntries.Single();
            await vm.LoadWaveformAsync(newRow);
            Assert.That(oldRow.Waveform, Is.Null);
            Assert.That(newRow, Is.Not.SameAs(oldRow));
            Assert.That(newRow.Waveform, Is.SameAs(fresh));
            managedRelease.SetResult(fresh);
            await managedLoading;
            Assert.That(vm.MapsetEntries.Single().Waveform, Is.SameAs(fresh));
        }
        finally
        {
            release.TrySetResult(stale);
            managedRelease.TrySetResult(fresh);
            await Task.WhenAll(loading, managedLoading, preparation);
            await vm.DeactivateAsync();
        }
    }

    [Test]
    public async Task ClosingCancelsBothWaveformGroups()
    {
        var messenger = new WeakReferenceMessenger();
        var release = new TaskCompletionSource<WaveformData>();
        var tokens = new List<CancellationToken>();
        var browser = new StubSampleBrowserService { Waveform = (_, token) => { tokens.Add(token); return release.Task; } };
        var vm = CreateWaveformManager(new(["Mapset/hit.wav"]), messenger, browser);
        Task closing = Task.CompletedTask;
        try
        {
            await vm.InitializeAsync(default);
            messenger.Send(new OHsSkinDefaultsStateChangedMessage(vm.Context, true, "skin", ["SkinDefault/normal-hitnormal.wav"], false));
            var rows = vm.MapsetEntries.Concat(vm.SkinDefaultEntries).ToArray();
            var loads = rows.Select(vm.LoadWaveformAsync).ToArray();
            Assert.That(tokens, Has.Count.EqualTo(2));
            closing = vm.DeactivateAsync();
            Assert.That(tokens.All(token => token.IsCancellationRequested), Is.True);
            Assert.That(closing.IsCompleted, Is.False);
            release.SetResult(new WaveformData(TimeSpan.FromSeconds(1), [.4f]));
            await Task.WhenAll(loads.Append(closing));
            Assert.That(rows.All(row => row.Waveform is null), Is.True);
        }
        finally
        {
            release.TrySetResult(new WaveformData(TimeSpan.FromSeconds(1), [.4f]));
            await closing;
            await vm.DeactivateAsync();
        }
    }

    private static ManageSamplesPopupVM CreateWaveformManager(StubSampleFileService files, IMessenger messenger,
        StubSampleBrowserService browser, StubSampleRemovalDialogService? removal = null) =>
        new(new("project.json", null, Guid.NewGuid()), files, new(files), new StubSampleFileDialogService([]),
            removal ?? new StubSampleRemovalDialogService(), messenger, new ImmediateUiScheduler(), waveformReader: browser);

    [Test]
    public async Task ContentRefreshReloadsWaveformWhenFileListIsUnchanged()
    {
        var messenger = new WeakReferenceMessenger();
        var current = new WaveformData(TimeSpan.FromSeconds(1), [.1f]);
        var browser = new StubSampleBrowserService { Waveform = (_, _) => Task.FromResult(current) };
        var files = new StubSampleFileService(["Mapset/hit.wav"]);
        var vm = new ManageSamplesPopupVM(new("project.json", null, Guid.NewGuid()), files, new(files),
            new StubSampleFileDialogService([]), new StubSampleRemovalDialogService(), messenger, new ImmediateUiScheduler(), waveformReader: browser);
        try
        {
            await vm.InitializeAsync(default);
            var row = vm.MapsetEntries.Single();
            await vm.LoadWaveformAsync(row);
            current = new WaveformData(TimeSpan.FromSeconds(1), [.9f]);
            messenger.Send(new Hitbench.Resources.Messages.ProjectSamplesChangedMessage(vm.Context, ["Mapset/hit.wav"], reloadSamples: true));
            await vm.LoadWaveformAsync(row);
            Assert.That(row.Waveform, Is.SameAs(current));
        }
        finally { await vm.DeactivateAsync(); }
    }
    [Test]
    public async Task DecoderConcurrencyIsBoundedAndRenameKeepsLoadedContent()
    {
        var reads=0;var release=new TaskCompletionSource<WaveformData>();
        var browser=new StubSampleBrowserService {Waveform=(_,_)=>{reads++;return release.Task;}};
        var files=new StubSampleFileService(["a.wav","b.wav","c.wav"]);
        var vm=new ManageSamplesPopupVM(new("project.json",null,Guid.NewGuid()),files,new(files),new StubSampleFileDialogService([]),new StubSampleRemovalDialogService(),new WeakReferenceMessenger(),new ImmediateUiScheduler(),waveformReader:browser);
        await vm.InitializeAsync(default);var rows=vm.Entries.ToArray();
        var tasks=rows.Select(vm.LoadWaveformAsync).ToArray();Assert.That(reads,Is.EqualTo(2));
        var expected=new WaveformData(TimeSpan.FromSeconds(1),[.5f]);release.SetResult(expected);await Task.WhenAll(tasks);
        Assert.That(reads,Is.EqualTo(3));
        await vm.EditCommand.ExecuteAsync(rows[0]);vm.DraftStem="renamed";await vm.CommitRenameCommand.ExecuteAsync(null);
        Assert.That(rows[0].Waveform,Is.SameAs(expected));Assert.That(rows[0].FileName,Is.EqualTo("renamed.wav"));
        await vm.DeactivateAsync();
    }
    [Test]
    public async Task RequestsCoalesceAndClosingIgnoresLateResult()
    {
        var reads=0; var pending=new TaskCompletionSource<WaveformData>();
        var browser=new StubSampleBrowserService {Waveform=(_,_)=>{reads++;return pending.Task;}};
        var files=new StubSampleFileService(["hit.wav"]);
        var vm=new ManageSamplesPopupVM(new("project.json",null,Guid.NewGuid()),files,new(files),new StubSampleFileDialogService([]),new StubSampleRemovalDialogService(),new WeakReferenceMessenger(),new ImmediateUiScheduler(),waveformReader:browser);
        await vm.InitializeAsync(default);var row=vm.Entries.Single();
        Assert.That(reads,Is.Zero);
        var first=vm.LoadWaveformAsync(row);var second=vm.LoadWaveformAsync(row);
        Assert.That(reads,Is.EqualTo(1));
        var close=vm.DeactivateAsync();pending.SetResult(new WaveformData(TimeSpan.FromSeconds(1),[.2f]));
        await Task.WhenAll(first,second,close);
        Assert.That(row.Waveform,Is.Null);
    }
    [Test]
    public async Task SuccessfulReadIsReusedAndFailureDoesNotDisableEditing()
    {
        var reads=0;var expected=new WaveformData(TimeSpan.FromSeconds(1),[.4f]);
        var browser=new StubSampleBrowserService {Waveform=(path,_)=>{reads++;return path.EndsWith("bad.wav")?Task.FromException<WaveformData>(new IOException("bad")):Task.FromResult(expected);}};
        var files=new StubSampleFileService(["hit.wav","bad.wav"]);
        var vm=new ManageSamplesPopupVM(new("project.json",null,Guid.NewGuid()),files,new(files),new StubSampleFileDialogService([]),new StubSampleRemovalDialogService(),new WeakReferenceMessenger(),new ImmediateUiScheduler(),waveformReader:browser);
        await vm.InitializeAsync(default);var row=vm.Entries.Single(e=>e.FileName=="hit.wav");
        await vm.LoadWaveformAsync(row);await vm.LoadWaveformAsync(row);
        Assert.That(reads,Is.EqualTo(1));Assert.That(row.Waveform,Is.SameAs(expected));
        var bad=vm.Entries.Single(e=>e.FileName=="bad.wav");await vm.LoadWaveformAsync(bad);
        Assert.That(vm.EditCommand.CanExecute(bad),Is.True);Assert.That(vm.ErrorMessage,Is.Null);
        await vm.DeactivateAsync();
    }
}
