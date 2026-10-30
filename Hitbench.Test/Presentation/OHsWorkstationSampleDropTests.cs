using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.DataModels;
using Hitbench.Services;
using Hitbench.ViewModels.Shell;
using Hitbench.Views.Pages;
namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class OHsWorkstationSampleDropTests
{
    [Test]
    public void TypedDropCreatesOneTrackAndExternalFilesRemainForShell()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), MarkerPlaybackCoordinator.DelayAsync), new ImmediateUiScheduler(), new StubSampleFileService(new[] { "a.wav" }), new StubSampleFileDialogService(Array.Empty<string>()), messenger: new WeakReferenceMessenger());
        UiTestPump.Run(vm.LoadAsync("project.json", default)); vm.AcknowledgePresentationReady(vm.LoadGeneration); var list = new OHsWorkstationTrackList { DataContext = vm };
        try { var data = new DataObject(typeof(OHsSampleTrackDragData), new OHsSampleTrackDragData(vm.SampleManagementContext!, "a.wav")); Assert.That(list.CanAcceptSampleDrop(data), Is.True); Assert.That(list.HandleSampleDrop(data), Is.True); Assert.That(vm.Tracks.Count, Is.EqualTo(1)); Assert.That(list.HandleSampleDrop(new DataObject(DataFormats.FileDrop, new[] { "a.wav" })), Is.False); list.Deactivate(); Assert.That(list.HandleSampleDrop(data), Is.False); }
        finally { list.Deactivate(); UiTestPump.Run(vm.DeactivateAsync()); }
    }
}
