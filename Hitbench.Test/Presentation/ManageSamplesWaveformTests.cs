using Hitbench.Core.Entities;
using Hitbench.Resources.DataModels;
using Hitbench.ViewModels.Popups;
using CommunityToolkit.Mvvm.Messaging;
namespace Hitbench.Test.Presentation;
public sealed class ManageSamplesWaveformTests
{
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
