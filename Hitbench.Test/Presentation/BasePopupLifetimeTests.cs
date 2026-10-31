using System.Windows;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class BasePopupLifetimeTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void PopupLifetimeDoesNotCloseOwner(bool replaceOwner)
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var app = Application.Current;
        var ownsApplicationDispatcher = app.Dispatcher.CheckAccess();
        var original = ownsApplicationDispatcher ? app.MainWindow : null;
        var owner = new Window();
        var replacement = new Window();
        var closed = 0; owner.Closed += (_, _) => closed++;
        var popup = new TestBasePopup();
        try
        {
            if (ownsApplicationDispatcher) app.MainWindow = owner;
            popup.LoadForTest();
            if (ownsApplicationDispatcher) Assert.That(app.MainWindow, Is.SameAs(popup));
            if (replaceOwner && ownsApplicationDispatcher) app.MainWindow = replacement;
            if (!popup.CloseForTest()) Assert.That(popup.CloseForTest(), Is.True);
            popup.ClosedForTest();
            if (ownsApplicationDispatcher) Assert.That(app.MainWindow, Is.SameAs(replaceOwner ? replacement : owner));
            Assert.That(closed, Is.Zero);
            if (ownsApplicationDispatcher) Assert.That(app.ShutdownMode, Is.EqualTo(ShutdownMode.OnExplicitShutdown));
        }
        finally { if (ownsApplicationDispatcher) app.MainWindow = original; popup.Close(); owner.Close(); replacement.Close(); }
    }
}
