using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using Hitbench.ViewModels.Popups;
using Hitbench.Views.Popups;
using Hitbench.Resources.DataModels;
namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class ManageSamplesDragTests
{
    [Test]
    public void DragThresholdAndManagerReadinessGuardNativeInvocation()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts(); var files = new StubSampleFileService(new[] { "a.wav" }); var vm = new ManageSamplesPopupVM(new("project.json", null, Guid.NewGuid()), files, new(files), new StubSampleFileDialogService(Array.Empty<string>()), new StubSampleRemovalDialogService(), new WeakReferenceMessenger(), new ImmediateUiScheduler()); UiTestPump.Run(vm.InitializeAsync(default));
        var drawer = new ManageSamplesDrawer(vm); var calls = 0; drawer.SetSampleDragHandler((_, data, effects) => { calls++; Assert.That(data.GetDataPresent(DataFormats.FileDrop), Is.False); Assert.That(((OHsSampleTrackDragData)data.GetData(typeof(OHsSampleTrackDragData))!).FileName, Is.EqualTo("a.wav")); return DragDropEffects.None; });
        try { var entry = vm.Entries[0]; Assert.That(drawer.TryStartSampleDrag(entry, new Vector(0, 0)), Is.False); Assert.That(drawer.TryStartSampleDrag(entry, new Vector(SystemParameters.MinimumHorizontalDragDistance + 1, 0)), Is.True); Assert.That(calls, Is.EqualTo(1)); UiTestPump.Run(vm.EditCommand.ExecuteAsync(entry)); Assert.That(drawer.TryStartSampleDrag(entry, new Vector(100, 100)), Is.False); }
        finally { UiTestPump.Run(vm.DeactivateAsync()); }
    }
}
