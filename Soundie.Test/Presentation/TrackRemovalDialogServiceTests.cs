using System.Windows;
using Soundie.Services.Dialogs;

namespace Soundie.Test.Presentation;

[Apartment(ApartmentState.STA)]
public sealed class TrackRemovalDialogServiceTests
{
    [Test]
    public void MissingOwnerCancelsWithoutOpeningPopup()
    {
        var service = new TrackRemovalDialogService(() => null, System.Windows.Threading.Dispatcher.CurrentDispatcher);
        var result = service.ConfirmAsync(CancellationToken.None);
        UiTestPump.Run(result);
        Assert.That(result.Result, Is.False);
    }
}
