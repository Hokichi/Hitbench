using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.ViewModels.Popups;
using osuHitsounder.Views.Popups;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)] [NonParallelizable]
public sealed class ManageSamplesPopupIntegrationTests
{
    [Test] public void AddActionAndRowsBindToManagerCommands()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var files = new StubSampleFileService([]);
        var vm = new ManageSamplesPopupVM(new("project.json", null, Guid.NewGuid()), files, new(files),
            new StubSampleFileDialogService(Array.Empty<string>()), new StubSampleRemovalDialogService(), new WeakReferenceMessenger(), new ImmediateUiScheduler());
        var popup = new ManageSamplesPopup(vm);
        try
        {
            UiTestPump.Run(popup.InitializeAsync(default)); using var view = new InMemoryView(popup, scheduleLayout: false);
            var add = (BalloonButton)popup.FindName("AddNewSampleButton");
            Assert.That(add.Command, Is.SameAs(vm.AddNewSampleCommand));
            Assert.That(add.ButtonIcon, Is.SameAs(popup.FindResource("PlusSolid")));
            Assert.That(add.IsEnabled, Is.True);
        }
        finally { UiTestPump.Run(popup.DeactivateAsync()); popup.Close(); }
    }
}
