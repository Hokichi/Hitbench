using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.DataModels;
using Hitbench.Resources.Messages;
using Hitbench.ViewModels.Popups;
namespace Hitbench.Test.Presentation;

public sealed class ManageSamplesExternalImportTests
{
    [Test]
    public async Task ReservationAndExternalRefreshRespectSessionAndLifetime()
    {
        var context = new SampleManagementContext("project.json", null, Guid.NewGuid()); var messenger = new WeakReferenceMessenger(); var files = new StubSampleFileService(new[] { "a.wav" }); var preview = new StubSamplePreviewService();
        var vm = new ManageSamplesPopupVM(context, files, new(files), new StubSampleFileDialogService(Array.Empty<string>()), new StubSampleRemovalDialogService(), messenger, new ImmediateUiScheduler(), preview);
        await vm.InitializeAsync(default);
        vm.EditCommand.ExecuteAsync(vm.Entries[0]).GetAwaiter().GetResult();
        Assert.That(await messenger.Send(new OHsPrepareSampleDropImportMessage(context)).Response, Is.False); vm.CancelEditCommand.Execute(null);
        Assert.That(await messenger.Send(new OHsPrepareSampleDropImportMessage(context)).Response, Is.True); Assert.That(vm.CanClose, Is.False);
        messenger.Send(new ProjectSamplesChangedMessage(context, new[] { "a.wav", "b.ogg", "piano/C4.wav" })); Assert.That(vm.Entries.Select(e => e.FileName), Is.EqualTo(new[] { "a.wav", "b.ogg" }));
        messenger.Send(new OHsSampleDropImportFinishedMessage(context)); Assert.That(vm.CanClose, Is.True);
        await vm.DeactivateAsync(); Assert.That(messenger.Send(new OHsPrepareSampleDropImportMessage(context)).HasReceivedResponse, Is.False);
    }
}
