using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.Messages;
using Hitbench.ViewModels.Shell.HomePage;
using Hitbench.Views.Pages;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class OHsAudioBusyPresentationTests
{
    [Test]
    public void BusyPresentationTracksMatchingOperationsAndClose()
    {
        new SongsResponsiveLayoutTests().SetUp();
        var messenger = new WeakReferenceMessenger(); var settings = new StubAppSettingsService();
        var vm = new HomePageVM(new StubHomeFolderService(), settings, new StubInstallationDialogService(),
            new StubProjectCreationDialogService(), new ImmediateUiScheduler(),
            new StubProjectFileService((_, _, _) => Task.FromResult("saved.json")), messenger);
        var window = TestShell.Create(new HomePage(vm), messenger, WorkstationTestPage.Create, vm, settings);
        try
        {
            var toast = (Border)window.FindName("AudioBusyToast");
            Assert.That(toast, Is.TypeOf<Border>());
            Assert.That(toast.Style, Is.SameAs(window.FindResource("OHsMainWindowAudioBusyToastStyle")));
            Assert.That(((TextBlock)toast.Child).Style, Is.SameAs(window.FindResource("OHsMainWindowAudioBusyTextStyle")));
            Assert.That(AutomationProperties.GetLiveSetting(toast), Is.EqualTo(AutomationLiveSetting.Polite));
            Assert.That(toast.Focusable, Is.False); Assert.That(toast.IsHitTestVisible, Is.False);
            var first = Guid.NewGuid(); var second = Guid.NewGuid();
            messenger.Send(new OHsAudioBusyChangedMessage(first, true, "first"));
            Assert.That(window.IsAudioBusy, Is.True); Assert.That(window.AudioBusyText, Is.EqualTo("first"));
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
            Assert.That(toast.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(((ContentControl)window.FindName("PageHost")).IsEnabled, Is.False);
            messenger.Send(new OHsAudioBusyChangedMessage(second, true, "second"));
            messenger.Send(new OHsAudioBusyChangedMessage(first, false, "first"));
            Assert.That(window.IsAudioBusy, Is.True); Assert.That(window.AudioBusyText, Is.EqualTo("second"));
            messenger.Send(new OHsAudioBusyChangedMessage(Guid.NewGuid(), false, "stale"));
            Assert.That(window.IsAudioBusy, Is.True);
            messenger.Send(new OHsAudioBusyChangedMessage(second, false, "second"));
            Assert.That(window.IsAudioBusy, Is.False);
            Assert.That(toast.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(((ContentControl)window.FindName("PageHost")).IsEnabled, Is.True);
            messenger.Send(new OHsAudioBusyChangedMessage(first, true, "again"));
            window.Close();
            messenger.Send(new OHsAudioBusyChangedMessage(second, true, "late"));
            Assert.That(window.IsAudioBusy, Is.False);
        }
        finally { window.Close(); }
    }
}
