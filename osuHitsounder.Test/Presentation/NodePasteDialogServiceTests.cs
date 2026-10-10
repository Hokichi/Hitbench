using osuHitsounder.Services.Dialogs;
using osuHitsounder.ViewModels.Popups;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
public class NodePasteDialogServiceTests
{
    [Test]
    public void MissingOwnerCancelsChoiceWithoutOpeningDialog()
    {
        var service = new NodePasteDialogService(() => null, System.Windows.Threading.Dispatcher.CurrentDispatcher, () => new NodePastePopupVM());
        var choice = service.ChooseTimingAsync(CancellationToken.None);
        UiTestPump.Run(choice);
        Assert.That(choice.Result, Is.Null);
        UiTestPump.Run(service.ShowCollisionAsync(CancellationToken.None));
    }
}
