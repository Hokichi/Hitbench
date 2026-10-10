using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using osuHitsounder.Resources.CustomControls;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public sealed class OHsMessageBoxPopupIntegrationTests
{
    [Test]
    public void ButtonSetsUseAssignedBalloonStylesTextAndResults()
    {
        EnsureResources();

        AssertButtons(MessageBoxButton.OK,
            ("PrimaryButton", "OK", MessageBoxResult.OK),
            null,
            null);
        AssertButtons(MessageBoxButton.OKCancel,
            ("PrimaryButton", "OK", MessageBoxResult.OK),
            ("SecondaryButton", "Cancel", MessageBoxResult.Cancel),
            null);
        AssertButtons(MessageBoxButton.YesNo,
            ("PrimaryButton", "Yes", MessageBoxResult.Yes),
            ("SecondaryButton", "No", MessageBoxResult.No),
            null);
        AssertButtons(MessageBoxButton.YesNoCancel,
            ("PrimaryButton", "Yes", MessageBoxResult.Yes),
            ("SecondaryButton", "No", MessageBoxResult.No),
            ("TertiaryButton", "Cancel", MessageBoxResult.Cancel));
    }

    [Test]
    public void UnmodifiedEnterInvokesConfiguredDefaultAction()
    {
        EnsureResources();
        var popup = new OHsMessageBoxPopup("Continue?", "Question", MessageBoxButton.YesNo);

        try
        {
            using var source = new InMemoryPresentationSource();
            popup.RaiseEvent(CreateKeyEvent(source, Key.Enter));
            Assert.That(popup.Result, Is.EqualTo(MessageBoxResult.Yes));
        }
        finally
        {
            popup.Close();
        }
    }

    [Test]
    public void DestructiveConfirmationUsesSafeDefaultAndDangerousAffirmative()
    {
        EnsureResources();
        var popup = new OHsMessageBoxPopup("Delete data?", "Remove track?",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning, destructiveConfirmation: true);
        try
        {
            var remove = (BalloonButton)popup.FindName("PrimaryButton");
            var cancel = (BalloonButton)popup.FindName("SecondaryButton");
            Assert.That(remove.ButtonText, Is.EqualTo("Remove Track"));
            Assert.That(remove.Style, Is.SameAs(popup.FindResource("OHsBalloonDestructiveActionStyle")));
            Assert.That(cancel.Style, Is.SameAs(popup.FindResource("OHsBalloonNeutralActionStyle")));
            using var source = new InMemoryPresentationSource();
            popup.RaiseEvent(CreateKeyEvent(source, Key.Enter));
            Assert.That(popup.Result, Is.EqualTo(MessageBoxResult.Cancel));
        }
        finally { popup.Close(); }
    }

    [TestCase(MessageBoxButton.OK, MessageBoxResult.OK)]
    [TestCase(MessageBoxButton.OKCancel, MessageBoxResult.Cancel)]
    [TestCase(MessageBoxButton.YesNo, MessageBoxResult.No)]
    [TestCase(MessageBoxButton.YesNoCancel, MessageBoxResult.Cancel)]
    public void EscapeUsesConfiguredDismissResult(MessageBoxButton buttons, MessageBoxResult expected)
    {
        EnsureResources();
        var popup = new OHsMessageBoxPopup("Continue?", "Question", buttons);

        try
        {
            using var source = new InMemoryPresentationSource();
            popup.RaiseEvent(CreateKeyEvent(source, Key.Escape));
            Assert.That(popup.Result, Is.EqualTo(expected));
        }
        finally
        {
            popup.Close();
        }
    }

    private static void AssertButtons(
        MessageBoxButton buttons,
        (string Name, string Text, MessageBoxResult Result) primary,
        (string Name, string Text, MessageBoxResult Result)? secondary,
        (string Name, string Text, MessageBoxResult Result)? tertiary)
    {
        var popup = new OHsMessageBoxPopup("Message", "Title", buttons);
        AssertButton(popup, primary, "OHsBalloonConfirmActionStyle");
        AssertOptionalButton(popup, secondary, "SecondaryButton");
        AssertOptionalButton(popup, tertiary, "TertiaryButton");
        popup.Close();
    }

    private static void AssertOptionalButton(
        OHsMessageBoxPopup popup,
        (string Name, string Text, MessageBoxResult Result)? expected,
        string buttonName)
    {
        if (expected is { } value)
            AssertButton(popup, value, "OHsBalloonDestructiveActionStyle");
        else
            Assert.That(((BalloonButton)popup.FindName(buttonName)).Visibility, Is.EqualTo(Visibility.Collapsed));
    }

    private static void AssertButton(
        OHsMessageBoxPopup popup,
        (string Name, string Text, MessageBoxResult Result) expected,
        string styleKey)
    {
        var button = (BalloonButton)popup.FindName(expected.Name);
        Assert.Multiple(() =>
        {
            Assert.That(button, Is.TypeOf<BalloonButton>());
            Assert.That(button.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(button.ButtonText, Is.EqualTo(expected.Text));
            Assert.That(button.Tag, Is.EqualTo(expected.Result));
            Assert.That(button.Style, Is.SameAs(popup.FindResource(styleKey)));
            Assert.That(AutomationProperties.GetName(button), Is.EqualTo(expected.Text));
        });
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
                     "Styles/TextBlock.xaml", "Styles/Button.xaml", "Styles/Border.xaml",
                     "Styles/ContentPresenter.xaml", "Styles/Window.xaml", "Styles/Popup.xaml"
                 })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"/osuHitsounder;component/Resources/{resource}", UriKind.Relative)
            });
    }
}
