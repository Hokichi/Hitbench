using System.Runtime.ExceptionServices;
using System.Windows;
using Onset.Resources.CustomControls;

namespace Onset.Test.Presentation;

public sealed class OHsMessageBoxOwnerTests
{
    [Test]
    public void ResolveOwnerForDialog_ReturnsNullWhenResolvedOwnerIsDialogItself()
    {
        RunInSta(() =>
        {
            var dialog = new Window();

            var owner = OHsMessageBox.ResolveOwnerForDialog(dialog, dialog, () => null);

            Assert.That(owner, Is.Null);
        });
    }

    [Test]
    public void ResolveOwnerForDialog_UsesFallbackOwnerWhenRequestedOwnerIsNull()
    {
        RunInSta(() =>
        {
            var dialog = new Window();
            var fallback = new Window();

            var owner = OHsMessageBox.ResolveOwnerForDialog(dialog, null, () => fallback);

            Assert.That(owner, Is.SameAs(fallback));
        });
    }

    private static void RunInSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
