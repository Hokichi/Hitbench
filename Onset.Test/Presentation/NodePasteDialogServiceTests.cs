using Onset.Services.Dialogs;

namespace Onset.Test.Presentation;

[Apartment(ApartmentState.STA)]
public class NodePasteDialogServiceTests
{
    [Test]
    public void MissingOwnerCompletesCollisionWithoutOpeningDialog()
    {
        var service = new NodePasteDialogService(() => null, System.Windows.Threading.Dispatcher.CurrentDispatcher);
        var collision = service.ShowCollisionAsync(CancellationToken.None);
        UiTestPump.Run(collision);
        Assert.That(collision.IsCompletedSuccessfully, Is.True);
    }
    [Test]
    public void CancelledCollisionDoesNotOpenDialog()
    {
        var service = new NodePasteDialogService(() => null, System.Windows.Threading.Dispatcher.CurrentDispatcher);
        var task = service.ShowCollisionAsync(new CancellationToken(true));
        Assert.That(() => UiTestPump.Run(task), Throws.InstanceOf<OperationCanceledException>());
    }
}
