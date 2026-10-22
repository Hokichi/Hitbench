using Soundie.Services.Dialogs;
using System.Windows.Threading;

namespace Soundie.Test.Presentation;

[Apartment(ApartmentState.STA)]
public sealed class ProjectRemovalDialogServiceTests
{
    [Test]
    public void MissingOwnerCancelsWithoutShowingPopup()
    {
        var result = new ProjectRemovalDialogService(() => null, Dispatcher.CurrentDispatcher).ConfirmAsync("Example", default);
        UiTestPump.Run(result);
        Assert.That(result.Result, Is.False);
    }
}
