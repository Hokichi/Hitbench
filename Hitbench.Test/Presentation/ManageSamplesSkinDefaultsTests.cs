using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.DataModels;
using Hitbench.Resources.Messages;
using Hitbench.ViewModels.Popups;

namespace Hitbench.Test.Presentation;

public class ManageSamplesSkinDefaultsTests
{
    [Test]
    public async Task DrawerUsesAuthoritativeStateAndKeepsSkinRowsReadOnly()
    {
        var messenger = new WeakReferenceMessenger(); var context = new SampleManagementContext("project.json", null, Guid.NewGuid());
        var files = new StubSampleFileService(["a.wav"]);
        var recipient = new object(); var changes = new List<bool>();
        messenger.Register<OHsSkinDefaultsStateRequestedMessage>(recipient, (_, _) => messenger.Send(new OHsSkinDefaultsStateChangedMessage(context, true, "skin", ["SkinDefault/drum-hitclap.wav"], false)));
        messenger.Register<OHsSetAllowDefaultsRequestedMessage>(recipient, (_, m) => changes.Add(m.AllowDefaults));
        var vm = new ManageSamplesPopupVM(context, files, new(files), new StubSampleFileDialogService([]), new StubSampleRemovalDialogService(), messenger, new ImmediateUiScheduler());
        try
        {
            await vm.InitializeAsync(default);
            Assert.That(vm.AllowDefaults, Is.True); Assert.That(vm.SkinDefaultEntries, Has.Count.EqualTo(1));
            Assert.That(vm.DeleteCommand.CanExecute(vm.SkinDefaultEntries[0]), Is.False);
            Assert.That(vm.EditCommand.CanExecute(vm.SkinDefaultEntries[0]), Is.False);
            vm.AllowDefaults = false; Assert.That(changes, Is.EqualTo(new[] { false }));
            messenger.Send(new OHsSkinDefaultsStateChangedMessage(context with { SessionId = Guid.NewGuid() }, true, "other", [], false));
            Assert.That(vm.AllowDefaults, Is.False);
        }
        finally { await vm.DeactivateAsync(); }
    }
}
