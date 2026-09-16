using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
    public void ModalFocusesInputShowsPlaceholderValidatesAndConfirms()
    {
        EnsureResources();
        var invoker = new Button { Content = "Open" };
        var owner = new Window { Content = invoker, Width = 640, Height = 360, ShowInTaskbar = false };
        owner.Show();
        invoker.Focus();
        var vm = new ProjectNamePopupVM("Artist - Title", new HashSet<string>());
        var popup = new ProjectNamePopup(vm) { Owner = owner };
        popup.Loaded += (_, _) => popup.Dispatcher.BeginInvoke(() =>
        {
            var input = (TextBox)popup.FindName("ProjectNameTextBox");
            var watermark = (TextBlock)popup.FindName("ProjectNameWatermark");
            var validation = (TextBlock)popup.FindName("ProjectNameValidation");
            var cancel = (BalloonButton)popup.FindName("CancelButton");
            var create = (BalloonButton)popup.FindName("CreateButton");
            Assert.Multiple(() =>
            {
                Assert.That(Keyboard.FocusedElement, Is.SameAs(input));
                Assert.That(input.Text, Is.Empty);
                Assert.That(watermark.Text, Is.EqualTo("Artist - Title"));
                Assert.That(watermark.Visibility, Is.EqualTo(Visibility.Visible));
                Assert.That(cancel.ButtonIcon, Is.SameAs(popup.FindResource("Ban")));
                Assert.That(cancel.ButtonText, Is.EqualTo("Cancel"));
                Assert.That(create.ButtonIcon, Is.SameAs(popup.FindResource("PlusSolid")));
                Assert.That(create.ButtonText, Is.EqualTo("Create"));
            });

            vm.ProjectName = "CON";
            popup.UpdateLayout();
            Assert.Multiple(() =>
            {
                Assert.That(create.IsEnabled, Is.False);
                Assert.That(input.BorderBrush, Is.SameAs(popup.FindResource("Brush.Border.Danger")));
                Assert.That(validation.Text, Is.EqualTo("Project name is reserved by Windows."));
            });

            vm.ProjectName = "Custom";
            popup.UpdateLayout();
            popup.RaiseEvent(CreateKeyEvent(popup, Key.Enter));
        }, DispatcherPriority.ApplicationIdle);

        try
        {
            Assert.That(popup.ShowDialog(), Is.True);
            Assert.That(popup.Result, Is.EqualTo("Custom"));
            Assert.That(Keyboard.FocusedElement, Is.SameAs(invoker));
        }
        finally
        {
            if (popup.IsVisible) popup.Close();
            owner.Close();
        }
    }

    [Test]
    public void InputRejectsInvalidTypingFiltersPasteAndCancelReturnsNull()
    {
        EnsureResources();
        var owner = new Window { Width = 640, Height = 360, ShowInTaskbar = false };
        owner.Show();
        var popup = new ProjectNamePopup(new ProjectNamePopupVM("Artist - Title", new HashSet<string>()))
        {
            Owner = owner
        };
        popup.Loaded += (_, _) => popup.Dispatcher.BeginInvoke(() =>
        {
            var input = (TextBox)popup.FindName("ProjectNameTextBox");
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
            Assert.That(cancel.ButtonIcon, Is.SameAs(popup.FindResource("Ban")));
            popup.RaiseEvent(CreateKeyEvent(popup, Key.Escape));
        }, DispatcherPriority.ApplicationIdle);

        try
        {
            Assert.That(popup.ShowDialog(), Is.False);
            Assert.That(popup.Result, Is.Null);
        }
        finally
        {
            if (popup.IsVisible) popup.Close();
            owner.Close();
        }
    }

    [Test]
    public void DisabledCreateIgnoresEnterUntilEscapeCancels()
    {
        EnsureResources();
        var owner = new Window { Width = 640, Height = 360, ShowInTaskbar = false };
        owner.Show();
        var vm = new ProjectNamePopupVM("Artist - Title", new HashSet<string>()) { ProjectName = "CON" };
        var popup = new ProjectNamePopup(vm) { Owner = owner };
        popup.Loaded += (_, _) => popup.Dispatcher.BeginInvoke(() =>
        {
            var create = (BalloonButton)popup.FindName("CreateButton");
            Assert.That(create.IsEnabled, Is.False);
            popup.RaiseEvent(CreateKeyEvent(popup, Key.Enter));
            Assert.That(popup.IsVisible, Is.True);
            Assert.That(popup.Result, Is.Null);
            popup.RaiseEvent(CreateKeyEvent(popup, Key.Escape));
        }, DispatcherPriority.ApplicationIdle);

        try
        {
            Assert.That(popup.ShowDialog(), Is.False);
            Assert.That(popup.Result, Is.Null);
        }
        finally
        {
            if (popup.IsVisible) popup.Close();
            owner.Close();
        }
    }

    private static KeyEventArgs CreateKeyEvent(UIElement target, Key key) => new(
        Keyboard.PrimaryDevice,
        PresentationSource.FromVisual(target),
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
