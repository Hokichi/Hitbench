using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Services.Dialogs;
using osuHitsounder.ViewModels.Popups;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)] [NonParallelizable]
public sealed class SampleManagementDialogServiceTests
{
    [Test] public void ContextLoadsBeforeSubstitutedPresentation()
    {
        var files = new StubSampleFileService(["hit.wav"]); var presented = false;
        var service = new SampleManagementDialogService(() => null, Dispatcher.CurrentDispatcher,
            () => throw new AssertionException("Loader unexpectedly created"),
            (context, _, _) => new ManageSamplesPopupVM(context, files, new(files), new StubSampleFileDialogService(Array.Empty<string>()),
                new StubSampleRemovalDialogService(), new WeakReferenceMessenger(), new ImmediateUiScheduler()),
            (vm, _) => { presented = true; Assert.That(vm.Entries.Single().FileName, Is.EqualTo("hit.wav")); return Task.CompletedTask; });
        UiTestPump.Run(service.ShowAsync(new("project.json", null, Guid.NewGuid()), default));
        Assert.That(presented, Is.True);
    }
}
