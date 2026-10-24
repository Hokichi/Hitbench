using System.Windows.Threading;
using Onset.Services.Dialogs;
using Onset.Resources.CustomControls;

namespace Onset.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public sealed class SampleRemovalDialogServiceTests
{
    [Test] public void PreCancelledRequestCannotConfirm()
    {
        var service = new SampleRemovalDialogService(() => null, Dispatcher.CurrentDispatcher,
            (_, _) => throw new AssertionException("Cancelled request presented a dialog"));
        var task = service.ConfirmAsync("hit.wav", new(true)); UiTestPump.Run(task); Assert.That(task.GetAwaiter().GetResult(), Is.False);
    }
    [Test] public void CancellationDuringConfirmationCannotDelete()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        using var cancellation = new CancellationTokenSource();
        var service = new SampleRemovalDialogService(() => null, Dispatcher.CurrentDispatcher,
            (popup, _) =>
            {
                Assert.That(((BalloonButton)popup.FindName("PrimaryButton")).ButtonText, Is.EqualTo("Delete Sample"));
                cancellation.Cancel(); return Task.FromResult(true);
            });
        var task = service.ConfirmAsync("hit.wav", cancellation.Token); UiTestPump.Run(task); Assert.That(task.GetAwaiter().GetResult(), Is.False);
    }
}

