using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.ViewModels.Popups;
using osuHitsounder.Views.Popups;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class ProjectNamePopupIntegrationTests
{
    [Test]
    public void InputIsFocusableShowsPlaceholderValidatesAndConfirms()
    {
        EnsureResources();
        var vm = new ProjectNamePopupVM("Artist - Title", new HashSet<string>());
        bool? completion = null;
        var popup = new ProjectNamePopup(vm, accepted => completion = accepted);
        using var source = new InMemoryPresentationSource();
        UiTestPump.Layout((FrameworkElement)popup.Content, 420, 300);
        try
        {
            var input = (TextBox)popup.FindName("ProjectNameTextBox");
            Assert.That(input, Is.TypeOf<TextBox>());
            Assert.That(input.Style, Is.SameAs(popup.FindResource("ProjectNamePopupInputStyle")));
            var watermark = (TextBlock)popup.FindName("ProjectNameWatermark");
            var validation = (TextBlock)popup.FindName("ProjectNameValidation");
            var cancel = (BalloonButton)popup.FindName("CancelButton");
            Assert.That(cancel.Style, Is.SameAs(popup.FindResource("OHsBalloonDestructiveActionStyle")));
            var create = (BalloonButton)popup.FindName("CreateButton");
            Assert.That(create.Style, Is.SameAs(popup.FindResource("OHsBalloonConfirmActionStyle")));
            Assert.Multiple(() =>
            {
                Assert.That(input.Focusable, Is.True);
                Assert.That(input.IsTabStop, Is.False);
                Assert.That(input.Text, Is.Empty);
                Assert.That(watermark.Text, Is.EqualTo("Artist - Title"));
                Assert.That(watermark.Visibility, Is.EqualTo(Visibility.Visible));
                Assert.That(cancel.ButtonText, Is.EqualTo("Cancel"));
                Assert.That(create.ButtonText, Is.EqualTo("Create"));
            });

            vm.ProjectName = "CON";
            UiTestPump.Layout((FrameworkElement)popup.Content, 420, 300);
            popup.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            Assert.Multiple(() =>
            {
                Assert.That(create.IsEnabled, Is.False);
                Assert.That(validation.Text, Is.EqualTo("Project name is reserved by Windows."));
            });

            vm.ProjectName = "Custom";
            UiTestPump.Layout((FrameworkElement)popup.Content, 420, 300);
            popup.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            popup.RaiseEvent(CreateKeyEvent(source, Key.Enter));

            Assert.That(completion, Is.True);
            Assert.That(popup.Result, Is.EqualTo("Custom"));
        }
        finally
        {
            popup.Close();
        }
    }

    [Test]
    public void InputRejectsInvalidTypingFiltersPasteAndCancelReturnsNull()
    {
        EnsureResources();
        bool? completion = null;
        var popup = new ProjectNamePopup(new ProjectNamePopupVM("Artist - Title", new HashSet<string>()),
            accepted => completion = accepted);
        using var source = new InMemoryPresentationSource();
        UiTestPump.Layout((FrameworkElement)popup.Content, 420, 300);
        try
        {
            var input = (TextBox)popup.FindName("ProjectNameTextBox");
            Assert.That(input, Is.TypeOf<TextBox>());
            Assert.That(input.Style, Is.SameAs(popup.FindResource("ProjectNamePopupInputStyle")));
            var composition = new TextComposition(InputManager.Current, input, ":");
            var typing = new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition)
            {
                RoutedEvent = TextCompositionManager.PreviewTextInputEvent
            };
            input.RaiseEvent(typing);
            Assert.That(typing.Handled, Is.True);

            input.Text = "A";
            input.CaretIndex = 1;
            var paste = new DataObjectPastingEventArgs(
                new DataObject(DataFormats.UnicodeText, ":B/"), false, DataFormats.UnicodeText);
            input.RaiseEvent(paste);
            Assert.That(input.Text, Is.EqualTo("AB"));

            var cancel = (BalloonButton)popup.FindName("CancelButton");
            Assert.That(cancel.Style, Is.SameAs(popup.FindResource("OHsBalloonDestructiveActionStyle")));
            popup.RaiseEvent(CreateKeyEvent(source, Key.Escape));

            Assert.That(completion, Is.False);
            Assert.That(popup.Result, Is.Null);
        }
        finally
        {
            popup.Close();
        }
    }

    [Test]
    public void DisabledCreateIgnoresEnterUntilEscapeCancels()
    {
        EnsureResources();
        var vm = new ProjectNamePopupVM("Artist - Title", new HashSet<string>()) { ProjectName = "CON" };
        bool? completion = null;
        var popup = new ProjectNamePopup(vm, accepted => completion = accepted);
        using var source = new InMemoryPresentationSource();
        UiTestPump.Layout((FrameworkElement)popup.Content, 420, 300);
        try
        {
            var create = (BalloonButton)popup.FindName("CreateButton");
            Assert.That(create.Style, Is.SameAs(popup.FindResource("OHsBalloonConfirmActionStyle")));
            Assert.That(create.IsEnabled, Is.False);
            popup.RaiseEvent(CreateKeyEvent(source, Key.Enter));
            Assert.That(completion, Is.Null);
            Assert.That(popup.Result, Is.Null);
            popup.RaiseEvent(CreateKeyEvent(source, Key.Escape));

            Assert.That(completion, Is.False);
            Assert.That(popup.Result, Is.Null);
        }
        finally
        {
            popup.Close();
        }
    }

    private static KeyEventArgs CreateKeyEvent(PresentationSource source, Key key) => new(
        Keyboard.PrimaryDevice,
        source,
        Environment.TickCount,
        key)
    {
        RoutedEvent = Keyboard.PreviewKeyDownEvent
    };

    private static void EnsureResources()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Clear();
        UiTestPump.EnsureFontResources(app);
        foreach (var resource in new[]
        {
            "Themes/Dark.xaml", "Converters/Converters.xaml", "Icons.xaml",
            "Styles/TextBlock.xaml", "Styles/TextBox.xaml", "Styles/Button.xaml",
            "Styles/Border.xaml", "Styles/ContentPresenter.xaml", "Styles/Window.xaml", "Styles/Popup.xaml",
            "Styles/ScrollViewer.xaml"
        })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"/osuHitsounder;component/Resources/{resource}", UriKind.Relative)
            });
    }
}
