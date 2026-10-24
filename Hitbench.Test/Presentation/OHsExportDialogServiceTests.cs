using CommunityToolkit.Mvvm.Messaging;
using System.Windows.Threading;
using Hitbench.Services.Dialogs;
using Hitbench.ViewModels.Popups;
namespace Hitbench.Test.Presentation;
[Apartment(ApartmentState.STA), NonParallelizable]
public class OHsExportDialogServiceTests
{
    [Test]
    public void PresentationSubstituteCompletesAndDeactivatesOwnedViewModel()
    {
        OHsExportPopupVM? shown = null;
        var service = new OHsExportDialogService(() => null, Dispatcher.CurrentDispatcher,
            request => new(request, new StubOHsExportService(), new StubOHsExportDirectoryLauncher(), new WeakReferenceMessenger()),
            (vm, _) => { shown = vm; return Task.CompletedTask; });
        UiTestPump.Run(service.ShowAsync(new(Guid.NewGuid(), "project.json", "map", "song", "{}", new("Hitsounds", 1, ".ogg", ".ogg", false)), default));
        Assert.That(shown, Is.Not.Null); Assert.That(shown!.CanExport, Is.False);
    }
}
