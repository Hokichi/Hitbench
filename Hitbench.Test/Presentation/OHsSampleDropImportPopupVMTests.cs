using Hitbench.ViewModels.Popups;
namespace Hitbench.Test.Presentation;

public sealed class OHsSampleDropImportPopupVMTests
{
    [Test]
    public async Task CheckedSelectionAndExternalPreviewAreOwned()
    {
        var preview = new StubSamplePreviewService();
        var vm = new OHsSampleDropImportPopupVM(new[] { "C:/one/a.wav", "C:/two/b.ogg" }, new StubSampleBrowserService(), preview, new ImmediateUiScheduler());
        await vm.InitializeAsync(default);
        vm.Entries[1].IsChecked = false;
        Assert.That(vm.GetCheckedPaths(), Is.EqualTo(new[] { "C:/one/a.wav" }));
        await vm.PlayCommand.ExecuteAsync(vm.Entries[0]);
        Assert.That(preview.PlayedPaths.Single(), Is.EqualTo("C:/one/a.wav"));
        vm.Entries[0].IsChecked = false; Assert.That(vm.CanConfirm, Is.False);
        await vm.DeactivateAsync(); Assert.That(preview.Disposed, Is.True);
    }
 [Test] public async Task LateWaveformIsIgnoredAfterDeactivation()
 {
  var gate=new TaskCompletionSource<Hitbench.Core.Entities.WaveformData>();
  var browser=new StubSampleBrowserService {Waveform=(_,_)=>gate.Task};
  var vm=new OHsSampleDropImportPopupVM(new[]{"a.wav"},browser,new StubSamplePreviewService(),new ImmediateUiScheduler());await vm.InitializeAsync(default);
  var read=vm.LoadWaveformAsync(vm.Entries[0]);var close=vm.DeactivateAsync();
  Assert.That(close.IsCompleted,Is.False);gate.SetResult(new(TimeSpan.FromSeconds(1),new float[]{.5f}));await close;await read;
  Assert.That(vm.Entries[0].Waveform,Is.Null);Assert.That(vm.CanConfirm,Is.False);
 }
 [Test] public async Task EqualNamesRemainIndependentlySelectable()
 {
  var vm=new OHsSampleDropImportPopupVM(new[]{"C:/one/hit.wav","C:/two/hit.wav"},new StubSampleBrowserService(),new StubSamplePreviewService(),new ImmediateUiScheduler());await vm.InitializeAsync(default);
  vm.Entries[0].IsChecked=false;Assert.That(vm.GetCheckedPaths(),Is.EqualTo(new[]{"C:/two/hit.wav"}));await vm.DeactivateAsync();
 }
}
